namespace SurveyorLedger.API.Models.Auth;

/// <summary>
/// Response model for authentication endpoints (register, login, verify OTP).
/// </summary>
public class AuthResponse
{
    /// <summary>
    /// The authenticated user's ID.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The authenticated user's email address.
    /// </summary>
    public required string Email { get; set; }

    /// <summary>
    /// The authenticated user's first name.
    /// </summary>
    public required string FirstName { get; set; }

    /// <summary>
    /// The authenticated user's last name.
    /// </summary>
    public required string LastName { get; set; }

    /// <summary>
    /// JWT access token for API requests.
    /// </summary>
    public required string AccessToken { get; set; }

    /// <summary>
    /// Refresh token to obtain new access tokens.
    /// </summary>
    public required string RefreshToken { get; set; }

    /// <summary>
    /// Access token expiration time in seconds.
    /// </summary>
    public int ExpiresIn { get; set; }
}
