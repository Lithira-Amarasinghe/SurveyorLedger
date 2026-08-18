using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Expense;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IExpenseService
{
    Task<Expense> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, ExpenseRequest request);
    Task<List<Expense>> GetAllAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
    Task<Expense> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId);
    Task<Expense> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId, ExpenseRequest request);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId);
    Task<Expense> UploadReceiptAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId, IFormFile file);
    Task<(Expense expense, Stream content)> GetReceiptFileAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId);
}

/// <summary>
/// StaffCost is a category here, not a separate resource (see expense-staffpayment-merge
/// design spec) - own-only visibility for that one category is filtered here in C# for
/// callers without expense.view_all, same shape StaffPaymentService used for its whole
/// resource. Every other category stays visible to anyone with expense.view.
/// </summary>
public class ExpenseService : IExpenseService
{
    private const string StaffCostCategory = "StaffCost";
    private static readonly HashSet<string> ValidCategories = new()
        { "StaffCost", "Subcontractor", "Equipment", "Material", "Transport", "Other" };
    private static readonly HashSet<string> ValidPayeeTypes = new()
        { "Salary", "Commission", "Bonus", "ProfitShare" };
    private static readonly HashSet<string> AllowedReceiptExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".jpg", ".jpeg", ".png" };

    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<ExpenseService> _logger;

    public ExpenseService(ApplicationDbContext context, IScopedAccessService access, IFileStorageService fileStorage, ILogger<ExpenseService> logger)
    {
        _context = context;
        _access = access;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<Expense> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, ExpenseRequest request)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "expense", "create", workspaceId);
        await ValidateAndNormalizePayeeAsync(request);
        var callerPersonId = await _access.ResolvePersonIdAsync(callerUserId);

        var expense = new Expense
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            JobId = jobId,
            Category = request.Category,
            Amount = request.Amount,
            Description = request.Description,
            IncurredDate = request.IncurredDate,
            PayeeId = request.PayeeId,
            PayeeType = request.PayeeType,
            RecordedBy = callerPersonId,
            CreatedAt = DateTime.UtcNow
        };

        await _context.Expenses.AddAsync(expense);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Expense {ExpenseId} recorded for job {JobId} by {UserId}", expense.Id, jobId, callerUserId);
        return await FindExpenseAsync(jobId, expense.Id);
    }

    public async Task<List<Expense>> GetAllAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "expense", "view", workspaceId);

        var query = _context.Expenses.Include(e => e.RecordedByUser).Include(e => e.Payee)
            .Where(e => e.JobId == jobId);

        if (!await _access.HasViewAllAsync(callerUserId, "expense", workspaceId))
        {
            var callerPersonId = await _access.ResolvePersonIdAsync(callerUserId);
            query = query.Where(e => e.Category != StaffCostCategory || e.PayeeId == callerPersonId);
        }

        return await query.OrderByDescending(e => e.IncurredDate).ToListAsync();
    }

    public async Task<Expense> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "expense", "view", workspaceId);
        var expense = await FindExpenseAsync(jobId, expenseId);

        if (expense.Category == StaffCostCategory && !await _access.HasViewAllAsync(callerUserId, "expense", workspaceId))
        {
            var callerPersonId = await _access.ResolvePersonIdAsync(callerUserId);
            if (expense.PayeeId != callerPersonId)
                throw new NotFoundException("Expense not found");
        }

        return expense;
    }

    public async Task<Expense> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId, ExpenseRequest request)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "expense", "edit", workspaceId);
        await ValidateAndNormalizePayeeAsync(request);
        var expense = await FindExpenseAsync(jobId, expenseId);

        expense.Category = request.Category;
        expense.Amount = request.Amount;
        expense.Description = request.Description;
        expense.IncurredDate = request.IncurredDate;
        expense.PayeeId = request.PayeeId;
        expense.PayeeType = request.PayeeType;

        await _context.SaveChangesAsync();
        return expense;
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "expense", "delete", workspaceId);
        var expense = await FindExpenseAsync(jobId, expenseId);

        if (expense.ReceiptFilePath != null)
            await _fileStorage.DeleteAsync(expense.ReceiptFilePath, CancellationToken.None);

        _context.Expenses.Remove(expense);
        await _context.SaveChangesAsync();
    }

    public async Task<Expense> UploadReceiptAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId, IFormFile file)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "expense", "edit", workspaceId);
        var expense = await FindExpenseAsync(jobId, expenseId);

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedReceiptExtensions.Contains(extension))
            throw new ValidationException($"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", AllowedReceiptExtensions)}.");
        if (file.Length > DocumentService.MaxFileSizeBytes)
            throw new ValidationException($"File exceeds the {DocumentService.MaxFileSizeBytes / (1024 * 1024)}MB size limit.");

        var storedFileName = $"{Guid.NewGuid():N}_{file.FileName}";
        var relativePath = $"{workspaceId}/jobs/{jobId}/expenses/{expenseId}/{storedFileName}";

        await using (var stream = file.OpenReadStream())
        {
            await _fileStorage.SaveAsync(stream, relativePath, CancellationToken.None);
        }

        expense.ReceiptFilePath = relativePath;
        await _context.SaveChangesAsync();
        return expense;
    }

    public async Task<(Expense expense, Stream content)> GetReceiptFileAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "expense", "view", workspaceId);
        var expense = await FindExpenseAsync(jobId, expenseId);

        if (expense.ReceiptFilePath == null)
            throw new NotFoundException("No receipt uploaded for this expense.");

        var content = await _fileStorage.OpenAsync(expense.ReceiptFilePath, CancellationToken.None);
        return (expense, content);
    }

    private async Task ValidateAndNormalizePayeeAsync(ExpenseRequest request)
    {
        if (!ValidCategories.Contains(request.Category))
            throw new ValidationException($"Category must be one of: {string.Join(", ", ValidCategories)}.");
        if (request.Amount <= 0)
            throw new ValidationException("Amount must be positive.");

        if (request.Category == StaffCostCategory)
        {
            if (request.PayeeId == null || request.PayeeType == null)
                throw new ValidationException("PayeeId and PayeeType are required when Category is StaffCost.");
            if (!ValidPayeeTypes.Contains(request.PayeeType))
                throw new ValidationException($"PayeeType must be one of: {string.Join(", ", ValidPayeeTypes)}.");
            var payeeExists = await _context.People.AnyAsync(p => p.Id == request.PayeeId && p.IsActive);
            if (!payeeExists)
                throw new ValidationException("PayeeId does not match an existing account.");
        }
        else if (request.PayeeId != null || request.PayeeType != null)
        {
            throw new ValidationException("PayeeId and PayeeType must be empty unless Category is StaffCost.");
        }
    }

    private async Task<Job> FindJobAsync(Guid workspaceId, Guid jobId)
    {
        return await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Job not found");
    }

    private async Task<Expense> FindExpenseAsync(Guid jobId, Guid expenseId)
    {
        return await _context.Expenses.Include(e => e.RecordedByUser).Include(e => e.Payee)
            .FirstOrDefaultAsync(e => e.Id == expenseId && e.JobId == jobId)
            ?? throw new NotFoundException("Expense not found");
    }
}
