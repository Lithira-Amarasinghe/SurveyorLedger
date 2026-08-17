using Microsoft.EntityFrameworkCore;
using SurveyorLedger.Core;
using SurveyorLedger.Data;

namespace SurveyorLedger.API.Services;

public class ScopeIdResolver : IScopeIdResolver
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ScopeIdResolver> _logger;

    public ScopeIdResolver(ApplicationDbContext context, ILogger<ScopeIdResolver> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Guid?> GetParentIdAsync(string scopeType, Guid scopeId)
    {
        if (scopeType == Constants.ScopeTypes.Job)
        {
            var job = await _context.Jobs
                .AsNoTracking()
                .Where(j => j.Id == scopeId)
                .Select(j => (Guid?)j.WorkspaceId)
                .FirstOrDefaultAsync();
            return job;
        }

        if (scopeType == Constants.ScopeTypes.Workspace)
        {
            // Workspace has no parent (yet). Organization scope can be added later.
            return null;
        }

        _logger.LogWarning("GetParentIdAsync called with unknown scope type: {ScopeType}", scopeType);
        return null;
    }

    public async Task<List<Guid>> GetChildIdsAsync(string parentScopeType, string childScopeType, Guid parentScopeId)
    {
        if (parentScopeType == Constants.ScopeTypes.Workspace && childScopeType == Constants.ScopeTypes.Job)
        {
            return await _context.Jobs
                .AsNoTracking()
                .Where(j => j.WorkspaceId == parentScopeId)
                .Select(j => j.Id)
                .ToListAsync();
        }

        if (parentScopeType == Constants.ScopeTypes.Job)
        {
            // Job has no children yet (Milestones, Documents don't have their own role grants).
            return [];
        }

        _logger.LogWarning("GetChildIdsAsync called with unknown scope pair: {ParentScopeType} -> {ChildScopeType}",
            parentScopeType, childScopeType);
        return [];
    }
}
