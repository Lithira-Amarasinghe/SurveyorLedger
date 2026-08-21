namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A cost incurred doing a Job, or a workspace-level cost not tied to any job
/// (staff/subcontractor/equipment/material/transport/other). JobId is nullable - null
/// means workspace-level; MilestoneId must be null in that case, since a milestone
/// belongs to a job. Tenant isolation is via WorkspaceId directly (a first-class column
/// here, unlike Invoice/Quotation which derive it through Job.WorkspaceId).
/// Hard delete, no IsActive - corrects a mis-entered record, not meaningful history to
/// preserve once wrong (same reasoning as LandSurvey/LandDeed). No approval workflow -
/// recorded directly, matching this app's flat RBAC.
/// PayeeId/PayeeType are the old StaffPayment.UserId/Type, folded in when
/// Category == "StaffCost" - see expense-staffpayment-merge design spec. Both null for
/// every other category.
/// </summary>
public class Expense
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? JobId { get; set; }
    public string Category { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime IncurredDate { get; set; }
    public string? ReceiptFilePath { get; set; }
    public Guid? PayeeId { get; set; }
    public string? PayeeType { get; set; }
    public Guid? MilestoneId { get; set; }
    public Guid RecordedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Job? Job { get; set; }
    public Person? Payee { get; set; }
    public Person RecordedByUser { get; set; }
}
