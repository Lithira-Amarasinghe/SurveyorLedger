namespace SurveyorLedger.Data.Entities;

/// <summary>
/// Holds signup data (password hash, name) between registration and OTP verification.
/// No corresponding User row exists until the OTP is confirmed - prevents unverified
/// signups from permanently squatting an email address.
/// </summary>
public class PendingRegistration
{
    public Guid Id { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
