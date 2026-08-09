namespace SurveyorLedger.API.Models.Land;

public class LandBoundaryResponse
{
    public Guid Id { get; set; }
    public Guid LandId { get; set; }
    public required string Label { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}
