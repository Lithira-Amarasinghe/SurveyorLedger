namespace SurveyorLedger.API.Models.Land;

public class LandDeedResponse
{
    public Guid Id { get; set; }
    public Guid LandId { get; set; }
    public required string DeedNumber { get; set; }
    public DateTime IssuedDate { get; set; }
    public bool IsCurrent { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}
