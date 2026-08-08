namespace SurveyorLedger.Data.Entities;

public class AuditLog
{
    public Guid Id { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; }
    public string ResourceType { get; set; }
    public Guid? ResourceId { get; set; }
    public string ScopeType { get; set; }
    public Guid? ScopeId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? IPAddress { get; set; }
    public DateTime CreatedAt { get; set; }

    public Workspace Workspace { get; set; }
    public User User { get; set; }
}
