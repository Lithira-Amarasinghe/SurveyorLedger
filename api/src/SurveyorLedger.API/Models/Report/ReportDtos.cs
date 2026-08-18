namespace SurveyorLedger.API.Models.Report;

public class FinancialSummaryResponse
{
    public decimal TotalInvoiced { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalOutstanding { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal ProfitMarginPercent { get; set; }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class PaymentHistoryRow
{
    public Guid PaymentId { get; set; }
    public DateTime ReceivedAt { get; set; }
    public Guid JobId { get; set; }
    public string JobNumber { get; set; }
    public string JobTitle { get; set; }
    public string InvoiceNumber { get; set; }
    public string ClientName { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; }
}

public class ExpenseHistoryRow
{
    public Guid ExpenseId { get; set; }
    public DateTime IncurredDate { get; set; }
    public Guid JobId { get; set; }
    public string JobNumber { get; set; }
    public string JobTitle { get; set; }
    public string Category { get; set; }
    public string? PayeeName { get; set; }
    public decimal Amount { get; set; }
}

public class OutstandingInvoiceRow
{
    public Guid InvoiceId { get; set; }
    public Guid JobId { get; set; }
    public string JobNumber { get; set; }
    public string JobTitle { get; set; }
    public string InvoiceNumber { get; set; }
    public string ClientName { get; set; }
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Balance { get; set; }
    public DateTime? DueDate { get; set; }
    public bool IsOverdue { get; set; }
    public int DaysOverdue { get; set; }
}
