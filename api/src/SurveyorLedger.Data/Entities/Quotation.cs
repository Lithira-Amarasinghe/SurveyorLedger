namespace SurveyorLedger.Data.Entities;

/// <summary>
/// Draft/Sent/Accepted/Rejected/Expired. RevisionNumber bumps whenever line items are
/// edited after Status has reached Sent - covers "revision charges" without a new entity.
/// </summary>
public class Quotation
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ClientId { get; set; }
    public Guid? JobId { get; set; }
    public string Number { get; set; }
    public List<QuotationLineItem> LineItems { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime? ValidUntil { get; set; }
    public int RevisionNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Workspace Workspace { get; set; }
    public Client Client { get; set; }
    public Job? Job { get; set; }
}
