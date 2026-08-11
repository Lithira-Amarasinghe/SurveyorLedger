namespace SurveyorLedger.API.Models.Job;

/// <summary>
/// A person with job-scoped access to this job. Role is a live read of their current
/// UserAccess role at this job's scope, not a stored/independent value.
/// </summary>
public class JobParticipantResponse
{
    public Guid UserId { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Email { get; set; }
    public required string Role { get; set; }
    public DateTime AssignedAt { get; set; }
}
