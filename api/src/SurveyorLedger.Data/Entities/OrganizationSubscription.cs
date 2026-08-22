namespace SurveyorLedger.Data.Entities;

public class OrganizationSubscription
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Tier { get; set; } = "Free";
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; }
}
