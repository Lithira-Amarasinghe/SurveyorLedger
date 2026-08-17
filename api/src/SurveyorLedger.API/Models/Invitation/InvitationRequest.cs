using System.ComponentModel.DataAnnotations;
using SurveyorLedger.API.Models.Land;

namespace SurveyorLedger.API.Models.Invitation;

/// <summary>
/// The single "add a person to this workspace" request - whether they're brand new or
/// already have an account elsewhere, nothing is granted until they accept. FirstName/
/// LastName/Phone/Address only apply when a new User is being created; ignored when the
/// email matches an existing account (that account's own details are used instead).
/// </summary>
public class InvitationRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "A valid email is required.")]
    public required string Email { get; set; }

    // Which role names are actually valid here is a backend decision, driven by the
    // RoleScopes table (see InvitationService.CreateInvitationAsync) - not a hardcoded list
    // here that has to be kept in sync by hand every time a role's scope changes.
    [Required(ErrorMessage = "Role is required.")]
    [StringLength(50, MinimumLength = 1)]
    public required string Role { get; set; }

    [StringLength(100, MinimumLength = 1)]
    public string? FirstName { get; set; }

    [StringLength(100, MinimumLength = 1)]
    public string? LastName { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    public AddressDto? Address { get; set; }
}
