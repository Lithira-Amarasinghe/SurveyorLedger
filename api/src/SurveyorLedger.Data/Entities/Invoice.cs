namespace SurveyorLedger.Data.Entities;

/// <summary>
/// Draft/Sent/PartiallyPaid/Paid/Overdue/Cancelled. Total/AmountPaid/Balance/DaysOverdue
/// are computed by InvoiceService from LineItems and Payments, never stored - see
/// InvoiceService.ComputeInvoiceTotals for the single source of truth. No WorkspaceId
/// column - tenant scoping goes through Job.WorkspaceId (see JobScopedBilling migration).
/// </summary>
public class Invoice
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid JobId { get; set; }
    public Guid? QuotationId { get; set; }
    public string Number { get; set; }
    public List<InvoiceLineItem> LineItems { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Person Client { get; set; }
    public Job Job { get; set; }
    public Quotation? Quotation { get; set; }
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
