namespace SurveyorLedger.Data.Entities;

/// <summary>Data-driven rule for what happens at ancestor scopes when a role is granted.
/// RulesJson shape: { "ancestors": [ { "scopeType": "Workspace", "grantRoleId": "<guid>" } ] }
/// Ordered nearest-ancestor-first. Empty array = no chaining (single scope only).</summary>
public class AssignmentPolicy
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string RulesJson { get; set; }

    public ICollection<Role> Roles { get; set; } = new List<Role>();
}
