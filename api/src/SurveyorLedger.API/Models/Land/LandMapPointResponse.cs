namespace SurveyorLedger.API.Models.Land;

public class LandMapPointResponse
{
    public Guid Id { get; set; }
    public Guid LandId { get; set; }
    public required string Name { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTime CreatedAt { get; set; }
}
