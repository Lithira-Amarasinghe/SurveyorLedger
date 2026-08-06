namespace SurveyorLedger.Data.Entities;

public class Subscription
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Tier { get; set; } = "Free";
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Workspace Workspace { get; set; }
}
