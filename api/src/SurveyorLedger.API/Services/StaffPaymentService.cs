using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.StaffPayment;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IStaffPaymentService
{
    Task<StaffPayment> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, StaffPaymentRequest request);
    Task<List<StaffPayment>> GetAllAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
    Task<StaffPayment> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid staffPaymentId);
    Task<StaffPayment> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid staffPaymentId, StaffPaymentRequest request);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid staffPaymentId);
}

/// <summary>
/// Own-only visibility for callers without staffpayment.view_all: filtered here in C#
/// (same shape as ScopedAccessService.AccessibleLandIds), not in Casbin, which can only
/// answer "may this role do this action" - not "which specific rows".
/// </summary>
public class StaffPaymentService : IStaffPaymentService
{
    private static readonly HashSet<string> ValidTypes = new() { "Salary", "Commission", "Bonus", "ProfitShare" };

    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly ILogger<StaffPaymentService> _logger;

    public StaffPaymentService(ApplicationDbContext context, IScopedAccessService access, ILogger<StaffPaymentService> logger)
    {
        _context = context;
        _access = access;
        _logger = logger;
    }

    public async Task<StaffPayment> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, StaffPaymentRequest request)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "staffpayment", "create", workspaceId);
        ValidateType(request.Type);
        await ValidateUserAsync(request.UserId);

        var payment = new StaffPayment
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            JobId = jobId,
            UserId = request.UserId,
            Type = request.Type,
            Amount = request.Amount,
            PaidDate = request.PaidDate,
            Notes = request.Notes,
            RecordedBy = callerUserId,
            CreatedAt = DateTime.UtcNow
        };

        await _context.StaffPayments.AddAsync(payment);
        await _context.SaveChangesAsync();

        _logger.LogInformation("StaffPayment {StaffPaymentId} recorded for job {JobId} user {UserId} by {CallerId}", payment.Id, jobId, request.UserId, callerUserId);
        return await FindStaffPaymentAsync(jobId, payment.Id);
    }

    public async Task<List<StaffPayment>> GetAllAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "staffpayment", "view", workspaceId);

        var query = _context.StaffPayments.Include(p => p.User).Where(p => p.JobId == jobId);

        if (!await _access.HasViewAllAsync(callerUserId, "staffpayment", workspaceId))
            query = query.Where(p => p.UserId == callerUserId);

        return await query.OrderByDescending(p => p.PaidDate).ToListAsync();
    }

    public async Task<StaffPayment> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid staffPaymentId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "staffpayment", "view", workspaceId);
        var payment = await FindStaffPaymentAsync(jobId, staffPaymentId);

        if (payment.UserId != callerUserId && !await _access.HasViewAllAsync(callerUserId, "staffpayment", workspaceId))
            throw new NotFoundException("Staff payment not found");

        return payment;
    }

    public async Task<StaffPayment> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid staffPaymentId, StaffPaymentRequest request)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "staffpayment", "edit", workspaceId);
        ValidateType(request.Type);
        await ValidateUserAsync(request.UserId);
        var payment = await FindStaffPaymentAsync(jobId, staffPaymentId);

        payment.UserId = request.UserId;
        payment.Type = request.Type;
        payment.Amount = request.Amount;
        payment.PaidDate = request.PaidDate;
        payment.Notes = request.Notes;

        await _context.SaveChangesAsync();
        return await FindStaffPaymentAsync(jobId, payment.Id);
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid staffPaymentId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "staffpayment", "delete", workspaceId);
        var payment = await FindStaffPaymentAsync(jobId, staffPaymentId);

        _context.StaffPayments.Remove(payment);
        await _context.SaveChangesAsync();
    }

    private static void ValidateType(string type)
    {
        if (!ValidTypes.Contains(type))
            throw new ValidationException($"Type must be one of: {string.Join(", ", ValidTypes)}.");
    }

    private async Task ValidateUserAsync(Guid userId)
    {
        var exists = await _context.Users.AnyAsync(u => u.Id == userId && u.IsActive);
        if (!exists)
            throw new ValidationException("UserId does not match an existing account.");
    }

    private async Task<Job> FindJobAsync(Guid workspaceId, Guid jobId)
    {
        return await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Job not found");
    }

    private async Task<StaffPayment> FindStaffPaymentAsync(Guid jobId, Guid staffPaymentId)
    {
        return await _context.StaffPayments.Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == staffPaymentId && p.JobId == jobId)
            ?? throw new NotFoundException("Staff payment not found");
    }
}
