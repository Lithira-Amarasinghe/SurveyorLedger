namespace SurveyorLedger.API.Services;

/// <summary>
/// Resolves parent/child scope IDs using the ScopeParentType hierarchy. Used by UserAccessGrantService
/// to walk the scope chain when granting ancestor access via assignment policies.
/// </summary>
public interface IScopeIdResolver
{
    /// <summary>Get the parent scope ID for a given scope. Returns null if no parent exists.</summary>
    Task<Guid?> GetParentIdAsync(string scopeType, Guid scopeId);

    /// <summary>Get all child scope IDs under a parent scope. Returns empty list if none exist.</summary>
    Task<List<Guid>> GetChildIdsAsync(string parentScopeType, string childScopeType, Guid parentScopeId);
}
