namespace SurveyorLedger.Data.Entities;

public class Workspace
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public Guid OwnerId { get; set; }
    public string SubscriptionTier { get; set; } = "Free";
    public Guid? OrganizationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public string? LetterheadCompanyName { get; set; }
    public string? LetterheadAddress { get; set; }
    public string? LetterheadPhone { get; set; }
    public string? LetterheadEmail { get; set; }
    public string? LetterheadRegistrationNumber { get; set; }
    public string? LetterheadLogoPath { get; set; }

    public UserAccount Owner { get; set; }
    public Organization? Organization { get; set; }
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    public ICollection<UserAccess> UserAccesses { get; set; } = new List<UserAccess>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
