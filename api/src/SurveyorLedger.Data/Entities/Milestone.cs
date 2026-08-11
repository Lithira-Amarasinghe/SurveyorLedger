namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A progress checkpoint within a Job (e.g. Site Visit, Survey Complete, Handover).
/// Tenant isolation is transitive through JobId -&gt; Job.WorkspaceId, same as
/// LandSurvey relies on LandId -&gt; Land.WorkspaceId - callers always resolve the
/// parent Job within the caller's workspace first.
/// </summary>
public class Milestone
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string Title { get; set; }
    public string? Description { get; set; }
    public DateTime? DueDate { get; set; }
    public string Status { get; set; } = "Pending";
    public int SortOrder { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedBy { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Job Job { get; set; }
    public User CreatedByUser { get; set; }
    public User? CompletedByUser { get; set; }
}
