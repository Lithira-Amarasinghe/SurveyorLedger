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
    Task<List<Invoice>> SearchAsync(Guid workspaceId, Guid callerUserId, Guid? clientId);
    Task<Invoice> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId);
    Task<Invoice> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, InvoiceRequest request);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId);
    Task<Payment> RecordPaymentAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, PaymentRequest request, IFormFile? proofFile);
    Task<List<Payment>> GetPaymentsAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId);
    Task SendAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, List<Guid> recipientPersonIds, string appBaseUrl);
    (decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue, int DaysOverdue) ComputeInvoiceTotals(Invoice invoice);
}

/// <summary>
/// ClientId is validated against Client/Finance UserAccess on the invoice's job, not
/// a standalone billing-client entity - see EnsureClientHoldsBillingRoleOnJobAsync.
/// </summary>
public class InvoiceService : IInvoiceService
{
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase) { "Cash", "BankTransfer", "Cheque" };

    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly IFileStorageService _fileStorage;
    private readonly IPdfService _pdfService;
    private readonly IEmailService _emailService;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(ApplicationDbContext context, IScopedAccessService access, IFileStorageService fileStorage, IPdfService pdfService, IEmailService emailService, ILogger<InvoiceService> logger)
    {
        _context = context;
        _access = access;
        _fileStorage = fileStorage;
        _pdfService = pdfService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<Invoice> CreateAsync(Guid workspaceId, Guid callerUserId, InvoiceRequest request)
    {
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, request.JobId, "create");
        await EnsureClientHoldsBillingRoleOnJobAsync(request.ClientId, request.JobId);
        ValidateLineItems(request.LineItems);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
            JobId = request.JobId,
            LineItems = request.LineItems.Select(i => new InvoiceLineItem { Id = Guid.NewGuid(), Description = i.Description.Trim(), Quantity = i.Quantity, UnitPrice = i.UnitPrice }).ToList(),
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

        _logger.LogInformation("Invoice {InvoiceId} ({Number}) created on job {JobId} by {UserId}", invoice.Id, invoice.Number, invoice.JobId, callerUserId);
        return invoice;
    }

    public async Task<List<Invoice>> SearchAsync(Guid workspaceId, Guid callerUserId, Guid? clientId)
    {
        await _access.EnsureListAllowedAsync(callerUserId, workspaceId);

        var invoices = _context.Invoices.Include(i => i.Payments).Where(i => i.Job.WorkspaceId == workspaceId);
        if (!await _access.HasViewAllAsync(callerUserId, "job", workspaceId))
        {
            var accessibleJobIds = (await _access.GetAccessibleJobsAsync(callerUserId))
                .Where(j => j.WorkspaceId == workspaceId).Select(j => j.JobId).ToHashSet();
            invoices = invoices.Where(i => accessibleJobIds.Contains(i.JobId));
        }
        if (clientId.HasValue)
            invoices = invoices.Where(i => i.ClientId == clientId.Value);

        return await invoices.OrderByDescending(i => i.CreatedAt).ToListAsync();
    }

    public async Task<Invoice> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId)
    {
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, invoice.JobId, "view");
        return invoice;
    }

    public async Task<Invoice> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, InvoiceRequest request)
    {
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, invoice.JobId, "edit");
        await EnsureClientHoldsBillingRoleOnJobAsync(request.ClientId, request.JobId);
        ValidateLineItems(request.LineItems);

        invoice.ClientId = request.ClientId;
        invoice.JobId = request.JobId;
        // Explicitly remove old rows and mark new ones Added - see
        // QuotationService.UpdateAsync for why a Clear()/reassign isn't enough for an
        // OwnsMany collection.
        foreach (var old in invoice.LineItems.ToList())
            _context.Remove(old);
        invoice.LineItems.Clear();
        foreach (var item in request.LineItems.Select(i => new InvoiceLineItem { Id = Guid.NewGuid(), Description = i.Description.Trim(), Quantity = i.Quantity, UnitPrice = i.UnitPrice }))
        {
            invoice.LineItems.Add(item);
            _context.Entry(item).State = EntityState.Added;
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
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, invoice.JobId, "delete");

        if (invoice.Payments.Count > 0)
            throw new ConflictException("Cannot delete an invoice with recorded payments. Cancel it instead.");

        invoice.IsActive = false;
        invoice.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<Payment> RecordPaymentAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, PaymentRequest request, IFormFile? proofFile)
    {
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, invoice.JobId, "create");
        if (!AllowedMethods.Contains(request.Method))
            throw new ValidationException($"Method must be one of: {string.Join(", ", AllowedMethods)}.");
        if (request.Amount <= 0)
            throw new ValidationException("Amount must be positive.");

        var (total, amountPaid, balance, _, _) = ComputeInvoiceTotals(invoice);
        if (request.Amount > balance)
            throw new ValidationException($"Amount {request.Amount} exceeds the outstanding balance of {balance}.");

        string? proofPath = null;
        if (proofFile != null)
        {
            var storedFileName = $"{Guid.NewGuid():N}_{proofFile.FileName}";
            proofPath = $"{workspaceId}/invoices/{invoiceId}/payments/{storedFileName}";
            await using var stream = proofFile.OpenReadStream();
            await _fileStorage.SaveAsync(stream, proofPath, CancellationToken.None);
        }

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
            payment.ReceiptNumber = await NextReceiptNumberAsync(workspaceId);
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

    public async Task<List<Payment>> GetPaymentsAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId)
    {
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, invoice.JobId, "view");

        return await _context.Payments
            .Where(p => p.InvoiceId == invoiceId && p.WorkspaceId == workspaceId)
            .OrderByDescending(p => p.ReceivedAt)
            .ToListAsync();
    }

    public async Task SendAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, List<Guid> recipientPersonIds, string appBaseUrl)
    {
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, invoice.JobId, "edit");

        if (recipientPersonIds.Count == 0)
            throw new ValidationException("At least one recipient is required.");

        var recipients = await _context.People
            .Where(p => recipientPersonIds.Contains(p.Id) && p.IsActive)
            .ToListAsync();
        if (recipients.Count != recipientPersonIds.Count)
            throw new NotFoundException("One or more recipients not found.");

        var eligiblePersonIds = await _context.UserAccesses
            .Include(ua => ua.User)
            .Where(ua => ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == invoice.JobId)
            .Where(ua => ua.Role.Name == Constants.SystemRoles.Client || ua.Role.Name == Constants.SystemRoles.Finance)
            .Select(ua => ua.User.PersonId)
            .ToListAsync();

        var ineligible = recipientPersonIds.Except(eligiblePersonIds).ToList();
        if (ineligible.Count > 0)
            throw new ValidationException("Every recipient must hold Client or Finance access on this invoice's job.");

        var totals = ComputeInvoiceTotals(invoice);
        var pdfBytes = _pdfService.GenerateInvoicePdf(invoice, totals);
        var linkUrl = $"{appBaseUrl.TrimEnd('/')}/app/jobs/{invoice.JobId}";

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
        var amountPaid = invoice.Payments.Sum(p => p.Amount);
        var balance = total - amountPaid;

        var isOverdue = invoice.Status is "Sent" or "PartiallyPaid" && invoice.DueDate.HasValue && invoice.DueDate.Value.Date < DateTime.UtcNow.Date;
        var daysOverdue = isOverdue ? (DateTime.UtcNow.Date - invoice.DueDate!.Value.Date).Days : 0;

        return (total, amountPaid, balance, isOverdue, daysOverdue);
    }

    private async Task<string> NextInvoiceNumberAsync(Guid workspaceId)
    {
        var count = await _context.Invoices.IgnoreQueryFilters().CountAsync(i => i.Job.WorkspaceId == workspaceId);
        return $"INV-{count + 1:D4}";
    }

    private async Task<string> NextReceiptNumberAsync(Guid workspaceId)
    {
        var count = await _context.Payments.IgnoreQueryFilters().CountAsync(p => p.WorkspaceId == workspaceId);
        return $"RCP-{count + 1:D4}";
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

    private async Task<Invoice> FindInvoiceAsync(Guid workspaceId, Guid invoiceId)
    {
        return await _context.Invoices.Include(i => i.Payments).Include(i => i.LineItems).Include(i => i.Client)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.Job.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Invoice not found");
    }
}
