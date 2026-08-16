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

/// <summary>
/// Result of adding a participant - exactly one of Participant/Invitation is set, matching
/// Status ("added" or "invited"). "invited" means nothing was granted yet; the person needs
/// to accept first, same as a workspace invite.
/// </summary>
public class AddParticipantResponse
{
    public required string Status { get; set; }
    public JobParticipantResponse? Participant { get; set; }
    public SurveyorLedger.API.Models.Invitation.InvitationResponse? Invitation { get; set; }
}
