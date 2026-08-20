namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A cost incurred doing a Job (staff/subcontractor/equipment/material/transport/other).
/// Tenant isolation is transitive through JobId -> Job.WorkspaceId, same as Milestone.
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
    public Guid JobId { get; set; }
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

    public Job Job { get; set; }
    public Person? Payee { get; set; }
    public Person RecordedByUser { get; set; }
}
