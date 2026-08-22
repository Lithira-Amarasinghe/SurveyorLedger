using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IInvoiceService
{
    Task<Invoice> CreateAsync(Guid workspaceId, Guid callerUserId, InvoiceRequest request);
    Task<List<Invoice>> SearchAsync(Guid workspaceId, Guid callerUserId, Guid? jobId = null);
    Task<Invoice> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId);
    Task<Invoice> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, InvoiceRequest request);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId);
    Task<Payment> RecordPaymentAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, PaymentRequest request, IFormFile? proofFile);
    Task<List<Payment>> GetPaymentsAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId);
    Task<Payment> VoidPaymentAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, Guid paymentId, string? reason);
    Task<Payment> RecordRefundAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, PaymentRequest request, IFormFile? proofFile);
    Task<(Stream Content, string Path)> GetPaymentProofFileAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, Guid paymentId);
    Task SendAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, List<Guid> recipientPersonIds, string appBaseUrl);
    (decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue, int DaysOverdue) ComputeInvoiceTotals(Invoice invoice);
    List<(InvoiceInstallment Installment, string Status)> ComputeInstallmentStatuses(Invoice invoice);
    decimal GetAmountBilledAgainstQuotationLine(Guid jobId, Guid quotationLineId, Guid? excludingInvoiceId = null);
}

