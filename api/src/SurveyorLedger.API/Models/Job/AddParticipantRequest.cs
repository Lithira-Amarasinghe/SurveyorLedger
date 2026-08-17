using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Job;

/// <summary>The job-scoped role Admin picks for this assignment - independent of the target's workspace role.</summary>
public class AddParticipantRequest
{
    // Which role names are actually valid here is a backend decision, driven by the
    // RoleScopes table (see JobService.ResolveJobRoleAsync) - not a hardcoded list here
    // that has to be kept in sync by hand every time a role's scope changes.
    [Required(ErrorMessage = "Role is required.")]
    [StringLength(50, MinimumLength = 1)]
    public required string Role { get; set; }
}

/// <summary>
/// For someone typed by email rather than picked from search - may or may not have an
/// account yet. FirstName/LastName only required when the email matches no existing account,
/// same rule as InvitationRequest.
/// </summary>
public class InviteParticipantRequest
{
    // Which role names are actually valid here is a backend decision, driven by the
    // RoleScopes table (see JobService.ResolveJobRoleAsync) - not a hardcoded list here
    // that has to be kept in sync by hand every time a role's scope changes.
    [Required(ErrorMessage = "Role is required.")]
    [StringLength(50, MinimumLength = 1)]
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
