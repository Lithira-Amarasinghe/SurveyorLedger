namespace SurveyorLedger.API.Models.Invitation;

public class InvitationResponse
{
    public Guid InvitationId { get; set; }
    public required string Email { get; set; }
    public required string Role { get; set; }
    public DateTime ExpiresAt { get; set; }
    public required string Status { get; set; }
}

public class InvitationListItemResponse
{
    public Guid InvitationId { get; set; }
    public required string Email { get; set; }
    public required string Role { get; set; }
    public required string Status { get; set; }
    public DateTime ExpiresAt { get; set; }
    public required string InvitedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool EmailFailed { get; set; }
}

public class InvitationPreviewResponse
{
    public Guid InvitationId { get; set; }
    public required string Email { get; set; }
    public required string WorkspaceName { get; set; }
    public required string Role { get; set; }
    public bool Expired { get; set; }

    /// <summary>True if the account already has a password - the UI can skip the "create account" tab.</summary>
    public bool HasLogin { get; set; }
}

public class AcceptInvitationResponse
{
    public Guid WorkspaceId { get; set; }
    public required string Role { get; set; }
}

public class MyInvitationResponse
{
    public Guid InvitationId { get; set; }
    public required string WorkspaceName { get; set; }
    public required string Role { get; set; }
    public required string Status { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>True if the account already has a password - accept just confirms, no password-setup step needed.</summary>
    public bool HasLogin { get; set; }
}
