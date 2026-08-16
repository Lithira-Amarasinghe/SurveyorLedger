using System.Data;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IQuotationService
{
    Task<Quotation> CreateAsync(Guid workspaceId, Guid callerUserId, QuotationRequest request);
    Task<List<Quotation>> SearchAsync(Guid workspaceId, Guid callerUserId, Guid? clientId);
    Task<Quotation> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid quotationId);
    Task<Quotation> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid quotationId, QuotationRequest request);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid quotationId);
    Task<Invoice> ConvertToInvoiceAsync(Guid workspaceId, Guid callerUserId, Guid quotationId, ConvertQuotationRequest request);
}

public class QuotationService : IQuotationService
{
    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly ILogger<QuotationService> _logger;

    public QuotationService(ApplicationDbContext context, IScopedAccessService access, ILogger<QuotationService> logger)
    {
        _context = context;
        _access = access;
        _logger = logger;
    }

    public async Task<Quotation> CreateAsync(Guid workspaceId, Guid callerUserId, QuotationRequest request)
    {
        await _access.EnsureAllowedAsync(callerUserId, "quotation", "create", workspaceId);
        await EnsureClientExistsAsync(request.ClientId);
        ValidateLineItems(request.LineItems);

        var quotation = new Quotation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ClientId = request.ClientId,
            JobId = request.JobId,
            LineItems = ToEntities(request.LineItems),
            TaxRatePercent = request.TaxRatePercent,
            Status = request.Status ?? "Draft",
            ValidUntil = request.ValidUntil,
            RevisionNumber = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            quotation.Number = await NextNumberAsync(workspaceId, "Q");
            await _context.Quotations.AddAsync(quotation);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        _logger.LogInformation("Quotation {QuotationId} ({Number}) created in workspace {WorkspaceId} by {UserId}", quotation.Id, quotation.Number, workspaceId, callerUserId);
        return quotation;
    }

    public async Task<List<Quotation>> SearchAsync(Guid workspaceId, Guid callerUserId, Guid? clientId)
    {
        await _access.EnsureListAllowedAsync(callerUserId, workspaceId);

        var quotations = _context.Quotations.Where(q => q.WorkspaceId == workspaceId);
        if (clientId.HasValue)
            quotations = quotations.Where(q => q.ClientId == clientId.Value);

        return await quotations.OrderByDescending(q => q.CreatedAt).ToListAsync();
    }

    public async Task<Quotation> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid quotationId)
    {
        await _access.EnsureAllowedAsync(callerUserId, "quotation", "view", workspaceId);
        return await FindQuotationAsync(workspaceId, quotationId);
    }

    public async Task<Quotation> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid quotationId, QuotationRequest request)
    {
        await _access.EnsureAllowedAsync(callerUserId, "quotation", "edit", workspaceId);
        await EnsureClientExistsAsync(request.ClientId);
        ValidateLineItems(request.LineItems);
        var quotation = await FindQuotationAsync(workspaceId, quotationId);

        // Line items changed after the quote was Sent - bump RevisionNumber so
        // "revision charges" have something to point at, without a new entity.
        if (quotation.Status is "Sent" or "Accepted" or "Rejected" or "Expired")
            quotation.RevisionNumber++;

        quotation.ClientId = request.ClientId;
        quotation.JobId = request.JobId;
        // Explicitly remove old rows and mark new ones Added - EF's relationship-fixup
        // detection for OwnsMany collections misidentifies a wholesale replacement as
        // reparenting the new rows onto the old (now-deleted) ids, producing an UPDATE
        // against a row that no longer exists and a spurious DbUpdateConcurrencyException.
        foreach (var old in quotation.LineItems.ToList())
            _context.Remove(old);
        quotation.LineItems.Clear();
        foreach (var item in ToEntities(request.LineItems))
        {
            quotation.LineItems.Add(item);
            _context.Entry(item).State = EntityState.Added;
        }
        quotation.TaxRatePercent = request.TaxRatePercent;
        quotation.ValidUntil = request.ValidUntil;
        if (request.Status != null)
            quotation.Status = request.Status;
        quotation.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return quotation;
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid quotationId)
    {
        await _access.EnsureAllowedAsync(callerUserId, "quotation", "delete", workspaceId);
        var quotation = await FindQuotationAsync(workspaceId, quotationId);

        quotation.IsActive = false;
        quotation.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<Invoice> ConvertToInvoiceAsync(Guid workspaceId, Guid callerUserId, Guid quotationId, ConvertQuotationRequest request)
    {
        await _access.EnsureAllowedAsync(callerUserId, "quotation", "edit", workspaceId);
        await _access.EnsureAllowedAsync(callerUserId, "invoice", "create", workspaceId);
        var quotation = await FindQuotationAsync(workspaceId, quotationId);

        if (quotation.Status is not ("Draft" or "Sent"))
            throw new ValidationException($"Cannot convert a quotation with status '{quotation.Status}'.");

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ClientId = quotation.ClientId,
            JobId = quotation.JobId,
            QuotationId = quotation.Id,
            LineItems = quotation.LineItems.Select(li => new InvoiceLineItem { Id = Guid.NewGuid(), Description = li.Description, Quantity = li.Quantity, UnitPrice = li.UnitPrice }).ToList(),
            TaxRatePercent = quotation.TaxRatePercent,
            DiscountAmount = request.DiscountAmount,
            Status = "Draft",
            DueDate = request.DueDate,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            invoice.Number = await NextNumberAsync(workspaceId, "INV");
            await _context.Invoices.AddAsync(invoice);
            quotation.Status = "Accepted";
            quotation.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        _logger.LogInformation("Quotation {QuotationId} converted to Invoice {InvoiceId} ({Number})", quotation.Id, invoice.Id, invoice.Number);
        return invoice;
    }

    private async Task<string> NextNumberAsync(Guid workspaceId, string prefix)
    {
        var count = prefix switch
        {
            "Q" => await _context.Quotations.IgnoreQueryFilters().CountAsync(q => q.WorkspaceId == workspaceId),
            "INV" => await _context.Invoices.IgnoreQueryFilters().CountAsync(i => i.WorkspaceId == workspaceId),
            _ => throw new InvalidOperationException($"Unknown prefix '{prefix}'.")
        };
        return $"{prefix}-{count + 1:D4}";
    }

    private async Task EnsureClientExistsAsync(Guid clientId)
    {
        var exists = await _context.People.AnyAsync(p => p.Id == clientId && p.IsActive);
        if (!exists)
            throw new NotFoundException("Client not found");
    }

    private static void ValidateLineItems(List<LineItemDto> items)
    {
        if (items.Count == 0)
            throw new ValidationException("At least one line item is required.");
        if (items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0))
            throw new ValidationException("Line item quantity must be positive and unit price cannot be negative.");
    }

    private static List<QuotationLineItem> ToEntities(List<LineItemDto> items) =>
        items.Select(i => new QuotationLineItem { Id = Guid.NewGuid(), Description = i.Description.Trim(), Quantity = i.Quantity, UnitPrice = i.UnitPrice }).ToList();

    private async Task<Quotation> FindQuotationAsync(Guid workspaceId, Guid quotationId)
    {
        return await _context.Quotations.Include(q => q.LineItems).FirstOrDefaultAsync(q => q.Id == quotationId && q.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Quotation not found");
    }
}
