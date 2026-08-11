using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Milestone;

/// <summary>
/// Request model for creating or updating a Milestone. Mirrors JobRequest's shape -
/// Title is the only required field.
/// </summary>
public class MilestoneRequest
{
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Title must be between 1 and 200 characters.")]
    public required string Title { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    public DateTime? DueDate { get; set; }
}
