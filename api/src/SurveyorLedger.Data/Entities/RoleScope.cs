namespace SurveyorLedger.Data.Entities;

/// <summary>Which scope types a role is valid to hold - the source of truth for
/// what "Workspace" scope vs "Job" scope roles exist, replacing a hardcoded switch.</summary>
public class RoleScope
{
    public Guid RoleId { get; set; }
    public string ScopeType { get; set; }
    public Role Role { get; set; }
}
