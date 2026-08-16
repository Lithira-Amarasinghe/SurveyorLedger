namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A real-world identity: name, contact info, address. No workspace scoping - the same
/// person can be a billing client of one workspace and a job participant of another
/// without duplicating their name/address. May or may not have a UserAccount (login).
/// </summary>
public class Person
{
    public Guid Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public Address Address { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public UserAccount? UserAccount { get; set; }
}
