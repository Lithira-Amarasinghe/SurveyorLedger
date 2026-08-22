namespace SurveyorLedger.Data.Entities;

public class Payment
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? ProofFilePath { get; set; }
    public string ReceiptNumber { get; set; }
    public Guid RecordedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>A refund is its own Payment row with Amount stored positive but subtracted
    /// (not added) when computing an invoice's AmountPaid - see InvoiceService.ComputeInvoiceTotals.</summary>
    public bool IsRefund { get; set; }

    /// <summary>Payments are never deleted, only voided - keeps the receipt-numbered audit
    /// trail intact. A voided payment (and a voided refund) is excluded from AmountPaid.</summary>
    public bool IsVoided { get; set; }
    public DateTime? VoidedAt { get; set; }
    public Guid? VoidedBy { get; set; }
    public string? VoidReason { get; set; }

    public Invoice Invoice { get; set; }
    public Person RecordedByUser { get; set; }
    public Person? VoidedByUser { get; set; }
}
