using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Auth;

/// <summary>
/// Request model for OTP verification.
/// </summary>
public class VerifyOtpRequest
{
    /// <summary>
    /// User's email address.
    /// </summary>
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    public required string Email { get; set; }

    /// <summary>
    /// 6-digit OTP code sent to the user's email.
    /// </summary>
    [Required(ErrorMessage = "OtpCode is required.")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "OtpCode must be exactly 6 digits.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "OtpCode must contain only digits.")]
    public required string OtpCode { get; set; }
}
