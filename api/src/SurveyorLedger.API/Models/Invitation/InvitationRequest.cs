using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Invitation;

public class InvitationRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "A valid email is required.")]
    public required string Email { get; set; }

    [Required(ErrorMessage = "Role is required.")]
    [RegularExpression("^(Admin|Manager|Surveyor|Client)$", ErrorMessage = "Role must be Admin, Manager, Surveyor, or Client.")]
    public required string Role { get; set; }

    /// <summary>
    /// Optional - set when inviting a specific pre-existing User (e.g. a client created
    /// during a call with only a name/phone) to attach this email/login to that exact
    /// record, instead of the default behavior of matching/creating an account by email.
    /// </summary>
    public Guid? UserId { get; set; }
}
