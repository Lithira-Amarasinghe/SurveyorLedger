namespace SurveyorLedger.API.Models.Land;

/// <summary>
/// Returned only from generate/regenerate - the raw token never appears in LandResponse,
/// so it doesn't casually show up in every list/get call an authenticated browser makes.
/// </summary>
public class LandLocationShareLinkResponse
{
    public string Token { get; set; } = string.Empty;
}
