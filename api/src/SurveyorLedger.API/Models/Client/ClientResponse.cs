namespace SurveyorLedger.API.Models.Client;

public class ClientResponse
{
    public Guid UserId { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    /// <summary>
    /// True once this client has accepted an invite and can log in.
    /// </summary>
    public bool HasLogin { get; set; }
    public DateTime CreatedAt { get; set; }
}
