using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Billing;
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
    (decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue, int DaysOverdue) ComputeInvoiceTotals(Invoice invoice);
}

/// <summary>
/// Deliberately does not depend on IClientService - ClientService depends on this
/// service (for GetBalanceAsync) and a mutual constructor dependency would be a
/// circular DI graph. ClientId is validated directly against _context.People instead.
/// </summary>
public class InvoiceService : IInvoiceService
{
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase) { "Cash", "BankTransfer", "Cheque" };

    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(ApplicationDbContext context, IScopedAccessService access, IFileStorageService fileStorage, ILogger<InvoiceService> logger)
    {
        _context = context;
        _access = access;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<Invoice> CreateAsync(Guid workspaceId, Guid callerUserId, InvoiceRequest request)
    {
        await _access.EnsureAllowedAsync(callerUserId, "invoice", "create", workspaceId);
        await EnsureClientExistsAsync(request.ClientId);
        ValidateLineItems(request.LineItems);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
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

        _logger.LogInformation("Invoice {InvoiceId} ({Number}) created in workspace {WorkspaceId} by {UserId}", invoice.Id, invoice.Number, workspaceId, callerUserId);
        return invoice;
    }

    public async Task<List<Invoice>> SearchAsync(Guid workspaceId, Guid callerUserId, Guid? clientId)
    {
        await _access.EnsureListAllowedAsync(callerUserId, workspaceId);

        var invoices = _context.Invoices.Include(i => i.Payments).Where(i => i.WorkspaceId == workspaceId);
        if (clientId.HasValue)
            invoices = invoices.Where(i => i.ClientId == clientId.Value);

        return await invoices.OrderByDescending(i => i.CreatedAt).ToListAsync();
    }

    public async Task<Invoice> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId)
    {
        await _access.EnsureAllowedAsync(callerUserId, "invoice", "view", workspaceId);
        return await FindInvoiceAsync(workspaceId, invoiceId);
    }

    public async Task<Invoice> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, InvoiceRequest request)
    {
        await _access.EnsureAllowedAsync(callerUserId, "invoice", "edit", workspaceId);
        await EnsureClientExistsAsync(request.ClientId);
        ValidateLineItems(request.LineItems);
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);

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
        await _access.EnsureAllowedAsync(callerUserId, "invoice", "delete", workspaceId);
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);

        if (invoice.Payments.Count > 0)
            throw new ConflictException("Cannot delete an invoice with recorded payments. Cancel it instead.");

        invoice.IsActive = false;
        invoice.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<Payment> RecordPaymentAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, PaymentRequest request, IFormFile? proofFile)
    {
        await _access.EnsureAllowedAsync(callerUserId, "invoice", "create", workspaceId);
        if (!AllowedMethods.Contains(request.Method))
            throw new ValidationException($"Method must be one of: {string.Join(", ", AllowedMethods)}.");
        if (request.Amount <= 0)
            throw new ValidationException("Amount must be positive.");

        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
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
            RecordedBy = callerUserId,
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
        await _access.EnsureAllowedAsync(callerUserId, "invoice", "view", workspaceId);
        await FindInvoiceAsync(workspaceId, invoiceId);

        return await _context.Payments
            .Where(p => p.InvoiceId == invoiceId && p.WorkspaceId == workspaceId)
            .OrderByDescending(p => p.ReceivedAt)
            .ToListAsync();
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
        var count = await _context.Invoices.IgnoreQueryFilters().CountAsync(i => i.WorkspaceId == workspaceId);
        return $"INV-{count + 1:D4}";
    }

    private async Task<string> NextReceiptNumberAsync(Guid workspaceId)
    {
        var count = await _context.Payments.IgnoreQueryFilters().CountAsync(p => p.WorkspaceId == workspaceId);
        return $"RCP-{count + 1:D4}";
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

    private async Task<Invoice> FindInvoiceAsync(Guid workspaceId, Guid invoiceId)
    {
        return await _context.Invoices.Include(i => i.Payments).Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Invoice not found");
    }
}
