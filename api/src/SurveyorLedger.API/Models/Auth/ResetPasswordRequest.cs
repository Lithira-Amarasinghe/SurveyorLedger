using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Auth;

public class ResetPasswordRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    public required string Email { get; set; }

    [Required(ErrorMessage = "OtpCode is required.")]
    public required string OtpCode { get; set; }

    [Required(ErrorMessage = "NewPassword is required.")]
    [StringLength(256, MinimumLength = 8, ErrorMessage = "NewPassword must be between 8 and 256 characters.")]
    public required string NewPassword { get; set; }
}
