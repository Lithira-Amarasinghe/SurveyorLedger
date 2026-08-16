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

    /// <summary>Set only for a Job-scope invite - "JOB-0001 · Title", so the UI can show they're joining one job, not the whole workspace.</summary>
    public string? JobLabel { get; set; }
}

public class AcceptInvitationResponse
{
    public Guid WorkspaceId { get; set; }
    public required string Role { get; set; }

    /// <summary>Set only for a Job-scope invite - the UI routes here instead of the workspace overview, since a job-only grant doesn't include workspace.view.</summary>
    public Guid? JobId { get; set; }
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

    /// <summary>Set only for a Job-scope invite - "JOB-0001 · Title", so the UI can show they're joining one job, not the whole workspace.</summary>
    public string? JobLabel { get; set; }
}
