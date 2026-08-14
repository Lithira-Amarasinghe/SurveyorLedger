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

    public Invoice Invoice { get; set; }
    public User RecordedByUser { get; set; }
}
