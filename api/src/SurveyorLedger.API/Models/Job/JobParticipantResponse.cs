namespace SurveyorLedger.API.Models.Job;

public class JobParticipantResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Email { get; set; }
    public required string ParticipantType { get; set; }
    public DateTime AddedAt { get; set; }
}
