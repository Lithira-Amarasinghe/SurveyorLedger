namespace SurveyorLedger.API.Models.Land;

public class LandSurveyResponse
{
    public Guid Id { get; set; }
    public Guid LandId { get; set; }
    public required string SurveyPlanNumber { get; set; }
    public DateTime SurveyDate { get; set; }
    public string? SurveyedByName { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}
