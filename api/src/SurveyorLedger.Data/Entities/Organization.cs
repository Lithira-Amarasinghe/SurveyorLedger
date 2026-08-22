namespace SurveyorLedger.Data.Entities;

public class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public Guid OwnerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public UserAccount Owner { get; set; }
    public OrganizationSubscription? Subscription { get; set; }
    public ICollection<Workspace> Workspaces { get; set; } = new List<Workspace>();
}
