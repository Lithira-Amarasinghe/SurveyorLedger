namespace SurveyorLedger.API.Models.Workspace;

public class MemberResponse
{
    public Guid UserId { get; set; }
    public required string Email { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Role { get; set; }
    public DateTime AssignedAt { get; set; }
    public bool IsOwner { get; set; }
}
