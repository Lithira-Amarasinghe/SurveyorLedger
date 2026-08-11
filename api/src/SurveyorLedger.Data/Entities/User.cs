namespace SurveyorLedger.Data.Entities;

public class User
{
    public Guid Id { get; set; }
    public string? Email { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string? PasswordHash { get; set; }
    public string? Phone { get; set; }
    public Address Address { get; set; } = new();
    public bool EmailVerified { get; set; }
    public DateTime? EmailVerifiedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Consecutive failed login attempts, reset on any successful login. Drives the
    /// temporary lockout below - the per-account half of brute-force protection (the
    /// per-IP rate limiter in Program.cs is the other half; neither covers the other's
    /// attack shape).
    /// </summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>Set while the account is temporarily locked after too many failures. Null when not locked.</summary>
    public DateTime? LockoutEndsAt { get; set; }

    public ICollection<Workspace> OwnedWorkspaces { get; set; } = new List<Workspace>();
    public ICollection<UserAccess> UserAccesses { get; set; } = new List<UserAccess>();
    public ICollection<AuthToken> AuthTokens { get; set; } = new List<AuthToken>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
