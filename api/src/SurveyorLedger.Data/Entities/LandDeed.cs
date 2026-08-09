namespace SurveyorLedger.Data.Entities;

/// <summary>
/// One deed record for a Land - many per Land, supports government reissue. Old deeds
/// stay (IsCurrent=false) rather than being overwritten or deleted.
/// </summary>
public class LandDeed
{
    public Guid Id { get; set; }
    public Guid LandId { get; set; }
    public string DeedNumber { get; set; }
    public DateTime IssuedDate { get; set; }
    public bool IsCurrent { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    public Land Land { get; set; }
}
