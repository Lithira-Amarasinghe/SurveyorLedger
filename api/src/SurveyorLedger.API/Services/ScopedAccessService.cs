using Microsoft.EntityFrameworkCore;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;

namespace SurveyorLedger.API.Services;

/// <summary>
/// The single place record-level access is decided. Job, Milestone, Document and
/// DocumentRequest all hang off a job and previously carried their own identical copy of
/// this logic; land needs the same treatment against its job links.
///
/// Two questions, deliberately answered by two different mechanisms:
///   - "may this role do this action" -> Casbin, against the workspace or the record scope.
///   - "which records can they reach" -> SQL, because Casbin decides one object at a time
///     and cannot produce a filtered, paginated list.
/// </summary>
public interface IScopedAccessService
{
    /// <summary>Plain workspace-scoped permission check - no record involved (create, list, manage).</summary>
    Task EnsureAllowedAsync(Guid userId, string resource, string action, Guid workspaceId);

    /// <summary>
    /// Gate for a list endpoint (GetJobsAsync, LandService.SearchAsync). Deliberately a
    /// membership check, not a permission check: a Member with zero job.view permission and
    /// zero job assignments should still get an empty list back, not a 403 - the downstream
    /// view_all-or-accessible-ids filter already narrows the actual data correctly, this
    /// gate only needs to reject someone with no relationship to the workspace at all.
    /// </summary>
    Task EnsureListAllowedAsync(Guid userId, Guid workspaceId);

    /// <summary>True when the caller's workspace role grants blanket visibility of a resource (the *.view_all permissions).</summary>
    Task<bool> HasViewAllAsync(Guid userId, string resource, Guid workspaceId);

    /// <summary>Permission check for one specific job. Throws <see cref="ForbiddenException"/> if denied.</summary>
    Task EnsureJobAccessAsync(Guid userId, Guid workspaceId, Guid jobId, string action);

    /// <summary>Permission check for one specific land record. Throws <see cref="ForbiddenException"/> if denied.</summary>
    Task EnsureLandAccessAsync(Guid userId, Guid workspaceId, Guid landId, string action);

    /// <summary>Job ids the caller holds a job-scoped grant on. Composable into a larger query.</summary>
    IQueryable<Guid> AccessibleJobIds(Guid userId);

    /// <summary>Land ids reachable through a job the caller is assigned to. Composable into a larger query.</summary>
    IQueryable<Guid> AccessibleLandIds(Guid userId);

    /// <summary>
    /// The role that applies to this caller for this specific job: their job-scoped grant
    /// if one exists (Client only ever has this), otherwise their workspace-scoped role
    /// (Admin/Surveyor, who don't need a per-job grant to have a role on every job).
    /// Throws <see cref="ForbiddenException"/> if neither exists - not a member at all.
    /// </summary>
    Task<string> GetEffectiveJobRoleAsync(Guid userId, Guid workspaceId, Guid jobId);
}

public class ScopedAccessService : IScopedAccessService
{
    private readonly ApplicationDbContext _context;
    private readonly ICasbinService _casbinService;

    public ScopedAccessService(ApplicationDbContext context, ICasbinService casbinService)
    {
        _context = context;
        _casbinService = casbinService;
    }

    public async Task EnsureAllowedAsync(Guid userId, string resource, string action, Guid workspaceId)
    {
        if (!await _casbinService.EnforceAsync(userId.ToString(), resource, action, workspaceId.ToString()))
            throw new ForbiddenException($"You do not have permission to {action} {resource}s in this workspace.");
    }

    public async Task EnsureListAllowedAsync(Guid userId, Guid workspaceId)
    {
        var isWorkspaceMember = await _context.UserAccesses
            .AnyAsync(ua => ua.UserId == userId && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId);
        if (isWorkspaceMember)
            return;

        var hasJobGrantInWorkspace = await AccessibleJobIds(userId)
            .AnyAsync(id => _context.Jobs.Any(j => j.Id == id && j.WorkspaceId == workspaceId));
        if (hasJobGrantInWorkspace)
            return;

        throw new ForbiddenException("You are not a member of this workspace.");
    }

    public Task<bool> HasViewAllAsync(Guid userId, string resource, Guid workspaceId) =>
        _casbinService.EnforceAsync(userId.ToString(), resource, "view_all", workspaceId.ToString());

    /// <summary>
    /// Both branches are Casbin. A job-scoped UserAccess row is already loaded as
    /// g(userId, roleName, jobId), so enforcing against the job id asks exactly the same
    /// question the workspace scope does - just one level down. No SQL needed.
    /// </summary>
    public async Task EnsureJobAccessAsync(Guid userId, Guid workspaceId, Guid jobId, string action)
    {
        if (await HasViewAllAsync(userId, "job", workspaceId))
        {
            // Blanket visibility still doesn't imply the action itself - an Admin has
            // job.delete, a hypothetical read-only auditor role would not.
            await EnsureAllowedAsync(userId, "job", action, workspaceId);
            return;
        }

        if (!await _casbinService.EnforceAsync(userId.ToString(), "job", action, jobId.ToString()))
            throw new ForbiddenException($"You do not have permission to {action} this job.");
    }

    /// <summary>
    /// Land has no scope grant of its own - it is reached through the jobs it is linked to.
    /// Casbin answers the verb; the job link answers the record. A caller with only a
    /// job-scope role (Client) is enforced against that job's scope directly, the same way
    /// EnsureJobAccessAsync does - a workspace-scope enforce would wrongly reject them since
    /// they hold no workspace-scope grouping at all.
    /// </summary>
    public async Task EnsureLandAccessAsync(Guid userId, Guid workspaceId, Guid landId, string action)
    {
        if (await HasViewAllAsync(userId, "land", workspaceId))
        {
            await EnsureAllowedAsync(userId, "land", action, workspaceId);
            return;
        }

        var linkedJobIds = await _context.JobLands
            .Where(jl => jl.LandId == landId && jl.IsActive)
            .Select(jl => jl.JobId)
            .ToListAsync();
        var accessibleJobIds = await AccessibleJobIds(userId).ToListAsync();

        foreach (var jobId in linkedJobIds.Intersect(accessibleJobIds))
        {
            if (await _casbinService.EnforceAsync(userId.ToString(), "land", action, jobId.ToString()))
                return;
        }

        throw new ForbiddenException($"You do not have permission to {action} this land record.");
    }

    public IQueryable<Guid> AccessibleJobIds(Guid userId) =>
        _context.UserAccesses
            .Where(ua => ua.UserId == userId && ua.ScopeType == Constants.ScopeTypes.Job)
            .Select(ua => ua.ScopeId);

    public IQueryable<Guid> AccessibleLandIds(Guid userId)
    {
        var jobIds = AccessibleJobIds(userId);

        // JobLand has no global query filter, so the soft-delete flag is applied by hand here.
        return _context.JobLands
            .Where(jl => jl.IsActive && jobIds.Contains(jl.JobId))
            .Select(jl => jl.LandId);
    }

    public async Task<string> GetEffectiveJobRoleAsync(Guid userId, Guid workspaceId, Guid jobId)
    {
        var jobRole = await _context.UserAccesses
            .Where(ua => ua.UserId == userId && ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == jobId)
            .Select(ua => ua.Role.Name)
            .FirstOrDefaultAsync();
        if (jobRole != null)
            return jobRole;

        var workspaceRole = await _context.UserAccesses
            .Where(ua => ua.UserId == userId && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId)
            .Select(ua => ua.Role.Name)
            .FirstOrDefaultAsync();

        return workspaceRole ?? throw new ForbiddenException("You are not a member of this workspace.");
    }
}
