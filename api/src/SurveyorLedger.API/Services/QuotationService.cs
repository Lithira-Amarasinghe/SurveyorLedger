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
    Task<List<Quotation>> SearchAsync(Guid workspaceId, Guid callerUserId, Guid? clientId, Guid? jobId = null);
    Task<Quotation> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid quotationId);
    Task<Quotation> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid quotationId, QuotationRequest request);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid quotationId);
    Task SendAsync(Guid workspaceId, Guid callerUserId, Guid quotationId, List<Guid> recipientPersonIds, string appBaseUrl);
    (decimal InvoicedAmount, decimal RemainingAmount) ComputeBillingProgress(Quotation quotation);
    (decimal InvoicedAmount, decimal RemainingAmount) ComputeLineProgress(Guid jobId, Guid quotationLineId, decimal lineAmount);
}

public class QuotationService : IQuotationService
{
    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly IPdfService _pdfService;
    private readonly IEmailService _emailService;
    private readonly IInvoiceService _invoiceService;
    private readonly IMilestoneService _milestoneService;
    private readonly ILogger<QuotationService> _logger;

    public QuotationService(ApplicationDbContext context, IScopedAccessService access, IPdfService pdfService, IEmailService emailService, IInvoiceService invoiceService, IMilestoneService milestoneService, ILogger<QuotationService> logger)
    {
        _context = context;
        _access = access;
        _pdfService = pdfService;
        _emailService = emailService;
        _invoiceService = invoiceService;
        _milestoneService = milestoneService;
        _logger = logger;
    }

    public async Task<Quotation> CreateAsync(Guid workspaceId, Guid callerUserId, QuotationRequest request)
    {
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, request.JobId, "create");
        await EnsureClientHoldsBillingRoleOnJobAsync(request.ClientId, request.JobId);
        await ValidateLineItemsAsync(request.LineItems, request.JobId, null);
        ValidateTaxRate(request.TaxRatePercent);

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

