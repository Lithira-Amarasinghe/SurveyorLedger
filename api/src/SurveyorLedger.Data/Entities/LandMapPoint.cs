namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A named point on a Land's map - boundary corners, landmarks, access points, the site
/// itself. Many per Land, each independently movable and renamable; every point is equal,
/// none ranked "primary."
/// </summary>
public class LandMapPoint
{
    public Guid Id { get; set; }
    public Guid LandId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Land Land { get; set; } = null!;
}
