using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Job;

/// <summary>The job-scoped role Admin picks for this assignment - independent of the target's workspace role.</summary>
public class AddParticipantRequest
{
    [Required(ErrorMessage = "Role is required.")]
    [RegularExpression("^(Surveyor|Client)$", ErrorMessage = "Job role must be Surveyor or Client.")]
    public required string Role { get; set; }
}
