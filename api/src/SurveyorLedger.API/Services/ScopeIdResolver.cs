namespace SurveyorLedger.API.Services;

/// <summary>
/// Pure dispatcher: holds no scope-type-specific logic itself, only routes to whichever
/// registered <see cref="IScopeLinkProvider"/> matches the scope types asked for. Adding a
/// new scope level means registering a new provider (see JobWorkspaceScopeLinkProvider) -
/// this class never needs to change.
/// </summary>
public class ScopeIdResolver : IScopeIdResolver
{
    private readonly IReadOnlyDictionary<string, IScopeLinkProvider> _byChildType;
    private readonly ILookup<string, IScopeLinkProvider> _byParentType;
    private readonly ILogger<ScopeIdResolver> _logger;

    public ScopeIdResolver(IEnumerable<IScopeLinkProvider> providers, ILogger<ScopeIdResolver> logger)
    {
        var providerList = providers.ToList();
        _byChildType = providerList.ToDictionary(p => p.ChildScopeType);
        _byParentType = providerList.ToLookup(p => p.ParentScopeType);
        _logger = logger;
    }

    public Task<Guid?> GetParentIdAsync(string scopeType, Guid scopeId)
    {
        if (_byChildType.TryGetValue(scopeType, out var provider))
            return provider.GetParentIdAsync(scopeId);

        // Not every scope type has a parent (e.g. Workspace, until an Organization level
        // exists) - that's a legitimate "top of the hierarchy" answer, not a warning.
        return Task.FromResult<Guid?>(null);
    }

    public Task<List<Guid>> GetChildIdsAsync(string parentScopeType, string childScopeType, Guid parentScopeId)
    {
        var provider = _byParentType[parentScopeType].FirstOrDefault(p => p.ChildScopeType == childScopeType);
        if (provider != null)
            return provider.GetChildIdsAsync(parentScopeId);

        _logger.LogWarning("No IScopeLinkProvider registered for {ParentScopeType} -> {ChildScopeType}",
            parentScopeType, childScopeType);
        return Task.FromResult(new List<Guid>());
    }
}
