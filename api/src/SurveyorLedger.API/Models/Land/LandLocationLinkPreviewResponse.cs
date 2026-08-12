namespace SurveyorLedger.API.Models.Land;

/// <summary>
/// Returned from the public preview endpoint. Deliberately excludes land id, owner,
/// and workspace name - the recipient already knows which land this is about from
/// context, and nothing here should be useful to someone who merely intercepts the URL.
/// </summary>
public class LandLocationLinkPreviewResponse
{
    public string AddressLine { get; set; } = string.Empty;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
}
