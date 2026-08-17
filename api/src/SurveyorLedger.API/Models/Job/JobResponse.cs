namespace SurveyorLedger.API.Models.Job;

public class JobResponse
{
    public Guid JobId { get; set; }
    public required string JobNumber { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string Status { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool CanManageParticipants { get; set; }
}
