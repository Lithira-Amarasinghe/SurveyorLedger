using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Job;

/// <summary>The job-scoped role Admin picks for this assignment - independent of the target's workspace role.</summary>
public class AddParticipantRequest
{
    [Required(ErrorMessage = "Role is required.")]
    [RegularExpression("^(Surveyor|Client)$", ErrorMessage = "Job role must be Surveyor or Client.")]
    public required string Role { get; set; }
}

/// <summary>
/// For someone typed by email rather than picked from search - may or may not have an
/// account yet. FirstName/LastName only required when the email matches no existing account,
/// same rule as InvitationRequest.
/// </summary>
public class InviteParticipantRequest
{
    [Required(ErrorMessage = "Role is required.")]
    [RegularExpression("^(Surveyor|Client)$", ErrorMessage = "Job role must be Surveyor or Client.")]
    public required string Role { get; set; }

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "A valid email is required.")]
    public required string Email { get; set; }

    [StringLength(100, MinimumLength = 1)]
    public string? FirstName { get; set; }

    [StringLength(100, MinimumLength = 1)]
    public string? LastName { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    public SurveyorLedger.API.Models.Land.AddressDto? Address { get; set; }
}
