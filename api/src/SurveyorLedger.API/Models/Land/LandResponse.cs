namespace SurveyorLedger.API.Models.Land;

/// <summary>
/// Response model for Land endpoints.
/// </summary>
public class LandResponse
{
    public Guid LandId { get; set; }
    public AddressDto Address { get; set; } = new();
    public decimal? Size { get; set; }
    public string? SizeUnit { get; set; }
    public string? GpsCoordinates { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
