namespace SurveyorLedger.API.Models.User;

/// <summary>
/// Minimal person info for cross-workspace search (e.g. picking a land owner) -
/// deliberately excludes anything workspace/access-related.
/// </summary>
public class UserSearchResponse
{
    public Guid UserId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Email { get; set; }
}
