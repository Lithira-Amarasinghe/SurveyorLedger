using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Milestone;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IMilestoneService
{
    Task<List<Milestone>> GetMilestonesAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
    Task<Milestone> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId);
    Task<Milestone> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, MilestoneRequest request);
    Task<Milestone> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, MilestoneRequest request);
    Task<Milestone> UpdateStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, string status);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId);
    Task<List<Milestone>> ReorderAsync(Guid workspaceId, Guid callerUserId, Guid jobId, List<Guid> orderedMilestoneIds);
    Task<List<MilestonePaymentRequirement>> GetPaymentRequirementsAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId);
    Task<List<MilestonePaymentRequirement>> SetPaymentRequirementsAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, List<(string TargetStatus, string RequiredState)> rules);
    Task<MilestonePaymentStatus> GetPaymentStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId);
    Task<decimal> GetCommittedAmountAsync(Guid jobId, Guid milestoneId, Guid? excludingQuotationId = null, Guid? excludingInvoiceId = null);
    Task EnsureWithinFeeCeilingAsync(Guid jobId, Guid milestoneId, decimal additionalAmount, Guid? excludingQuotationId = null, Guid? excludingInvoiceId = null);
}

public record LinkedInvoiceSummary(Guid InvoiceId, string Number, string Status);

public record LinkedQuotationSummary(Guid QuotationId, string Number, string Status);

/// <summary>Breaks CommittedAmount into a bar-friendly progression: QuotedAmount is fee
/// promised on an active quotation line but not yet invoiced, InvoicedAmount is fee that has
/// reached an invoice (direct or via a quotation line), PaidAmount is the share of that
/// invoiced amount actually collected (each invoice's AmountPaid split pro-rata across its
/// lines by amount, since payments are recorded against the invoice total, not per line).
/// QuotedAmount + InvoicedAmount == CommittedAmount always.</summary>
public record MilestonePaymentStatus(decimal? Amount, decimal CommittedAmount, decimal QuotedAmount, decimal InvoicedAmount, decimal PaidAmount, decimal? RemainingAmount, List<LinkedInvoiceSummary> LinkedInvoices, List<LinkedQuotationSummary> LinkedQuotations, string? NextGate);

/// <summary>
/// Milestones are a job sub-resource: every action reuses JobService's job.view /
/// job.edit Casbin permissions and the same job-assignment scoping rule (unless the
/// caller holds job.view_all, they must hold a job-scoped UserAccess row for this
/// specific job). This is intentionally duplicated from JobService rather than
/// extracted to a shared base - see the design spec's reasoning: only two call sites
/// exist, and a shared abstraction for two users isn't justified yet.
/// </summary>
public class MilestoneService : IMilestoneService
{
    private static readonly HashSet<string> ValidStatuses = new() { "Pending", "InProgress", "Completed" };
    private static readonly HashSet<string> ValidPaymentStates = new() { "Invoiced", "PartiallyPaid", "FullyPaid" };

    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly ILogger<MilestoneService> _logger;

    public MilestoneService(ApplicationDbContext context, IScopedAccessService access, ILogger<MilestoneService> logger)
    {
        _context = context;
        _access = access;
        _logger = logger;
    }

    public async Task<List<Milestone>> GetMilestonesAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