/// <summary>
/// No ClientId - access is governed by job-scoped or workspace-scoped permissions, not a
/// stored client reference. See EnsureAccessAsync.
/// </summary>
public class InvoiceService : IInvoiceService
{
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase) { "Cash", "BankTransfer", "Cheque" };
    private static readonly HashSet<string> AllowedProofExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png" };

    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly IFileStorageService _fileStorage;
    private readonly IPdfService _pdfService;
    private readonly IEmailService _emailService;
    private readonly IMilestoneService _milestoneService;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(ApplicationDbContext context, IScopedAccessService access, IFileStorageService fileStorage, IPdfService pdfService, IEmailService emailService, IMilestoneService milestoneService, ILogger<InvoiceService> logger)
    {
        _context = context;
        _access = access;
        _fileStorage = fileStorage;
        _pdfService = pdfService;
        _emailService = emailService;
        _milestoneService = milestoneService;
        _logger = logger;
    }

    public async Task<Invoice> CreateAsync(Guid workspaceId, Guid callerUserId, InvoiceRequest request)
    {
        if (request.JobId.HasValue)
            await _access.EnsureJobAccessAsync(callerUserId, workspaceId, request.JobId.Value, "create");
        else
            await _access.EnsureAllowedAsync(callerUserId, "invoice", "create", workspaceId);
        await ValidateLineItemsAsync(request.LineItems, request.JobId, null);
        ValidateFinancials(request.TaxRatePercent, request.DiscountAmount, request.LineItems);
        ValidateInstallments(request.Installments, request.LineItems, request.TaxRatePercent, request.DiscountAmount);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            JobId = request.JobId,
            LineItems = request.LineItems.Select(i => new InvoiceLineItem { Id = Guid.NewGuid(), Description = i.Description.Trim(), Quantity = i.Quantity, UnitPrice = i.UnitPrice, MilestoneId = i.MilestoneId, QuotationLineId = i.QuotationLineId }).ToList(),
            Installments = request.Installments.Select(i => new InvoiceInstallment { Id = Guid.NewGuid(), Amount = i.Amount, DueDate = i.DueDate }).ToList(),
            TaxRatePercent = request.TaxRatePercent,
            DiscountAmount = request.DiscountAmount,
            Status = request.Status ?? "Draft",
            DueDate = request.DueDate,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            invoice.Number = await NextInvoiceNumberAsync(workspaceId);
            await _context.Invoices.AddAsync(invoice);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        _logger.LogInformation("Invoice {InvoiceId} ({Number}) created in workspace {WorkspaceId} (job {JobId}) by {UserId}", invoice.Id, invoice.Number, workspaceId, invoice.JobId, callerUserId);
        return invoice;
    }

    public async Task<List<Invoice>> SearchAsync(Guid workspaceId, Guid callerUserId, Guid? jobId = null)
    {
        await _access.EnsureListAllowedAsync(callerUserId, workspaceId);

        var invoices = _context.Invoices.Include(i => i.Payments).Where(i => i.WorkspaceId == workspaceId);
        if (!await _access.HasViewAllAsync(callerUserId, "job", workspaceId))
        {
            var accessibleJobIds = (await _access.GetAccessibleJobsAsync(callerUserId))
                .Where(j => j.WorkspaceId == workspaceId).Select(j => j.JobId).ToHashSet();
            var canViewWorkspaceLevel = await _access.CanAsync(callerUserId, "invoice", "view", workspaceId);
            invoices = invoices.Where(i =>
                (i.JobId.HasValue && accessibleJobIds.Contains(i.JobId.Value)) ||
                (!i.JobId.HasValue && canViewWorkspaceLevel));
        }
        if (jobId.HasValue)
            invoices = invoices.Where(i => i.JobId == jobId.Value);

        return await invoices.OrderByDescending(i => i.CreatedAt).ToListAsync();
    }

    public async Task<Invoice> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId)
    {
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await EnsureAccessAsync(callerUserId, workspaceId, invoice, "view");
        return invoice;
    }

    public async Task<Invoice> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, InvoiceRequest request)
    {
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await EnsureAccessAsync(callerUserId, workspaceId, invoice, "edit");

        // Once money has moved, the figures behind that payment are locked - only the
        // due date stays editable. Prevents e.g. shrinking the total below AmountPaid
        // (negative balance) or silently reverting a PartiallyPaid/Paid invoice back to
        // Draft. Same "no touching it once paid" principle DeleteAsync already enforces
        // for the delete path.
        // A payment that's since been voided no longer counts - voiding is exactly the
        // "undo this" path, so a fully-voided invoice must go back to being editable.
        if (invoice.Payments.Any(p => !p.IsVoided))
            EnsureOnlyDueDateChanged(invoice, request);

        await ValidateLineItemsAsync(request.LineItems, request.JobId, invoiceId);
        ValidateFinancials(request.TaxRatePercent, request.DiscountAmount, request.LineItems);
        ValidateInstallments(request.Installments, request.LineItems, request.TaxRatePercent, request.DiscountAmount);

        invoice.JobId = request.JobId;
        // Explicitly remove old rows and mark new ones Added - see
        // QuotationService.UpdateAsync for why a Clear()/reassign isn't enough for an
        // OwnsMany collection.
        foreach (var old in invoice.LineItems.ToList())
            _context.Remove(old);
        invoice.LineItems.Clear();
        foreach (var item in request.LineItems.Select(i => new InvoiceLineItem { Id = Guid.NewGuid(), Description = i.Description.Trim(), Quantity = i.Quantity, UnitPrice = i.UnitPrice, MilestoneId = i.MilestoneId, QuotationLineId = i.QuotationLineId }))
        {
            invoice.LineItems.Add(item);
            _context.Entry(item).State = EntityState.Added;
        }
        foreach (var old in invoice.Installments.ToList())
            _context.Remove(old);
        invoice.Installments.Clear();
        foreach (var installment in request.Installments.Select(i => new InvoiceInstallment { Id = Guid.NewGuid(), Amount = i.Amount, DueDate = i.DueDate }))
        {
            invoice.Installments.Add(installment);
            _context.Entry(installment).State = EntityState.Added;
        }
        invoice.TaxRatePercent = request.TaxRatePercent;
        invoice.DiscountAmount = request.DiscountAmount;
        invoice.DueDate = request.DueDate;
        // Server-derived statuses (PartiallyPaid/Paid) are not client-settable here -
        // only Draft/Sent/Cancelled pass through, matching the spec's status rules.
        if (request.Status is "Draft" or "Sent" or "Cancelled")
            invoice.Status = request.Status;
        invoice.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return invoice;
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId)
    {
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await EnsureAccessAsync(callerUserId, workspaceId, invoice, "delete");

        if (invoice.Payments.Any(p => !p.IsVoided))
            throw new ConflictException("Cannot delete an invoice with recorded payments. Cancel it instead.");

        invoice.IsActive = false;
        invoice.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<Payment> RecordPaymentAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, PaymentRequest request, IFormFile? proofFile)
    {
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await EnsureAccessAsync(callerUserId, workspaceId, invoice, "create");
        if (!AllowedMethods.Contains(request.Method))
            throw new ValidationException($"Method must be one of: {string.Join(", ", AllowedMethods)}.");
        if (request.Amount <= 0)
            throw new ValidationException("Amount must be positive.");
        if (request.ReceivedAt.Date > DateTime.UtcNow.Date)
            throw new ValidationException("Received date cannot be in the future.");

        var (total, amountPaid, balance, _, _) = ComputeInvoiceTotals(invoice);
        if (request.Amount > balance)
            throw new ValidationException($"Amount {request.Amount:0.00} exceeds the outstanding balance of {balance:0.00}.");

        var proofPath = await SaveProofFileAsync(workspaceId, invoiceId, proofFile);

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            InvoiceId = invoiceId,
            Amount = request.Amount,
            Method = request.Method,
            ReceivedAt = request.ReceivedAt,
            ReferenceNumber = request.ReferenceNumber?.Trim(),
            ProofFilePath = proofPath,
            RecordedBy = await _access.ResolvePersonIdAsync(callerUserId),
            CreatedAt = DateTime.UtcNow
        };

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            payment.ReceiptNumber = await NextReceiptNumberAsync(workspaceId, "RCP");
            await _context.Payments.AddAsync(payment);

            var newAmountPaid = amountPaid + request.Amount;
            invoice.Status = newAmountPaid >= total ? "Paid" : "PartiallyPaid";
            invoice.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        _logger.LogInformation("Payment {PaymentId} ({ReceiptNumber}) of {Amount} recorded for Invoice {InvoiceId}", payment.Id, payment.ReceiptNumber, payment.Amount, invoiceId);
        return payment;
    }

    public async Task<Payment> RecordRefundAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, PaymentRequest request, IFormFile? proofFile)
    {
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await EnsureAccessAsync(callerUserId, workspaceId, invoice, "create");
        if (!AllowedMethods.Contains(request.Method))
            throw new ValidationException($"Method must be one of: {string.Join(", ", AllowedMethods)}.");
        if (request.Amount <= 0)
            throw new ValidationException("Amount must be positive.");
        if (request.ReceivedAt.Date > DateTime.UtcNow.Date)
            throw new ValidationException("Refund date cannot be in the future.");

        var (total, amountPaid, _, _, _) = ComputeInvoiceTotals(invoice);
        if (request.Amount > amountPaid)
            throw new ValidationException($"Refund amount {request.Amount:0.00} exceeds the {amountPaid:0.00} actually paid on this invoice.");

        var proofPath = await SaveProofFileAsync(workspaceId, invoiceId, proofFile);

        var refund = new Payment
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            InvoiceId = invoiceId,
            Amount = request.Amount,
            IsRefund = true,
            Method = request.Method,
            ReceivedAt = request.ReceivedAt,
            ReferenceNumber = request.ReferenceNumber?.Trim(),
            ProofFilePath = proofPath,
            RecordedBy = await _access.ResolvePersonIdAsync(callerUserId),
            CreatedAt = DateTime.UtcNow
        };

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            refund.ReceiptNumber = await NextReceiptNumberAsync(workspaceId, "RFD");
            await _context.Payments.AddAsync(refund);

            var newAmountPaid = amountPaid - request.Amount;
            invoice.Status = newAmountPaid >= total ? "Paid" : newAmountPaid > 0 ? "PartiallyPaid" : "Sent";
            invoice.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        _logger.LogInformation("Refund {PaymentId} ({ReceiptNumber}) of {Amount} recorded for Invoice {InvoiceId}", refund.Id, refund.ReceiptNumber, refund.Amount, invoiceId);
        return refund;
    }

    public async Task<Payment> VoidPaymentAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, Guid paymentId, string? reason)
    {
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await EnsureAccessAsync(callerUserId, workspaceId, invoice, "create");

        var payment = invoice.Payments.FirstOrDefault(p => p.Id == paymentId)
            ?? throw new NotFoundException("Payment not found.");
        if (payment.IsVoided)
            throw new NotFoundException("Payment not found.");

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            payment.IsVoided = true;
            payment.VoidedAt = DateTime.UtcNow;
            payment.VoidedBy = await _access.ResolvePersonIdAsync(callerUserId);
            payment.VoidReason = reason?.Trim();

            // Recompute from the in-memory collection - the entity is already marked voided
            // here but the DB-level query filter only excludes it on a *fresh* query, so the
            // exclusion in ComputeInvoiceTotals's Sum is what makes this correct right now.
            var (total, amountPaid, _, _, _) = ComputeInvoiceTotals(invoice);
            if (invoice.Status is "Paid" or "PartiallyPaid")
                invoice.Status = amountPaid >= total ? "Paid" : amountPaid > 0 ? "PartiallyPaid" : "Sent";
            invoice.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        _logger.LogInformation("Payment {PaymentId} voided on Invoice {InvoiceId}", payment.Id, invoiceId);
        return payment;
    }

    public async Task<List<Payment>> GetPaymentsAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId)
    {
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await EnsureAccessAsync(callerUserId, workspaceId, invoice, "view");

        // IgnoreQueryFilters: this is the ledger view, where a voided payment stays visible
        // (marked voided) rather than disappearing - the query filter only protects totals
        // math elsewhere from silently counting one.
        return await _context.Payments.IgnoreQueryFilters()
            .Include(p => p.RecordedByUser)
            .Where(p => p.InvoiceId == invoiceId && p.WorkspaceId == workspaceId)
            .OrderByDescending(p => p.ReceivedAt)
            .ToListAsync();
    }

    public async Task<(Stream Content, string Path)> GetPaymentProofFileAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, Guid paymentId)
    {
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await EnsureAccessAsync(callerUserId, workspaceId, invoice, "view");

        var payment = await _context.Payments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == paymentId && p.InvoiceId == invoiceId && p.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Payment not found.");
        if (payment.ProofFilePath == null)
            throw new NotFoundException("No proof file attached to this payment.");

        var content = await _fileStorage.OpenAsync(payment.ProofFilePath, CancellationToken.None);
        return (content, payment.ProofFilePath);
    }

    private async Task<string?> SaveProofFileAsync(Guid workspaceId, Guid invoiceId, IFormFile? proofFile)
    {
        if (proofFile == null) return null;

        var extension = Path.GetExtension(proofFile.FileName);
        if (!AllowedProofExtensions.Contains(extension))
            throw new ValidationException($"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", AllowedProofExtensions)}.");
        if (proofFile.Length > DocumentService.MaxFileSizeBytes)
            throw new ValidationException($"File exceeds the {DocumentService.MaxFileSizeBytes / (1024 * 1024)}MB size limit.");

        var storedFileName = $"{Guid.NewGuid():N}_{proofFile.FileName}";
        var proofPath = $"{workspaceId}/invoices/{invoiceId}/payments/{storedFileName}";
        await using var stream = proofFile.OpenReadStream();
        await _fileStorage.SaveAsync(stream, proofPath, CancellationToken.None);
        return proofPath;
    }

    public async Task SendAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, List<Guid> recipientPersonIds, string appBaseUrl)
    {
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await EnsureAccessAsync(callerUserId, workspaceId, invoice, "edit");

        if (recipientPersonIds.Count == 0)
            throw new ValidationException("At least one recipient is required.");

        var recipients = await _context.People
            .Where(p => recipientPersonIds.Contains(p.Id) && p.IsActive)
            .ToListAsync();
        if (recipients.Count != recipientPersonIds.Count)
            throw new NotFoundException("One or more recipients not found.");

        var eligiblePersonIds = await ResolveEligibleRecipientPersonIdsAsync(workspaceId, invoice.JobId);
        var ineligible = recipientPersonIds.Except(eligiblePersonIds).ToList();
        if (ineligible.Count > 0)
            throw new ValidationException(invoice.JobId.HasValue
                ? "Every recipient must hold Client or Finance access on this invoice's job."
                : "Every recipient must be able to view invoices in this workspace.");

        var totals = ComputeInvoiceTotals(invoice);
        var letterhead = await LoadLetterheadAsync(workspaceId);
        var pdfBytes = _pdfService.GenerateInvoicePdf(invoice, totals, letterhead);
        var linkUrl = invoice.JobId.HasValue
            ? $"{appBaseUrl.TrimEnd('/')}/app/jobs/{invoice.JobId}"
            : $"{appBaseUrl.TrimEnd('/')}/app/workspace/{workspaceId}/billing/invoices";

        foreach (var recipient in recipients)
        {
            if (string.IsNullOrWhiteSpace(recipient.Email))
                continue;
            await _emailService.SendBillingDocumentAsync(recipient.Email, "Invoice", invoice.Number, linkUrl, pdfBytes, $"{invoice.Number}.pdf");
        }

        if (invoice.Status == "Draft")
        {
            invoice.Status = "Sent";
            invoice.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Invoice {InvoiceId} sent to {Count} recipient(s)", invoiceId, recipients.Count);
    }

    public (decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue, int DaysOverdue) ComputeInvoiceTotals(Invoice invoice)
    {
        var subtotal = invoice.LineItems.Sum(li => li.Quantity * li.UnitPrice);
        var tax = subtotal * invoice.TaxRatePercent / 100m;
        var total = subtotal - invoice.DiscountAmount + tax;
        var amountPaid = invoice.Payments.Where(p => !p.IsVoided).Sum(p => p.IsRefund ? -p.Amount : p.Amount);
        var balance = total - amountPaid;

        var isOverdue = invoice.Status is "Sent" or "PartiallyPaid" && invoice.DueDate.HasValue && invoice.DueDate.Value.Date < DateTime.UtcNow.Date;
        var daysOverdue = isOverdue ? (DateTime.UtcNow.Date - invoice.DueDate!.Value.Date).Days : 0;

        return (total, amountPaid, balance, isOverdue, daysOverdue);
    }

    private async Task<string> NextInvoiceNumberAsync(Guid workspaceId)
    {
        var count = await _context.Invoices.IgnoreQueryFilters().CountAsync(i => i.WorkspaceId == workspaceId);
        return $"INV-{count + 1:D4}";
    }

    private async Task<string> NextReceiptNumberAsync(Guid workspaceId, string prefix)
    {
        var count = await _context.Payments.IgnoreQueryFilters().CountAsync(p => p.WorkspaceId == workspaceId);
        return $"{prefix}-{count + 1:D4}";
    }

    /// <summary>Job-scoped invoices use job-scoped access (covers Client/Finance role
    /// holders on that job, same as before). Workspace-level invoices (JobId null) use the
    /// plain workspace-wide invoice.* permission instead - there's no job to check against.</summary>
    private async Task EnsureAccessAsync(Guid callerUserId, Guid workspaceId, Invoice invoice, string action)
    {
        if (invoice.JobId.HasValue)
            await _access.EnsureJobAccessAsync(callerUserId, workspaceId, invoice.JobId.Value, action);
        else
            await _access.EnsureAllowedAsync(callerUserId, "invoice", action, workspaceId);
    }

    private async Task<List<Guid>> ResolveEligibleRecipientPersonIdsAsync(Guid workspaceId, Guid? jobId)
    {
        if (jobId.HasValue)
        {
            return await _context.UserAccesses
                .Include(ua => ua.User)
                .Where(ua => ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == jobId.Value)
                .Where(ua => ua.Role.Name == Constants.SystemRoles.Client || ua.Role.Name == Constants.SystemRoles.Finance)
                .Select(ua => ua.User.PersonId)
                .ToListAsync();
        }

        var workspaceUsers = await _context.UserAccesses
            .Include(ua => ua.User)
            .Where(ua => ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId)
            .ToListAsync();
        var eligible = new List<Guid>();
        foreach (var ua in workspaceUsers)
        {
            if (await _access.CanAsync(ua.UserId, "invoice", "view", workspaceId))
                eligible.Add(ua.User.PersonId);
        }
        return eligible;
    }

    /// <summary>Any line carrying a QuotationLineId must point at an active quotation line
    /// on this same job; if that quotation line carries a MilestoneId, it's auto-copied
    /// onto the invoice line (an explicit conflicting MilestoneId on such a line is
    /// rejected) so milestone rollups are a single-field query. The total billed against
    /// that quotation line (this invoice's own lines plus every other active invoice's)
    /// must not exceed the quotation line's Quantity * UnitPrice. Separately, every line
    /// carrying a bare MilestoneId (no QuotationLineId - a direct, not quotation-drawn,
    /// charge) is grouped by milestone and checked against
    /// MilestoneService.EnsureWithinFeeCeilingAsync - the milestone's fee ceiling, shared
    /// with whatever's already committed via quotation lines for the same milestone. A
    /// workspace-level invoice (jobId null) can't carry MilestoneId/QuotationLineId at all -
    /// both concepts are job-scoped.</summary>
    private async Task ValidateLineItemsAsync(List<LineItemDto> items, Guid? jobId, Guid? excludingInvoiceId)
    {
        if (items.Count == 0)
            throw new ValidationException("At least one line item is required.");
        if (items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0))
            throw new ValidationException("Line item quantity must be positive and unit price cannot be negative.");

        if (!jobId.HasValue)
        {
            if (items.Any(i => i.MilestoneId.HasValue || i.QuotationLineId.HasValue))
                throw new ValidationException("A workspace-level invoice's lines cannot reference a milestone or quotation line - both are job-scoped.");
            return;
        }

        var quotationLineGroups = items.Where(i => i.QuotationLineId.HasValue).GroupBy(i => i.QuotationLineId!.Value);
        foreach (var group in quotationLineGroups)
        {
            var quotationLine = await FindQuotationLineAsync(group.Key);
            if (quotationLine == null || quotationLine.Value.JobId != jobId)
                throw new ValidationException("QuotationLineId must reference an active quotation line on this same job.");

            foreach (var item in group)
            {
                if (item.MilestoneId.HasValue && item.MilestoneId != quotationLine.Value.MilestoneId)
                    throw new ValidationException("A line's MilestoneId must match its linked quotation line's milestone.");
                item.MilestoneId = quotationLine.Value.MilestoneId;
            }

            var thisInvoiceAmount = group.Sum(i => i.Quantity * i.UnitPrice);
            var otherInvoicesAmount = GetAmountBilledAgainstQuotationLine(jobId.Value, group.Key, excludingInvoiceId);
            var totalBilled = thisInvoiceAmount + otherInvoicesAmount;
            if (totalBilled > quotationLine.Value.Amount)
                throw new ValidationException($"Billing {totalBilled:0.00} against this quotation line would exceed its total of {quotationLine.Value.Amount:0.00}.");
        }

        var directMilestoneGroups = items.Where(i => i.MilestoneId.HasValue && !i.QuotationLineId.HasValue).GroupBy(i => i.MilestoneId!.Value);
        foreach (var group in directMilestoneGroups)
        {
            var amount = group.Sum(i => i.Quantity * i.UnitPrice);
            await _milestoneService.EnsureWithinFeeCeilingAsync(jobId.Value, group.Key, amount, excludingInvoiceId: excludingInvoiceId);
        }
    }

    /// <summary>Resolves a QuotationLineId to its owning quotation's JobId and its
    /// Quantity * UnitPrice amount, or null if no active quotation currently has a line
    /// with that Id. Owned-entity line items have no standalone DbSet, so this goes
    /// through Quotations with LineItems included.</summary>
    private async Task<(Guid? JobId, decimal Amount, Guid? MilestoneId)?> FindQuotationLineAsync(Guid quotationLineId)
    {
        var quotation = await _context.Quotations
            .Include(q => q.LineItems)
            .Where(q => q.IsActive)
            .FirstOrDefaultAsync(q => q.LineItems.Any(li => li.Id == quotationLineId));
        if (quotation == null)
            return null;
        var line = quotation.LineItems.First(li => li.Id == quotationLineId);
        return (quotation.JobId, line.Quantity * line.UnitPrice, line.MilestoneId);
    }

    /// <summary>Sums Quantity * UnitPrice across every active invoice line on this job whose
    /// QuotationLineId matches, optionally excluding one invoice (the one currently being
    /// saved, so it doesn't double-count against itself). Used both for the over-billing
    /// check above and by QuotationService's edit-safety guard - the single source of
    /// truth for "how much has been invoiced against this quotation line so far".</summary>
    public decimal GetAmountBilledAgainstQuotationLine(Guid jobId, Guid quotationLineId, Guid? excludingInvoiceId = null)
    {
        return _context.Invoices
            .Include(i => i.LineItems)
            .Where(i => i.IsActive && i.JobId == jobId && (excludingInvoiceId == null || i.Id != excludingInvoiceId))
            .AsEnumerable()
            .SelectMany(i => i.LineItems)
            .Where(li => li.QuotationLineId == quotationLineId)
            .Sum(li => li.Quantity * li.UnitPrice);
    }

    /// <summary>Guards against a negative total: tax rate must be non-negative, discount
    /// must be non-negative and cannot exceed the subtotal it's discounting.</summary>
    private static void ValidateFinancials(decimal taxRatePercent, decimal discountAmount, List<LineItemDto> lineItems)
    {
        if (taxRatePercent < 0)
            throw new ValidationException("Tax rate cannot be negative.");
        if (discountAmount < 0)
            throw new ValidationException("Discount amount cannot be negative.");

        var subtotal = lineItems.Sum(li => li.Quantity * li.UnitPrice);
        if (discountAmount > subtotal)
            throw new ValidationException($"Discount ({discountAmount:0.00}) cannot exceed the subtotal ({subtotal:0.00}).");
    }

    /// <summary>Once a payment exists, only DueDate may change - everything that affects
    /// the invoice's figures (job, line items, tax, discount, installments, status) is
    /// locked. Compares by value, not reference, since the request always carries the full
    /// current state back (line-item collection is replaced wholesale on every save, not
    /// patched).</summary>
    private static void EnsureOnlyDueDateChanged(Invoice invoice, InvoiceRequest request)
    {
        var lineItemsChanged = invoice.LineItems.Count != request.LineItems.Count
            || invoice.LineItems.OrderBy(li => li.Id).Select(li => (li.Description, li.Quantity, li.UnitPrice, li.MilestoneId, li.QuotationLineId))
                .Except(request.LineItems.Select(li => (li.Description.Trim(), li.Quantity, li.UnitPrice, li.MilestoneId, li.QuotationLineId))).Any();

        if (invoice.JobId != request.JobId
            || invoice.TaxRatePercent != request.TaxRatePercent
            || invoice.DiscountAmount != request.DiscountAmount
            || (request.Status != null && request.Status != invoice.Status)
            || lineItemsChanged)
        {
            throw new ConflictException("This invoice already has recorded payments - only the due date can be changed. Cancel and reissue instead if the amount is wrong.");
        }
    }

    /// <summary>Empty schedule is always valid - installments are optional. When present,
    /// checked only at write time against the total as computed from this same request -
    /// not re-validated if the invoice is edited again without touching the schedule (see
    /// design spec's "accepted limitation" note).</summary>
    private static void ValidateInstallments(List<InstallmentDto> installments, List<LineItemDto> lineItems, decimal taxRatePercent, decimal discountAmount)
    {
        if (installments.Count == 0)
            return;
        if (installments.Any(i => i.Amount <= 0))
            throw new ValidationException("Installment amount must be positive.");

        var subtotal = lineItems.Sum(li => li.Quantity * li.UnitPrice);
        var total = subtotal - discountAmount + subtotal * taxRatePercent / 100m;
        var scheduled = installments.Sum(i => i.Amount);
        if (scheduled != total)
            throw new ValidationException($"Installments must sum to the invoice total ({total:0.00}), got {scheduled:0.00}.");
    }

    /// <summary>Ordered by due date, walks cumulative installment amount against the
    /// invoice's cumulative AmountPaid - Paid once that running total covers the
    /// installment, Overdue if its due date has passed and it isn't yet, else Pending.
    /// Purely a display layer - ComputeInvoiceTotals/Invoice.Status are unaffected.</summary>
    public List<(InvoiceInstallment Installment, string Status)> ComputeInstallmentStatuses(Invoice invoice)
    {
        var (_, amountPaid, _, _, _) = ComputeInvoiceTotals(invoice);
        var ordered = invoice.Installments.OrderBy(i => i.DueDate).ToList();
        var result = new List<(InvoiceInstallment, string)>();
        var cumulative = 0m;

        foreach (var installment in ordered)
        {
            cumulative += installment.Amount;
            string status;
            if (amountPaid >= cumulative)
                status = "Paid";
            else if (installment.DueDate.Date < DateTime.UtcNow.Date)
                status = "Overdue";
            else
                status = "Pending";
            result.Add((installment, status));
        }

        return result;
    }

    private async Task<Invoice> FindInvoiceAsync(Guid workspaceId, Guid invoiceId)
    {
        return await _context.Invoices.Include(i => i.Payments).Include(i => i.LineItems).Include(i => i.Installments).Include(i => i.Job)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Invoice not found");
    }

    /// <summary>Null when the workspace has no letterhead text/logo at all, so PdfService's
    /// no-op check stays a single null check at the call site.</summary>
    private async Task<PdfLetterhead?> LoadLetterheadAsync(Guid workspaceId)
    {
        var workspace = await _context.Workspaces.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workspaceId);
        if (workspace == null) return null;

        var hasText = workspace.LetterheadCompanyName != null || workspace.LetterheadAddress != null
            || workspace.LetterheadPhone != null || workspace.LetterheadEmail != null || workspace.LetterheadRegistrationNumber != null;
        byte[]? logoBytes = null;
        if (workspace.LetterheadLogoPath != null)
        {
            await using var stream = await _fileStorage.OpenAsync(workspace.LetterheadLogoPath, CancellationToken.None);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            logoBytes = ms.ToArray();
        }
        if (!hasText && logoBytes == null) return null;

        return new PdfLetterhead(workspace.LetterheadCompanyName, workspace.LetterheadAddress, workspace.LetterheadPhone,
            workspace.LetterheadEmail, workspace.LetterheadRegistrationNumber, logoBytes);
    }
}
