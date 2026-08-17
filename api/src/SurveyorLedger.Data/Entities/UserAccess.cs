namespace SurveyorLedger.Data.Entities;

public class UserAccess
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public string ScopeType { get; set; }
    public Guid ScopeId { get; set; }
    public DateTime AssignedAt { get; set; }
    public Guid? AssignedBy { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// True when this row was created by the access-chaining engine (UserAccessGrantService.
    /// GrantAncestorRolesAsync) rather than a direct grant/invite. Lets cascade-revoke tell
    /// "auto-granted baseline access" apart from "the admin deliberately gave them this role
    /// at this scope" - only the former is safe to auto-remove when nothing chains into it anymore.
    /// </summary>
    public bool IsChainGranted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public UserAccount User { get; set; }
    public Role Role { get; set; }
}