    public async Task<List<Quotation>> SearchAsync(Guid workspaceId, Guid callerUserId, Guid? clientId, Guid? jobId = null)
    {
        await _access.EnsureListAllowedAsync(callerUserId, workspaceId);

        var quotations = _context.Quotations.Include(q => q.LineItems).Where(q => q.Job.WorkspaceId == workspaceId);
        if (!await _access.HasViewAllAsync(callerUserId, "job", workspaceId))
        {
            var accessibleJobIds = (await _access.GetAccessibleJobsAsync(callerUserId))
                .Where(j => j.WorkspaceId == workspaceId).Select(j => j.JobId).ToHashSet();
            quotations = quotations.Where(q => accessibleJobIds.Contains(q.JobId));
        }
        if (clientId.HasValue)
            quotations = quotations.Where(q => q.ClientId == clientId.Value);
        if (jobId.HasValue)
            quotations = quotations.Where(q => q.JobId == jobId.Value);

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
        await ValidateLineItemsAsync(request.LineItems, request.JobId, quotationId);
        ValidateTaxRate(request.TaxRatePercent);
        EnsureLineEditsPreserveInvoicedAmounts(quotation, request.LineItems);

        // Line items changed after the quote was Sent - bump RevisionNumber so
        // "revision charges" have something to point at, without a new entity.
        if (quotation.Status is "Sent" or "Accepted" or "Rejected" or "Expired")
            quotation.RevisionNumber++;

        quotation.ClientId = request.ClientId;
        quotation.JobId = request.JobId;

        // Update-in-place by Id so a line's identity survives an edit - once anything is
        // invoiced against a quotation line, InvoiceLineItem.QuotationLineId depends on that
        // Id staying stable. EnsureLineEditsPreserveInvoicedAmounts (above) already rejected
        // any edit that would remove or shrink an invoiced line below its invoiced amount.
        var currentById = quotation.LineItems.ToDictionary(li => li.Id);
        var keepIds = new HashSet<Guid>();
        foreach (var item in request.LineItems)
        {
            if (item.Id.HasValue && currentById.TryGetValue(item.Id.Value, out var existing))
            {
                existing.Description = item.Description.Trim();
                existing.Quantity = item.Quantity;
                existing.UnitPrice = item.UnitPrice;
                existing.MilestoneId = item.MilestoneId;
                keepIds.Add(existing.Id);
            }
            else
            {
                var created = new QuotationLineItem { Id = Guid.NewGuid(), Description = item.Description.Trim(), Quantity = item.Quantity, UnitPrice = item.UnitPrice, MilestoneId = item.MilestoneId };
                quotation.LineItems.Add(created);
                _context.Entry(created).State = EntityState.Added;
                keepIds.Add(created.Id);
            }
        }
        foreach (var stale in quotation.LineItems.Where(li => !keepIds.Contains(li.Id)).ToList())
        {
            quotation.LineItems.Remove(stale);
            _context.Remove(stale);
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

    /// <summary>Sums the amount billed against each of this quotation's lines
    /// (InvoiceLineItem.QuotationLineId), via the same source of truth
    /// InvoiceService.GetAmountBilledAgainstQuotationLine uses for the over-billing block.
    /// Quotation linkage lives at the line level now, not on Invoice, so this is a sum of
    /// per-line progress rather than a query against a document-level QuotationId.
    /// Requires quotation.LineItems already loaded (FindQuotationAsync/GetByIdAsync/the
    /// updated SearchAsync all do this).</summary>
    public (decimal InvoicedAmount, decimal RemainingAmount) ComputeBillingProgress(Quotation quotation)
    {
        var invoicedAmount = quotation.LineItems.Sum(li => _invoiceService.GetAmountBilledAgainstQuotationLine(quotation.JobId, li.Id));

        var quotationSubtotal = quotation.LineItems.Sum(li => li.Quantity * li.UnitPrice);
        var quotationTax = quotationSubtotal * quotation.TaxRatePercent / 100m;
        var quotationTotal = quotationSubtotal + quotationTax;

        return (invoicedAmount, quotationTotal - invoicedAmount);
    }

    /// <summary>Per-line counterpart to ComputeBillingProgress - how much of THIS specific
    /// quotation line has been invoiced so far, and how much remains. Delegates the actual
    /// sum to InvoiceService.GetAmountBilledAgainstQuotationLine, the single source of
    /// truth also used by the over-billing block on invoice save.</summary>
    public (decimal InvoicedAmount, decimal RemainingAmount) ComputeLineProgress(Guid jobId, Guid quotationLineId, decimal lineAmount)
    {
        var invoiced = _invoiceService.GetAmountBilledAgainstQuotationLine(jobId, quotationLineId);
        return (invoiced, lineAmount - invoiced);
    }

    private async Task<string> NextNumberAsync(Guid workspaceId, string prefix)
    {
        var count = await _context.Quotations.IgnoreQueryFilters().CountAsync(q => q.Job.WorkspaceId == workspaceId);
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

    /// <summary>Every line carrying a MilestoneId is grouped by milestone and checked
    /// against MilestoneService.EnsureWithinFeeCeilingAsync - the fee ceiling shared with
    /// whatever's already committed via direct-invoice lines for the same milestone.
    /// excludingQuotationId lets an update exclude this quotation's own current lines from
    /// the sum before re-adding its (possibly changed) new amount.</summary>
    private async Task ValidateLineItemsAsync(List<LineItemDto> items, Guid jobId, Guid? excludingQuotationId)
    {
        if (items.Count == 0)
            throw new ValidationException("At least one line item is required.");
        if (items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0))
            throw new ValidationException("Line item quantity must be positive and unit price cannot be negative.");

        var milestoneGroups = items.Where(i => i.MilestoneId.HasValue).GroupBy(i => i.MilestoneId!.Value);
        foreach (var group in milestoneGroups)
        {
            var amount = group.Sum(i => i.Quantity * i.UnitPrice);
            await _milestoneService.EnsureWithinFeeCeilingAsync(jobId, group.Key, amount, excludingQuotationId: excludingQuotationId);
        }
    }

    /// <summary>Rejects an update that would remove a quotation line, or shrink one below
    /// its already-invoiced amount, while any invoice is still actively billed against it.
    /// Must run before the update-in-place loop mutates anything.</summary>
    private void EnsureLineEditsPreserveInvoicedAmounts(Quotation quotation, List<LineItemDto> requestItems)
    {
        var incomingById = requestItems.Where(i => i.Id.HasValue).ToDictionary(i => i.Id!.Value);
        foreach (var current in quotation.LineItems)
        {
            var invoiced = _invoiceService.GetAmountBilledAgainstQuotationLine(quotation.JobId, current.Id);
            if (invoiced <= 0)
                continue;
            if (!incomingById.TryGetValue(current.Id, out var stillPresent))
                throw new ValidationException($"Cannot remove quotation line '{current.Description}' - {invoiced} is already invoiced against it.");
            var newAmount = stillPresent.Quantity * stillPresent.UnitPrice;
            if (newAmount < invoiced)
                throw new ValidationException($"Cannot reduce quotation line '{current.Description}' below its invoiced amount of {invoiced}.");
        }
    }

    private static void ValidateTaxRate(decimal taxRatePercent)
    {
        if (taxRatePercent < 0)
            throw new ValidationException("Tax rate cannot be negative.");
    }

    private static List<QuotationLineItem> ToEntities(List<LineItemDto> items) =>
        items.Select(i => new QuotationLineItem { Id = Guid.NewGuid(), Description = i.Description.Trim(), Quantity = i.Quantity, UnitPrice = i.UnitPrice, MilestoneId = i.MilestoneId }).ToList();

    private async Task<Quotation> FindQuotationAsync(Guid workspaceId, Guid quotationId)
    {
        return await _context.Quotations.Include(q => q.LineItems).Include(q => q.Client)
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.Job.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Quotation not found");
    }
}
