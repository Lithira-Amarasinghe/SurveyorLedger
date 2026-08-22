using Microsoft.EntityFrameworkCore;
using SurveyorLedger.Core;
using SurveyorLedger.Data;

namespace SurveyorLedger.API.Services;

/// <summary>A Workspace's parent is its Organization.</summary>
public class WorkspaceOrganizationScopeLinkProvider : IScopeLinkProvider
{
    private readonly ApplicationDbContext _context;

    public WorkspaceOrganizationScopeLinkProvider(ApplicationDbContext context)
    {
        _context = context;
    }

    public string ChildScopeType => Constants.ScopeTypes.Workspace;
    public string ParentScopeType => Constants.ScopeTypes.Organization;

    public Task<Guid?> GetParentIdAsync(Guid childScopeId) =>
        _context.Workspaces
            .AsNoTracking()
            .Where(w => w.Id == childScopeId)
            .Select(w => (Guid?)w.OrganizationId)
            .FirstOrDefaultAsync();

    public Task<List<Guid>> GetChildIdsAsync(Guid parentScopeId) =>
        _context.Workspaces
            .AsNoTracking()
            .Where(w => w.OrganizationId == parentScopeId)
            .Select(w => w.Id)
            .ToListAsync();
}
