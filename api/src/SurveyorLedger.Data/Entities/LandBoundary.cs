namespace SurveyorLedger.Data.Entities;

/// <summary>
/// One surrounding-property note for a Land - many per Land, arbitrary label. Not
/// restricted to a fixed North/South/East/West set; real parcels don't fit that.
/// </summary>
public class LandBoundary
{
    public Guid Id { get; set; }
    public Guid LandId { get; set; }
    public string Label { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }

    public Land Land { get; set; }
}
