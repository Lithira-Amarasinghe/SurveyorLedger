using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Job;

/// <summary>
/// Request model for creating or updating a Job. Only Title is required - a job can be
/// created with nothing else known yet (e.g. straight off a phone call), with details
/// filled in later via this same endpoint or the participant/land attach endpoints.
/// </summary>
public class JobRequest
{
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Title must be between 1 and 200 characters.")]
    public required string Title { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }
}
