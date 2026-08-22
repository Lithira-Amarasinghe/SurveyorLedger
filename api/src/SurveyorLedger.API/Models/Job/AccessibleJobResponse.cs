namespace SurveyorLedger.API.Models.Job;

/// <summary>One row of the cross-workspace "jobs I can open" list (GET /api/jobs/mine).</summary>
public class AccessibleJobResponse
{
    public Guid JobId { get; set; }
    public required string JobNumber { get; set; }
    public required string Title { get; set; }
    public required string Status { get; set; }
    public Guid WorkspaceId { get; set; }
    public required string WorkspaceName { get; set; }
    public Guid OrganizationId { get; set; }

    /// <summary>The real scope-type value (Constants.ScopeTypes) the access was found at - "Workspace" or "Job" today, "Organization" later.</summary>
    public required string AccessScopeType { get; set; }
}

/// <summary>A single job plus its workspace context, for a caller who may not be a workspace member (GET /api/jobs/{jobId}).</summary>
public class JobWithWorkspaceResponse
{
    public Guid JobId { get; set; }
    public required string JobNumber { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string Status { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid WorkspaceId { get; set; }
    public required string WorkspaceName { get; set; }
}
