namespace SurveyorLedger.API.Models.Workspace;

/// <summary>
/// Response model for workspace endpoints.
/// </summary>
public class WorkspaceResponse
{
    /// <summary>
    /// The unique identifier of the workspace.
    /// </summary>
    public Guid WorkspaceId { get; set; }

    /// <summary>
    /// The name of the workspace.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The optional description of the workspace.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// When the workspace was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Indicates whether the workspace is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Subscription tier for this workspace ("Free" or "Pro").
    /// </summary>
    public required string Tier { get; set; }

    /// <summary>
    /// The caller's role name(s) on this workspace.
    /// </summary>
    public required List<string> Roles { get; set; }

    /// <summary>The organization this workspace belongs to.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>The organization's name, for display without a second lookup.</summary>
    public required string OrganizationName { get; set; }
}
