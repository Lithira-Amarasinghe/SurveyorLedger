using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.User;

/// <summary>
/// Request model for updating user profile.
/// </summary>
public class UpdateProfileRequest
{
    /// <summary>
    /// The updated first name.
    /// </summary>
    [Required(ErrorMessage = "FirstName is required.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "FirstName must be between 1 and 100 characters.")]
    public required string FirstName { get; set; }

    /// <summary>
    /// The updated last name.
    /// </summary>
    [Required(ErrorMessage = "LastName is required.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "LastName must be between 1 and 100 characters.")]
    public required string LastName { get; set; }
}
