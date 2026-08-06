using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Workspace;

/// <summary>
/// Request model for creating or updating a workspace.
/// </summary>
public class WorkspaceRequest
{
    /// <summary>
    /// The name of the workspace. Must be between 2 and 100 characters.
    /// </summary>
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 100 characters.")]
    public required string Name { get; set; }

    /// <summary>
    /// Optional description of the workspace. Maximum 500 characters.
    /// </summary>
    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }
}
