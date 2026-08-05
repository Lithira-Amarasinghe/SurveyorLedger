using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Auth;

/// <summary>
/// Request model for token refresh.
/// </summary>
public class RefreshTokenRequest
{
    /// <summary>
    /// The refresh token used to obtain a new access token.
    /// </summary>
    [Required(ErrorMessage = "RefreshToken is required.")]
    [StringLength(int.MaxValue, MinimumLength = 1, ErrorMessage = "RefreshToken cannot be empty.")]
    public required string RefreshToken { get; set; }
}
