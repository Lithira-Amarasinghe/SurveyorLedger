namespace SurveyorLedger.Data.Entities;

public class Job
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string JobNumber { get; set; }
    public string Title { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "Draft";
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Workspace Workspace { get; set; }
    public Person CreatedByUser { get; set; }
    public ICollection<JobLand> Lands { get; set; } = new List<JobLand>();
}
