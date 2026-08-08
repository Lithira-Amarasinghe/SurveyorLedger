namespace SurveyorLedger.Data.Entities;

public class Invitation
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Email { get; set; }
    public string Role { get; set; }
    public string Token { get; set; }
    public Guid InvitedBy { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Status { get; set; } = "Pending";
    public bool EmailFailed { get; set; }
    public DateTime CreatedAt { get; set; }

    public Workspace Workspace { get; set; }
    public User InvitedByUser { get; set; }
}
