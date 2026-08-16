using System.Data;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.Core;
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
    Task SendAsync(Guid workspaceId, Guid callerUserId, Guid quotationId, List<Guid> recipientPersonIds, string appBaseUrl);
}

public class QuotationService : IQuotationService
{
    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly IPdfService _pdfService;
    private readonly IEmailService _emailService;
    private readonly ILogger<QuotationService> _logger;

    public QuotationService(ApplicationDbContext context, IScopedAccessService access, IPdfService pdfService, IEmailService emailService, ILogger<QuotationService> logger)
    {
        _context = context;
        _access = access;
        _pdfService = pdfService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<Quotation> CreateAsync(Guid workspaceId, Guid callerUserId, QuotationRequest request)
    {
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, request.JobId, "create");
        await EnsureClientHoldsBillingRoleOnJobAsync(request.ClientId, request.JobId);
        ValidateLineItems(request.LineItems);

        var quotation = new Quotation
        {
            Id = Guid.NewGuid(),
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

        _logger.LogInformation("Quotation {QuotationId} ({Number}) created on job {JobId} by {UserId}", quotation.Id, quotation.Number, quotation.JobId, callerUserId);
        return quotation;
    }

    public async Task<List<Quotation>> SearchAsync(Guid workspaceId, Guid callerUserId, Guid? clientId)
    {
        await _access.EnsureListAllowedAsync(callerUserId, workspaceId);

        var quotations = _context.Quotations.Where(q => q.Job.WorkspaceId == workspaceId);
        if (!await _access.HasViewAllAsync(callerUserId, "job", workspaceId))
        {
            var accessibleJobIds = (await _access.GetAccessibleJobsAsync(callerUserId))
                .Where(j => j.WorkspaceId == workspaceId).Select(j => j.JobId).ToHashSet();
            quotations = quotations.Where(q => accessibleJobIds.Contains(q.JobId));
        }
        if (clientId.HasValue)
            quotations = quotations.Where(q => q.ClientId == clientId.Value);

        return await quotations.OrderByDescending(q => q.CreatedAt).ToListAsync();
    }

    public async Task<Quotation> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid quotationId)
    {
        var quotation = await FindQuotationAsync(workspaceId, quotationId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, quotation.JobId, "view");
        return quotation;
    }

    public async Task<Quotation> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid quotationId, QuotationRequest request)
    {
        var quotation = await FindQuotationAsync(workspaceId, quotationId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, quotation.JobId, "edit");
        await EnsureClientHoldsBillingRoleOnJobAsync(request.ClientId, request.JobId);
        ValidateLineItems(request.LineItems);

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
        var quotation = await FindQuotationAsync(workspaceId, quotationId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, quotation.JobId, "delete");

        quotation.IsActive = false;
        quotation.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<Invoice> ConvertToInvoiceAsync(Guid workspaceId, Guid callerUserId, Guid quotationId, ConvertQuotationRequest request)
    {
        var quotation = await FindQuotationAsync(workspaceId, quotationId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, quotation.JobId, "edit");
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, quotation.JobId, "create");

        if (quotation.Status is not ("Draft" or "Sent"))
            throw new ValidationException($"Cannot convert a quotation with status '{quotation.Status}'.");

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
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

    public async Task SendAsync(Guid workspaceId, Guid callerUserId, Guid quotationId, List<Guid> recipientPersonIds, string appBaseUrl)
    {
        var quotation = await FindQuotationAsync(workspaceId, quotationId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, quotation.JobId, "edit");

        if (recipientPersonIds.Count == 0)
            throw new ValidationException("At least one recipient is required.");

        var recipients = await _context.People
            .Where(p => recipientPersonIds.Contains(p.Id) && p.IsActive)
            .ToListAsync();
        if (recipients.Count != recipientPersonIds.Count)
            throw new NotFoundException("One or more recipients not found.");

        var eligiblePersonIds = await _context.UserAccesses
            .Include(ua => ua.User)
            .Where(ua => ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == quotation.JobId)
            .Where(ua => ua.Role.Name == Constants.SystemRoles.Client || ua.Role.Name == Constants.SystemRoles.Finance)
            .Select(ua => ua.User.PersonId)
            .ToListAsync();

        var ineligible = recipientPersonIds.Except(eligiblePersonIds).ToList();
        if (ineligible.Count > 0)
            throw new ValidationException("Every recipient must hold Client or Finance access on this quotation's job.");

        var pdfBytes = _pdfService.GenerateQuotationPdf(quotation);
        var linkUrl = $"{appBaseUrl.TrimEnd('/')}/app/jobs/{quotation.JobId}";

        foreach (var recipient in recipients)
        {
            if (string.IsNullOrWhiteSpace(recipient.Email))
                continue;
            await _emailService.SendBillingDocumentAsync(recipient.Email, "Quotation", quotation.Number, linkUrl, pdfBytes, $"{quotation.Number}.pdf");
        }

        if (quotation.Status == "Draft")
        {
            quotation.Status = "Sent";
            quotation.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Quotation {QuotationId} sent to {Count} recipient(s)", quotationId, recipients.Count);
    }

    private async Task<string> NextNumberAsync(Guid workspaceId, string prefix)
    {
        var count = prefix switch
        {
            "Q" => await _context.Quotations.IgnoreQueryFilters().CountAsync(q => q.Job.WorkspaceId == workspaceId),
            "INV" => await _context.Invoices.IgnoreQueryFilters().CountAsync(i => i.Job.WorkspaceId == workspaceId),
            _ => throw new InvalidOperationException($"Unknown prefix '{prefix}'.")
        };
        return $"{prefix}-{count + 1:D4}";
    }

    /// <summary>Replaces EnsureClientExistsAsync - ClientId must resolve to a Person who holds
    /// Client or Finance UserAccess on this specific JobId, not just any active Person.</summary>
    private async Task EnsureClientHoldsBillingRoleOnJobAsync(Guid clientId, Guid jobId)
    {
        var personExists = await _context.People.AnyAsync(p => p.Id == clientId && p.IsActive);
        if (!personExists)
            throw new NotFoundException("Client not found");

        var holdsRole = await _context.UserAccesses
            .Include(ua => ua.User)
            .Where(ua => ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == jobId)
            .Where(ua => ua.Role.Name == Constants.SystemRoles.Client || ua.Role.Name == Constants.SystemRoles.Finance)
            .AnyAsync(ua => ua.User.PersonId == clientId);

        if (!holdsRole)
            throw new ValidationException("ClientId must be a Person who holds Client or Finance access on this job.");
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
        return await _context.Quotations.Include(q => q.LineItems).Include(q => q.Client)
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.Job.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Quotation not found");
    }
}
