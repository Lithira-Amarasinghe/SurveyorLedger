namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A pending grant: create the Person (if needed) and the Invitation together, up front,
/// but never touch UserAccess until they accept. ScopeType/ScopeId mirrors UserAccess's
/// shape - today always ("Workspace", workspaceId), but consistent with the rest of the
/// system if a different scope type is ever invited into directly.
/// </summary>
public class Invitation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; }
    public string ScopeType { get; set; }
    public Guid ScopeId { get; set; }
    public Guid RoleId { get; set; }
    public string Token { get; set; }
    public Guid InvitedBy { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Status { get; set; } = "Pending";
    public bool EmailFailed { get; set; }
    public DateTime CreatedAt { get; set; }

    public Person User { get; set; }
    public Role Role { get; set; }
    public UserAccount InvitedByUser { get; set; }
}
