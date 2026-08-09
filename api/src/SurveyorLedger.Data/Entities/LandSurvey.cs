namespace SurveyorLedger.Data.Entities;

/// <summary>
/// One historical survey record for a Land - many per Land, never overwritten.
/// </summary>
public class LandSurvey
{
    public Guid Id { get; set; }
    public Guid LandId { get; set; }
    public string SurveyPlanNumber { get; set; }
    public DateTime SurveyDate { get; set; }

    /// <summary>
    /// Free text, not a User FK - historical surveys routinely predate any account
    /// in this system, sometimes by decades.
    /// </summary>
    public string? SurveyedByName { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    public Land Land { get; set; }
}