        return await _context.Milestones
            .Where(m => m.JobId == jobId)
            .OrderBy(m => m.SortOrder)
            .ToListAsync();
    }

    public async Task<Milestone> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");
        return await FindMilestoneAsync(jobId, milestoneId);
    }

    public async Task<Milestone> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, MilestoneRequest request)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var nextSortOrder = await _context.Milestones
            .Where(m => m.JobId == jobId)
            .Select(m => (int?)m.SortOrder)
            .MaxAsync() ?? -1;

        var callerPersonId = await _access.ResolvePersonIdAsync(callerUserId);

        var milestone = new Milestone
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            Title = request.Title.Trim(),
            Description = request.Description,
            DueDate = request.DueDate,
            Amount = request.Amount,
            Status = "Pending",
            SortOrder = nextSortOrder + 1,
            CreatedBy = callerPersonId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Milestones.AddAsync(milestone);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Milestone {MilestoneId} created for job {JobId} by {UserId}", milestone.Id, jobId, callerUserId);
        return milestone;
    }

    public async Task<Milestone> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, MilestoneRequest request)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var milestone = await FindMilestoneAsync(jobId, milestoneId);
        milestone.Title = request.Title.Trim();
        milestone.Description = request.Description;
        milestone.DueDate = request.DueDate;
        milestone.Amount = request.Amount;
        milestone.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return milestone;
    }

    public async Task<Milestone> UpdateStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, string status)
    {
        if (!ValidStatuses.Contains(status))
            throw new ValidationException($"Status must be one of: {string.Join(", ", ValidStatuses)}.");

        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var milestone = await FindMilestoneAsync(jobId, milestoneId);

        await _context.Entry(milestone).Collection(m => m.PaymentRequirements).LoadAsync();
        var applicableRules = milestone.PaymentRequirements.Where(r => r.TargetStatus == status).ToList();
        if (applicableRules.Count > 0)
        {
            var linkedInvoices = await FindLinkedInvoicesAsync(milestoneId);
            var committedAmount = await GetCommittedAmountAsync(jobId, milestoneId);
            var unmet = applicableRules.FirstOrDefault(r => !IsRequirementSatisfied(r.RequiredState, milestone, linkedInvoices, committedAmount));
            if (unmet != null)
                throw new ValidationException($"Requires the linked invoice(s) to be {DescribeState(unmet.RequiredState)} before it can be marked {status}.");
        }

        milestone.Status = status;
        milestone.UpdatedAt = DateTime.UtcNow;

        if (status == "Completed")
        {
            milestone.CompletedAt = DateTime.UtcNow;
            milestone.CompletedBy = await _access.ResolvePersonIdAsync(callerUserId);
        }
        else
        {
            // Reopening a milestone clears stale completion metadata rather than
            // leaving a CompletedAt/CompletedBy that no longer matches its status.
            milestone.CompletedAt = null;
            milestone.CompletedBy = null;
        }

        await _context.SaveChangesAsync();
        return milestone;
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var milestone = await FindMilestoneAsync(jobId, milestoneId);
        milestone.IsActive = false;
        milestone.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Full-list reorder: caller submits every milestone id in the desired order, and each
    /// gets SortOrder = its index. Requires the submitted set to exactly match the job's
    /// current active milestones - a partial or stale list (e.g. a milestone someone else
    /// just deleted) is rejected rather than silently reordering a subset, which would
    /// leave SortOrder gaps or duplicates.
    /// </summary>
    public async Task<List<Milestone>> ReorderAsync(Guid workspaceId, Guid callerUserId, Guid jobId, List<Guid> orderedMilestoneIds)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var milestones = await _context.Milestones.Where(m => m.JobId == jobId).ToListAsync();
        var byId = milestones.ToDictionary(m => m.Id);

        if (orderedMilestoneIds.Count != milestones.Count || orderedMilestoneIds.Distinct().Count() != orderedMilestoneIds.Count
            || !orderedMilestoneIds.All(byId.ContainsKey))
            throw new ValidationException("The reorder list must contain exactly this job's current milestones, each once.");

        for (var i = 0; i < orderedMilestoneIds.Count; i++)
        {
            var milestone = byId[orderedMilestoneIds[i]];
            milestone.SortOrder = i;
            milestone.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return milestones.OrderBy(m => m.SortOrder).ToList();
    }

    public async Task<List<MilestonePaymentRequirement>> GetPaymentRequirementsAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");
        var milestone = await _context.Milestones.Include(m => m.PaymentRequirements).FirstOrDefaultAsync(m => m.Id == milestoneId && m.JobId == jobId)
            ?? throw new NotFoundException("Milestone not found");
        return milestone.PaymentRequirements;
    }

    public async Task<List<MilestonePaymentRequirement>> SetPaymentRequirementsAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, List<(string TargetStatus, string RequiredState)> rules)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        foreach (var (targetStatus, requiredState) in rules)
        {
            if (!ValidStatuses.Contains(targetStatus))
                throw new ValidationException($"TargetStatus must be one of: {string.Join(", ", ValidStatuses)}.");
            if (!ValidPaymentStates.Contains(requiredState))
                throw new ValidationException($"RequiredState must be one of: {string.Join(", ", ValidPaymentStates)}.");
        }

        var milestone = await _context.Milestones.Include(m => m.PaymentRequirements).FirstOrDefaultAsync(m => m.Id == milestoneId && m.JobId == jobId)
            ?? throw new NotFoundException("Milestone not found");

        // Wholesale replace - same delete-all/insert-all pattern InvoiceService uses for
        // LineItems/Installments, appropriate here for the same reason: a small owned list
        // with no external references to preserve.
        foreach (var old in milestone.PaymentRequirements.ToList())
            _context.Remove(old);
        milestone.PaymentRequirements.Clear();
        foreach (var (targetStatus, requiredState) in rules)
        {
            var rule = new MilestonePaymentRequirement { Id = Guid.NewGuid(), TargetStatus = targetStatus, RequiredState = requiredState };
            milestone.PaymentRequirements.Add(rule);
            _context.Entry(rule).State = EntityState.Added;
        }
        milestone.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return milestone.PaymentRequirements;
    }

    public async Task<MilestonePaymentStatus> GetPaymentStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");
        var milestone = await FindMilestoneAsync(jobId, milestoneId);

        var linkedInvoices = await FindLinkedInvoicesAsync(milestoneId);
        var linkedQuotations = await FindLinkedQuotationsAsync(milestoneId);
        var committedAmount = await GetCommittedAmountAsync(jobId, milestoneId);
        var nextGate = await ResolveNextGateAsync(milestone, linkedInvoices, committedAmount);

        // Every invoice line tagged with this milestone (direct or copied from a quotation
        // line) is "invoiced"; the rest of committedAmount is quoted-but-not-yet-invoiced.
        var invoicedAmount = linkedInvoices
            .SelectMany(i => i.LineItems)
            .Where(li => li.MilestoneId == milestoneId)
            .Sum(li => li.Quantity * li.UnitPrice);
        var quotedAmount = committedAmount - invoicedAmount;

        // Payments land against an invoice's total, not a line, so a milestone's paid share
        // of each invoice is its milestone-tagged lines' proportion of that invoice's total.
        // Total/AmountPaid aren't stored columns (InvoiceService.ComputeInvoiceTotals owns that
        // math) - injecting IInvoiceService here would be circular since it depends on
        // IMilestoneService, so the same subtotal/tax/discount/payments formula is inlined.
        var paidAmount = 0m;
        foreach (var invoice in linkedInvoices)
        {
            var invoiceSubtotal = invoice.LineItems.Sum(li => li.Quantity * li.UnitPrice);
            var invoiceTotal = invoiceSubtotal - invoice.DiscountAmount + invoiceSubtotal * invoice.TaxRatePercent / 100m;
            if (invoiceTotal <= 0) continue;
            var invoiceAmountPaid = invoice.Payments.Sum(p => p.Amount);
            var milestoneShare = invoice.LineItems.Where(li => li.MilestoneId == milestoneId).Sum(li => li.Quantity * li.UnitPrice);
            paidAmount += invoiceAmountPaid * (milestoneShare / invoiceTotal);
        }

        return new MilestonePaymentStatus(
            milestone.Amount,
            committedAmount,
            quotedAmount,
            invoicedAmount,
            paidAmount,
            milestone.Amount.HasValue ? milestone.Amount.Value - committedAmount : null,
            linkedInvoices.Select(i => new LinkedInvoiceSummary(i.Id, i.Number, i.Status)).ToList(),
            linkedQuotations.Select(q => new LinkedQuotationSummary(q.Id, q.Number, q.Status)).ToList(),
            nextGate);
    }

    /// <summary>Sums everything currently committed against a milestone's fee: every
    /// quotation line tagged with this MilestoneId on a non-Rejected/Expired active
    /// quotation (Draft/Sent/Accepted all count - two drafts can each partially quote the
    /// milestone as long as their sum stays under the fee), plus every direct-invoice line
    /// tagged with this MilestoneId whose QuotationLineId is null (a line billed through a
    /// quotation is already counted via that quotation line - counting the resulting
    /// invoice line too would double-charge the ceiling). excludingQuotationId/
    /// excludingInvoiceId let a document being saved exclude its own not-yet-persisted
    /// lines from the sum.</summary>
    public async Task<decimal> GetCommittedAmountAsync(Guid jobId, Guid milestoneId, Guid? excludingQuotationId = null, Guid? excludingInvoiceId = null)
    {
        var quotationCommitted = await _context.Quotations
            .Where(q => q.IsActive && q.JobId == jobId && q.Status != "Rejected" && q.Status != "Expired")
            .Where(q => excludingQuotationId == null || q.Id != excludingQuotationId)
            .SelectMany(q => q.LineItems)
            .Where(li => li.MilestoneId == milestoneId)
            .SumAsync(li => (decimal?)(li.Quantity * li.UnitPrice)) ?? 0m;

        var invoiceCommitted = await _context.Invoices
            .Where(i => i.IsActive && i.JobId == jobId)
            .Where(i => excludingInvoiceId == null || i.Id != excludingInvoiceId)
            .SelectMany(i => i.LineItems)
            .Where(li => li.MilestoneId == milestoneId && li.QuotationLineId == null)
            .SumAsync(li => (decimal?)(li.Quantity * li.UnitPrice)) ?? 0m;

        return quotationCommitted + invoiceCommitted;
    }

    /// <summary>Also validates that milestoneId resolves to an active milestone on this
    /// same job - the old exclusivity checks in InvoiceService/QuotationService never
    /// verified this, so it's added here as the single place both now route through.
    /// No-ops if the milestone has no fee (Amount == null) - "milestone can exist without
    /// a fee" per the design spec.</summary>
    public async Task EnsureWithinFeeCeilingAsync(Guid jobId, Guid milestoneId, decimal additionalAmount, Guid? excludingQuotationId = null, Guid? excludingInvoiceId = null)
    {
        var milestone = await _context.Milestones.FirstOrDefaultAsync(m => m.Id == milestoneId && m.IsActive);
        if (milestone == null || milestone.JobId != jobId)
            throw new ValidationException("MilestoneId must reference an active milestone on this same job.");
        if (milestone.Amount == null)
            return;

        var committed = await GetCommittedAmountAsync(jobId, milestoneId, excludingQuotationId, excludingInvoiceId);
        var total = committed + additionalAmount;
        if (total > milestone.Amount)
            throw new ValidationException($"Billing {total} against milestone '{milestone.Title}' would exceed its fee of {milestone.Amount}.");
    }

    /// <summary>Every active invoice carrying a line item tagged with this milestone -
    /// partial billing means this is no longer at most one, unlike before the fee-ceiling
    /// feature. Payments included since gate satisfaction needs each invoice's AmountPaid.</summary>
    private async Task<List<Invoice>> FindLinkedInvoicesAsync(Guid milestoneId) =>
        await _context.Invoices.Include(i => i.LineItems).Include(i => i.Payments)
            .Where(i => i.IsActive && i.LineItems.Any(li => li.MilestoneId == milestoneId))
            .ToListAsync();

    /// <summary>Every active quotation carrying a line item tagged with this milestone -
    /// mirrors FindLinkedInvoicesAsync, surfaced in MilestonePaymentStatus so the milestone
    /// panel can show quotation-side linkage too, not just invoices.</summary>
    private async Task<List<Quotation>> FindLinkedQuotationsAsync(Guid milestoneId) =>
        await _context.Quotations.Include(q => q.LineItems)
            .Where(q => q.IsActive && q.LineItems.Any(li => li.MilestoneId == milestoneId))
            .ToListAsync();

    /// <summary>Human-readable description of the nearest unmet requirement for this
    /// milestone's *current* status - i.e. what would block its next transition attempt via
    /// UpdateStatusAsync, without knowing in advance which status the caller will try next.</summary>
    private async Task<string?> ResolveNextGateAsync(Milestone milestone, List<Invoice> linkedInvoices, decimal committedAmount)
    {
        await _context.Entry(milestone).Collection(m => m.PaymentRequirements).LoadAsync();
        foreach (var rule in milestone.PaymentRequirements)
        {
            if (!IsRequirementSatisfied(rule.RequiredState, milestone, linkedInvoices, committedAmount))
                return $"Requires the linked invoice(s) to be {DescribeState(rule.RequiredState)} before it can be marked {rule.TargetStatus}.";
        }
        return null;
    }

    private static string DescribeState(string state) => state switch
    {
        "Invoiced" => "invoiced",
        "PartiallyPaid" => "at least partially paid",
        "FullyPaid" => "fully paid",
        _ => state
    };

    /// <summary>"FullyPaid" now requires both that the milestone is fully committed (its
    /// fee ceiling is exactly met - partial billing means "some invoice is Paid" is no
    /// longer sufficient on its own) and that every linked invoice has actually been paid
    /// off.</summary>
    private static bool IsRequirementSatisfied(string requiredState, Milestone milestone, List<Invoice> linkedInvoices, decimal committedAmount)
    {
        if (linkedInvoices.Count == 0)
            return false;
        return requiredState switch
        {
            "Invoiced" => linkedInvoices.Any(i => i.Status is "Sent" or "PartiallyPaid" or "Paid"),
            "PartiallyPaid" => linkedInvoices.Sum(i => i.Payments.Sum(p => p.Amount)) > 0,
            "FullyPaid" => milestone.Amount.HasValue && committedAmount >= milestone.Amount.Value && linkedInvoices.All(i => i.Status == "Paid"),
            _ => false
        };
    }

    private async Task<Job> FindJobAsync(Guid workspaceId, Guid jobId)
    {
        return await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Job not found");
    }

    private async Task<Milestone> FindMilestoneAsync(Guid jobId, Guid milestoneId)
    {
        return await _context.Milestones.FirstOrDefaultAsync(m => m.Id == milestoneId && m.JobId == jobId)
            ?? throw new NotFoundException("Milestone not found");
    }

}
