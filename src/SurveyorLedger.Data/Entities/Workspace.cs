namespace SurveyorLedger.Data.Entities;

public class Workspace
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public Guid OwnerId { get; set; }
    public string SubscriptionTier { get; set; } = "Free";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public User Owner { get; set; }
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    public ICollection<UserAccess> UserAccesses { get; set; } = new List<UserAccess>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
