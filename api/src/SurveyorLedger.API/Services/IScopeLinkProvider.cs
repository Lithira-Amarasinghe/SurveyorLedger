namespace SurveyorLedger.API.Services;

/// <summary>
/// Knows how to resolve IDs across exactly one parent/child scope-type pair (e.g. Job's
/// parent Workspace, or Workspace's child Jobs). ScopeIdResolver dispatches to whichever
/// provider matches the scope types involved - it holds no per-type logic of its own.
///
/// To add a new scope level (e.g. Organization above Workspace): implement this interface
/// once for the new pair and register it in Program.cs. Nothing else in the access-chaining
/// engine (ScopeIdResolver, UserAccessGrantService, AssignmentPolicy) changes.
/// </summary>
public interface IScopeLinkProvider
{
    /// <summary>The scope type whose parent this provider resolves (e.g. "Job").</summary>
    string ChildScopeType { get; }

    /// <summary>The scope type that is the parent (e.g. "Workspace").</summary>
    string ParentScopeType { get; }

    /// <summary>The parent scope ID for a given child scope ID. Null if the child scope doesn't exist.</summary>
    Task<Guid?> GetParentIdAsync(Guid childScopeId);

    /// <summary>Every child scope ID under a given parent scope ID.</summary>
    Task<List<Guid>> GetChildIdsAsync(Guid parentScopeId);
}
