namespace SurveyorLedger.API.Models.Land;

/// <summary>
/// Returned from the public map-view preview endpoint. Read-only - unlike the
/// add-a-point link's preview, there's no write action this response needs to support.
/// </summary>
public class LandMapViewLinkPreviewResponse
{
    public string AddressLine { get; set; } = string.Empty;
    public List<LandMapPointResponse> Points { get; set; } = new();
}
