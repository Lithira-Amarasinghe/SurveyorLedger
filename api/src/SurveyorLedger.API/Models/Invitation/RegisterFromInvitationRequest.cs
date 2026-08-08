using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Invitation;

/// <summary>
/// Request model for creating an account directly from an invitation link. Email is not
/// part of this request - it comes from the invitation itself and can't be spoofed.
/// </summary>
public class RegisterFromInvitationRequest
{
    [Required(ErrorMessage = "Password is required.")]
    [StringLength(256, MinimumLength = 8, ErrorMessage = "Password must be between 8 and 256 characters.")]
    public required string Password { get; set; }

    [Required(ErrorMessage = "ConfirmPassword is required.")]
    [Compare(nameof(Password), ErrorMessage = "ConfirmPassword must match Password.")]
    public required string ConfirmPassword { get; set; }

    [Required(ErrorMessage = "FirstName is required.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "FirstName must be between 1 and 100 characters.")]
    public required string FirstName { get; set; }

    [Required(ErrorMessage = "LastName is required.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "LastName must be between 1 and 100 characters.")]
    public required string LastName { get; set; }
}
