namespace SurveyorLedger.Data.Entities;

public class EmailVerification
{
    public Guid Id { get; set; }
    public string Email { get; set; }
    public string OTPCodeHash { get; set; }
    public string TokenType { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? LastSentAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
