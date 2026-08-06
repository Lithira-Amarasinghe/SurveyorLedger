using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Auth;

/// <summary>
/// Request model for user login.
/// </summary>
public class LoginRequest
{
    /// <summary>
    /// User's email address.
    /// </summary>
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    public required string Email { get; set; }

    /// <summary>
    /// User's password.
    /// </summary>
    [Required(ErrorMessage = "Password is required.")]
    public required string Password { get; set; }
}
