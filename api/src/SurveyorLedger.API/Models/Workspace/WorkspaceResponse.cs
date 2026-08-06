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
}
