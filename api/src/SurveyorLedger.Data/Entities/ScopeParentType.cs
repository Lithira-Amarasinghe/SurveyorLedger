namespace SurveyorLedger.Data.Entities;

/// <summary>Maps a scope type to its parent scope type - the one place the hierarchy shape
/// is declared for access-chaining purposes. Adding Organization above Workspace later is one
/// new row here, nothing else changes.</summary>
public class ScopeParentType
{
    public required string ScopeType { get; set; }
    public string? ParentScopeType { get; set; }
}
