namespace SurveyorLedger.Data.Entities;

public class AuthToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenType { get; set; }
    public string TokenHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public UserAccount User { get; set; }
}
