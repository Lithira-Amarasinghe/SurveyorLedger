namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A login credential for a Person - "how they sign in". A Person may have zero or one
/// UserAccount. Email lives on Person only; login lookups join through Person.Email.
/// Casbin subject id and JWT NameIdentifier are this entity's Id, never Person.Id.
/// </summary>
public class UserAccount
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public string? PasswordHash { get; set; }
    public bool EmailVerified { get; set; }
    public DateTime? EmailVerifiedAt { get; set; }

    /// <summary>
    /// Whether this account has completed signup via any method (password set, or a future
    /// OAuth login linked) - distinct from PasswordHash != null, which only means "has a
    /// password" and would be permanently false for an OAuth-only account.
    /// </summary>
    public bool HasCompletedSignup { get; set; }

    /// <summary>Consecutive failed login attempts, reset on any successful login.</summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>Set while the account is temporarily locked after too many failures. Null when not locked.</summary>
    public DateTime? LockoutEndsAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Person Person { get; set; }
    public ICollection<Workspace> OwnedWorkspaces { get; set; } = new List<Workspace>();
    public ICollection<Organization> OwnedOrganizations { get; set; } = new List<Organization>();
    public ICollection<UserAccess> UserAccesses { get; set; } = new List<UserAccess>();
    public ICollection<AuthToken> AuthTokens { get; set; } = new List<AuthToken>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
