using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Auth;

public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    public required string Email { get; set; }
}
