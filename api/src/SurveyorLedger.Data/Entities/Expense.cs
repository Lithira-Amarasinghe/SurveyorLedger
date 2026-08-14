namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A cost incurred doing a Job (travel, equipment, printing, third-party/government
/// fees, misc). Tenant isolation is transitive through JobId -> Job.WorkspaceId, same
/// as Milestone. Hard delete, no IsActive - corrects a mis-entered record, not
/// meaningful history to preserve once wrong (same reasoning as LandSurvey/LandDeed).
/// No approval workflow - recorded directly, matching this app's flat RBAC.
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
    public Guid RecordedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Job Job { get; set; }
    public User RecordedByUser { get; set; }
}
