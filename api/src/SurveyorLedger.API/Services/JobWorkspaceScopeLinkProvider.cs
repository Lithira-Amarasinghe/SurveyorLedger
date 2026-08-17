using Microsoft.EntityFrameworkCore;
using SurveyorLedger.Core;
using SurveyorLedger.Data;

namespace SurveyorLedger.API.Services;

/// <summary>The only scope relationship that exists today: a Job's parent is its Workspace.</summary>
public class JobWorkspaceScopeLinkProvider : IScopeLinkProvider
{
    private readonly ApplicationDbContext _context;

    public JobWorkspaceScopeLinkProvider(ApplicationDbContext context)
    {
        _context = context;
    }

    public string ChildScopeType => Constants.ScopeTypes.Job;
    public string ParentScopeType => Constants.ScopeTypes.Workspace;

    public Task<Guid?> GetParentIdAsync(Guid childScopeId) =>
        _context.Jobs
            .AsNoTracking()
            .Where(j => j.Id == childScopeId)
            .Select(j => (Guid?)j.WorkspaceId)
            .FirstOrDefaultAsync();

    public Task<List<Guid>> GetChildIdsAsync(Guid parentScopeId) =>
        _context.Jobs
            .AsNoTracking()
            .Where(j => j.WorkspaceId == parentScopeId)
            .Select(j => j.Id)
            .ToListAsync();
}
