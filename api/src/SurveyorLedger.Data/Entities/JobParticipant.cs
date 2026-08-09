namespace SurveyorLedger.Data.Entities;

/// <summary>
/// One person on a Job - client, surveyor, assistant, or other. Single join table for
/// every role rather than a separate table per role; new roles are just a new
/// ParticipantType value, no schema change.
/// </summary>
public class JobParticipant
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public Guid UserId { get; set; }
    public string ParticipantType { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid AddedBy { get; set; }
    public DateTime AddedAt { get; set; }

    public Job Job { get; set; }
    public User User { get; set; }
}
