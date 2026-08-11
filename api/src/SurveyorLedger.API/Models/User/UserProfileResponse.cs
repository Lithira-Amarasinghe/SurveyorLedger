using SurveyorLedger.API.Models.Land;

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
    /// The user's email address. Null for a client who hasn't been invited/verified yet.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// The user's first name.
    /// </summary>
    public required string FirstName { get; set; }

    /// <summary>
    /// The user's last name.
    /// </summary>
    public required string LastName { get; set; }

    public string? Phone { get; set; }

    public AddressDto? Address { get; set; }

    /// <summary>Whether the email has been confirmed - false for someone added but not yet accepted.</summary>
    public bool EmailVerified { get; set; }

    /// <summary>
    /// When the user account was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
