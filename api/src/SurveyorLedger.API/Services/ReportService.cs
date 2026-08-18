using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Report;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;

namespace SurveyorLedger.API.Services;

public interface IReportService
{
    Task<FinancialSummaryResponse> GetFinancialSummaryAsync(Guid workspaceId, Guid callerUserId, DateTime? from, DateTime? to);
    Task<PagedResult<PaymentHistoryRow>> GetPaymentHistoryAsync(Guid workspaceId, Guid callerUserId, DateTime? from, DateTime? to, int page, int pageSize);
    Task<PagedResult<ExpenseHistoryRow>> GetExpenseHistoryAsync(Guid workspaceId, Guid callerUserId, DateTime? from, DateTime? to, int page, int pageSize);
    Task<List<OutstandingInvoiceRow>> GetOutstandingInvoicesAsync(Guid workspaceId, Guid callerUserId);
}

/// <summary>
/// Admin-only, workspace-wide aggregation over existing Invoice/Payment/Expense data -
/// no new financial data, purely read-only. Every query filters by workspace directly
/// (Payment.WorkspaceId, Expense.WorkspaceId) or via the Invoice/Job join, same tenant
/// isolation guarantee as everywhere else in this codebase.
/// </summary>
public class ReportService : IReportService
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly IInvoiceService _invoiceService;

    public ReportService(ApplicationDbContext context, IScopedAccessService access, IInvoiceService invoiceService)
    {
        _context = context;
        _access = access;
        _invoiceService = invoiceService;
    }

    public async Task<FinancialSummaryResponse> GetFinancialSummaryAsync(Guid workspaceId, Guid callerUserId, DateTime? from, DateTime? to)
    {
        await _access.EnsureAllowedAsync(callerUserId, "report", "view", workspaceId);
        ValidateRange(from, to);

        // Invoiced: invoices created within range (regardless of current status).
        var invoicesInRange = await _context.Invoices
            .Include(i => i.LineItems).Include(i => i.Payments)
            .Where(i => i.Job.WorkspaceId == workspaceId)
            .Where(i => from == null || i.CreatedAt >= from)
            .Where(i => to == null || i.CreatedAt < to.Value.Date.AddDays(1))
            .ToListAsync();
        var totalInvoiced = invoicesInRange.Sum(i => _invoiceService.ComputeInvoiceTotals(i).Total);

        // Paid: payments received within range (cash-basis, independent of when the invoice was created).
        var totalPaid = await _context.Payments
            .Where(p => p.WorkspaceId == workspaceId)
            .Where(p => from == null || p.ReceivedAt >= from)
            .Where(p => to == null || p.ReceivedAt < to.Value.Date.AddDays(1))
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;

        // Outstanding: current balance across every active invoice, not date-scoped - a
        // balance is a fact about today, not something that happened "in range".
        var allInvoices = await _context.Invoices
            .Include(i => i.LineItems).Include(i => i.Payments)
            .Where(i => i.Job.WorkspaceId == workspaceId)
            .ToListAsync();
        var totalOutstanding = allInvoices.Sum(i => _invoiceService.ComputeInvoiceTotals(i).Balance);

        var totalExpenses = await _context.Expenses
            .Where(e => e.WorkspaceId == workspaceId)
            .Where(e => from == null || e.IncurredDate >= from)
            .Where(e => to == null || e.IncurredDate < to.Value.Date.AddDays(1))
            .SumAsync(e => (decimal?)e.Amount) ?? 0m;

        var grossProfit = totalPaid - totalExpenses;
        var marginPercent = totalPaid > 0 ? grossProfit / totalPaid * 100m : 0m;

        return new FinancialSummaryResponse
        {
            TotalInvoiced = totalInvoiced,
            TotalPaid = totalPaid,
            TotalOutstanding = totalOutstanding,
            TotalExpenses = totalExpenses,
            GrossProfit = grossProfit,
            ProfitMarginPercent = marginPercent
        };
    }

    public async Task<PagedResult<PaymentHistoryRow>> GetPaymentHistoryAsync(Guid workspaceId, Guid callerUserId, DateTime? from, DateTime? to, int page, int pageSize)
    {
        await _access.EnsureAllowedAsync(callerUserId, "report", "view", workspaceId);
        ValidateRange(from, to);
        (page, pageSize) = NormalizePaging(page, pageSize);

        var query = _context.Payments
            .Include(p => p.Invoice).ThenInclude(i => i.Job)
            .Include(p => p.Invoice).ThenInclude(i => i.Client)
            .Where(p => p.WorkspaceId == workspaceId)
            .Where(p => from == null || p.ReceivedAt >= from)
            .Where(p => to == null || p.ReceivedAt < to.Value.Date.AddDays(1))
            .OrderByDescending(p => p.ReceivedAt);

        var totalCount = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<PaymentHistoryRow>
        {
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Items = items.Select(p => new PaymentHistoryRow
            {
                PaymentId = p.Id,
                ReceivedAt = p.ReceivedAt,
                JobId = p.Invoice.JobId,
                JobNumber = p.Invoice.Job.JobNumber,
                JobTitle = p.Invoice.Job.Title,
                InvoiceNumber = p.Invoice.Number,
                ClientName = $"{p.Invoice.Client.FirstName} {p.Invoice.Client.LastName}",
                Amount = p.Amount,
                Method = p.Method
            }).ToList()
        };
    }

    public async Task<PagedResult<ExpenseHistoryRow>> GetExpenseHistoryAsync(Guid workspaceId, Guid callerUserId, DateTime? from, DateTime? to, int page, int pageSize)
    {
        await _access.EnsureAllowedAsync(callerUserId, "report", "view", workspaceId);
        ValidateRange(from, to);
        (page, pageSize) = NormalizePaging(page, pageSize);

        var query = _context.Expenses
            .Include(e => e.Job)
            .Include(e => e.Payee)
            .Where(e => e.WorkspaceId == workspaceId)
            .Where(e => from == null || e.IncurredDate >= from)
            .Where(e => to == null || e.IncurredDate < to.Value.Date.AddDays(1))
            .OrderByDescending(e => e.IncurredDate);

        var totalCount = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<ExpenseHistoryRow>
        {
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Items = items.Select(e => new ExpenseHistoryRow
            {
                ExpenseId = e.Id,
                IncurredDate = e.IncurredDate,
                JobId = e.JobId,
                JobNumber = e.Job.JobNumber,
                JobTitle = e.Job.Title,
                Category = e.Category,
                PayeeName = e.Payee == null ? null : $"{e.Payee.FirstName} {e.Payee.LastName}",
                Amount = e.Amount
            }).ToList()
        };
    }

    public async Task<List<OutstandingInvoiceRow>> GetOutstandingInvoicesAsync(Guid workspaceId, Guid callerUserId)
    {
        await _access.EnsureAllowedAsync(callerUserId, "report", "view", workspaceId);

        var invoices = await _context.Invoices
            .Include(i => i.LineItems).Include(i => i.Payments).Include(i => i.Job).Include(i => i.Client)
            .Where(i => i.Job.WorkspaceId == workspaceId)
            .Where(i => i.Status != "Cancelled")
            .ToListAsync();

        return invoices
            .Select(i => (Invoice: i, Totals: _invoiceService.ComputeInvoiceTotals(i)))
            .Where(x => x.Totals.Balance > 0)
            .OrderByDescending(x => x.Totals.DaysOverdue)
            .Select(x => new OutstandingInvoiceRow
            {
                InvoiceId = x.Invoice.Id,
                JobId = x.Invoice.JobId,
                JobNumber = x.Invoice.Job.JobNumber,
                JobTitle = x.Invoice.Job.Title,
                InvoiceNumber = x.Invoice.Number,
                ClientName = $"{x.Invoice.Client.FirstName} {x.Invoice.Client.LastName}",
                Total = x.Totals.Total,
                AmountPaid = x.Totals.AmountPaid,
                Balance = x.Totals.Balance,
                DueDate = x.Invoice.DueDate,
                IsOverdue = x.Totals.IsOverdue,
                DaysOverdue = x.Totals.DaysOverdue
            })
            .ToList();
    }

    private static void ValidateRange(DateTime? from, DateTime? to)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            throw new ValidationException("'from' must be on or before 'to'.");
    }

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedPageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        return (normalizedPage, normalizedPageSize);
    }
}
