namespace SurveyorLedger.Data.Entities;

/// <summary>
/// Links a Job to a Land - many-to-many, so an existing Land can be reused on a new
/// Job (e.g. a resurvey) without re-entering it.
/// </summary>
public class JobLand
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public Guid LandId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public Job Job { get; set; }
    public Land Land { get; set; }
}
