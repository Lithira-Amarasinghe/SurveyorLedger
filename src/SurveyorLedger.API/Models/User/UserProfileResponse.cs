namespace SurveyorLedger.API.Models.User;

/// <summary>
/// Response model for user profile endpoint.
/// </summary>
public class UserProfileResponse
{
    /// <summary>
    /// The user's ID.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The user's email address.
    /// </summary>
    public required string Email { get; set; }

    /// <summary>
    /// The user's first name.
    /// </summary>
    public required string FirstName { get; set; }

    /// <summary>
    /// The user's last name.
    /// </summary>
    public required string LastName { get; set; }

    /// <summary>
    /// When the user account was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
