namespace SurveyorLedger.Data.Entities;

/// <summary>
/// Draft/Sent/PartiallyPaid/Paid/Overdue/Cancelled. Total/AmountPaid/Balance/DaysOverdue
/// are computed by InvoiceService from LineItems and Payments, never stored - see
/// InvoiceService.ComputeInvoiceTotals for the single source of truth. JobId is nullable -
/// null means workspace-level (not tied to any job). WorkspaceId is a first-class column
/// (not derived through Job) so tenant isolation still works when JobId is null.
/// Quotation linkage lives per-line only - see InvoiceLineItem.QuotationLineId. No
/// ClientId - who can see this is governed by job-scoped or workspace-scoped permissions,
/// not a stored client reference.
/// </summary>
public class Invoice
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? JobId { get; set; }
    public string Number { get; set; }
    public List<InvoiceLineItem> LineItems { get; set; } = new();
    public List<InvoiceInstallment> Installments { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Job? Job { get; set; }
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
