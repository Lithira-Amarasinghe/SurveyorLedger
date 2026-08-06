using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Auth;

/// <summary>
/// Request model for user registration.
/// </summary>
public class RegisterRequest
{
    /// <summary>
    /// User's email address.
    /// </summary>
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    public required string Email { get; set; }

    /// <summary>
    /// User's password. Minimum 8 characters, maximum 256.
    /// </summary>
    [Required(ErrorMessage = "Password is required.")]
    [StringLength(256, MinimumLength = 8, ErrorMessage = "Password must be between 8 and 256 characters.")]
    public required string Password { get; set; }

    /// <summary>
    /// User's first name.
    /// </summary>
    [Required(ErrorMessage = "FirstName is required.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "FirstName must be between 1 and 100 characters.")]
    public required string FirstName { get; set; }

    /// <summary>
    /// User's last name.
    /// </summary>
    [Required(ErrorMessage = "LastName is required.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "LastName must be between 1 and 100 characters.")]
    public required string LastName { get; set; }
}
