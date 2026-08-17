namespace SurveyorLedger.API.Models.Workspace;

/// <summary>Another scope this member holds access to beyond the workspace itself - e.g. a specific job.</summary>
public class MemberScopeGrantResponse
{
    public required string ScopeType { get; set; }
    public Guid ScopeId { get; set; }
    public required string Label { get; set; }
    public required string Role { get; set; }
}

/// <summary>Blanket access this member's role grants over an entire scope type, and what it lets them do.</summary>
public class MemberFullAccessGrantResponse
{
    public required string ScopeType { get; set; }
    public required string RoleName { get; set; }
    public required List<string> Actions { get; set; }
}

public class MemberResponse
{
    public Guid UserId { get; set; }
    public required string Email { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required List<string> Roles { get; set; }
    public DateTime AssignedAt { get; set; }
    public bool IsOwner { get; set; }

    /// <summary>Blanket (view_all) access this member's role(s) grant, with the actions each allows.</summary>
    public List<MemberFullAccessGrantResponse> FullAccessGrants { get; set; } = new();

    /// <summary>Explicit extra scope grants this member holds (e.g. specific job assignments).</summary>
    public List<MemberScopeGrantResponse> AdditionalScopes { get; set; } = new();
}
