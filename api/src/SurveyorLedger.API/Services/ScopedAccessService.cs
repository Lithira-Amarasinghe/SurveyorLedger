using Microsoft.EntityFrameworkCore;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

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
public record AccessibleJob(
    Guid JobId, string JobNumber, string Title, string Status,
    Guid WorkspaceId, string WorkspaceName, string AccessScopeType);

public interface IScopedAccessService
{
    /// <summary>Plain workspace-scoped permission check - no record involved (create, list, manage).</summary>
    Task EnsureAllowedAsync(Guid userId, string resource, string action, Guid workspaceId);

    /// <summary>Non-throwing version of EnsureAllowedAsync - for callers that need a boolean
    /// to drive UI flags (e.g. JobResponse.CanViewBudget) rather than an exception to catch.</summary>
    Task<bool> CanAsync(Guid userId, string resource, string action, Guid workspaceId);

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

    /// <summary>
    /// Non-throwing version of the same rule EnsureJobAccessAsync enforces (blanket job.view_all
    /// bypass at Workspace scope, else a per-job Casbin check) - for callers that need a boolean
    /// to drive UI, not an exception to catch. EnsureJobAccessAsync itself is untouched; this is
    /// a new method, not a refactor, so its existing error messages and tests can't regress.
    /// </summary>
    Task<bool> CanAccessJobAsync(Guid userId, Guid workspaceId, Guid jobId, string action);

    /// <summary>Permission check for one specific land record. Throws <see cref="ForbiddenException"/> if denied.</summary>
    Task EnsureLandAccessAsync(Guid userId, Guid workspaceId, Guid landId, string action);

    /// <summary>Job ids the caller holds a job-scoped grant on. Composable into a larger query.</summary>
    IQueryable<Guid> AccessibleJobIds(Guid userId);

    /// <summary>Land ids reachable through a job the caller is assigned to. Composable into a larger query.</summary>
    IQueryable<Guid> AccessibleLandIds(Guid userId);

    /// <summary>
    /// The role(s) that apply to this caller for this specific job: their job-scoped grants
    /// if any exist (Client only ever has this), otherwise their workspace-scoped role(s)
    /// (Admin/Surveyor, who don't need a per-job grant to have a role on every job). A user
    /// can hold more than one role at a scope, so this returns all of them.
    /// Throws <see cref="ForbiddenException"/> if neither exists - not a member at all.
    /// </summary>
    Task<List<string>> GetEffectiveJobRolesAsync(Guid userId, Guid workspaceId, Guid jobId);

    /// <summary>
    /// Whether granting this UserAccount (userId = UserAccount.Id) access at (scopeType, scopeId) needs no fresh consent -
    /// true if they already hold active access at that exact scope, or at any ancestor scope
    /// above it (e.g. a workspace member being added to a job under that workspace). False
    /// means an invitation is required instead of an instant grant. Hierarchy-agnostic: the
    /// ancestor walk is one small switch here, so adding a level above Workspace later is one
    /// more branch, not a rewrite of every call site.
    /// </summary>
    Task<bool> HasConsentCoverageAsync(Guid userId, string scopeType, Guid scopeId);

    /// <summary>Every job this UserAccount (userId = UserAccount.Id) can open, across every workspace, tagged with the
    /// real Constants.ScopeTypes value the access was found at (broadest wins, deduped).
    /// Deliberately not workspace-filtered - see Global Constraints.</summary>
    Task<List<AccessibleJob>> GetAccessibleJobsAsync(Guid userId);

    /// <summary>Resolves a caller's UserAccount.Id (the JWT subject) to the Person.Id behind it - needed
    /// wherever an actor field (CreatedBy, RecordedBy, UploadedBy, ...) means Person, not UserAccount.</summary>
    Task<Guid> ResolvePersonIdAsync(Guid userAccountId);

    /// <summary>
    /// Everyone who can actually reach <paramref name="scopeId"/>: direct grants at that exact
    /// scope, plus anyone holding a *.view_all permission for <paramref name="resource"/> at
    /// any ancestor scope above it (e.g. Admin's job.view_all at Workspace covers every Job
    /// underneath without a per-job row). Walks the full ancestor chain via IScopeIdResolver -
    /// hierarchy-agnostic, so a level added above Workspace later is covered with zero changes
    /// here, and a new *.view_all permission on any role is picked up automatically since the
    /// check queries RolePermissions directly rather than a hardcoded role/action.
    /// </summary>
    Task<List<UserAccess>> GetUsersWithAccessAsync(string scopeType, Guid scopeId, string resource);
}

public class ScopedAccessService : IScopedAccessService
{
    private readonly ApplicationDbContext _context;
    private readonly ICasbinService _casbinService;
    private readonly IScopeIdResolver _scopeIdResolver;

    public ScopedAccessService(ApplicationDbContext context, ICasbinService casbinService, IScopeIdResolver scopeIdResolver)
    {
        _context = context;
        _casbinService = casbinService;
        _scopeIdResolver = scopeIdResolver;
    }

    public async Task EnsureAllowedAsync(Guid userId, string resource, string action, Guid workspaceId)
    {
        if (!await _casbinService.EnforceAsync(userId.ToString(), resource, action, workspaceId.ToString()))
            throw new ForbiddenException($"You do not have permission to {action} {resource}s in this workspace.");
    }

    public Task<bool> CanAsync(Guid userId, string resource, string action, Guid workspaceId) =>
        _casbinService.EnforceAsync(userId.ToString(), resource, action, workspaceId.ToString());

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

    public async Task<bool> CanAccessJobAsync(Guid userId, Guid workspaceId, Guid jobId, string action)
    {
        if (await HasViewAllAsync(userId, "job", workspaceId))
            return await _casbinService.EnforceAsync(userId.ToString(), "job", action, workspaceId.ToString());

        return await _casbinService.EnforceAsync(userId.ToString(), "job", action, jobId.ToString());
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

    public async Task<List<string>> GetEffectiveJobRolesAsync(Guid userId, Guid workspaceId, Guid jobId)
    {
        var jobRoles = await _context.UserAccesses
            .Where(ua => ua.UserId == userId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == jobId)
            .Select(ua => ua.Role.Name)
            .ToListAsync();
        if (jobRoles.Count > 0)
            return jobRoles;

        var workspaceRoles = await _context.UserAccesses
            .Where(ua => ua.UserId == userId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId)
            .Select(ua => ua.Role.Name)
            .ToListAsync();

        if (workspaceRoles.Count == 0)
            throw new ForbiddenException("You are not a member of this workspace.");

        return workspaceRoles;
    }

    public async Task<bool> HasConsentCoverageAsync(Guid userId, string scopeType, Guid scopeId)
    {
        var hasThisScope = await _context.UserAccesses
            .AnyAsync(ua => ua.UserId == userId && ua.IsActive && ua.ScopeType == scopeType && ua.ScopeId == scopeId);
        if (hasThisScope)
            return true;

        // Ancestor walk via IScopeIdResolver - hierarchy-agnostic, so a level added above
        // Workspace later (Organization) is covered by registering one more IScopeLinkProvider,
        // not another branch here.
        foreach (var (ancestorType, ancestorId) in await GetAncestorScopesAsync(scopeType, scopeId))
        {
            var hasAncestorScope = await _context.UserAccesses
                .AnyAsync(ua => ua.UserId == userId && ua.IsActive && ua.ScopeType == ancestorType && ua.ScopeId == ancestorId);
            if (hasAncestorScope)
                return true;
        }

        return false;
    }

    /// <summary>Walks from (scopeType, scopeId) to the top of the hierarchy via IScopeIdResolver, returning each ancestor (type, id) in order, nearest first.</summary>
    private async Task<List<(string ScopeType, Guid ScopeId)>> GetAncestorScopesAsync(string scopeType, Guid scopeId)
    {
        var ancestors = new List<(string, Guid)>();
        var currentType = scopeType;
        var currentId = scopeId;

        while (true)
        {
            var parentType = _scopeIdResolver.GetParentScopeType(currentType);
            if (parentType == null)
                break;

            var parentId = await _scopeIdResolver.GetParentIdAsync(currentType, currentId);
            if (parentId == null)
                break;

            ancestors.Add((parentType, parentId.Value));
            currentType = parentType;
            currentId = parentId.Value;
        }

        return ancestors;
    }

    public async Task<List<UserAccess>> GetUsersWithAccessAsync(string scopeType, Guid scopeId, string resource)
    {
        var direct = await _context.UserAccesses
            .Include(ua => ua.Role)
            .Include(ua => ua.User).ThenInclude(a => a.Person)
            .Where(ua => ua.ScopeType == scopeType && ua.ScopeId == scopeId && ua.IsActive)
            .ToListAsync();

        var ancestors = await GetAncestorScopesAsync(scopeType, scopeId);
        if (ancestors.Count == 0)
            return direct;

        // Which roles hold blanket ("*.view_all") access to this resource - queried straight
        // off RolePermissions, not any hardcoded role name, so a new role granted job.view_all
        // later (or any resource's view_all) is picked up with zero code change here.
        var viewAllRoleIds = await _context.RolePermissions
            .Where(rp => rp.Permission.Resource == resource && rp.Permission.Action == "view_all")
            .Select(rp => rp.RoleId)
            .ToListAsync();
        if (viewAllRoleIds.Count == 0)
            return direct;

        var implicitAccess = new List<UserAccess>();
        foreach (var (ancestorType, ancestorId) in ancestors)
        {
            var rows = await _context.UserAccesses
                .Include(ua => ua.Role)
                .Include(ua => ua.User).ThenInclude(a => a.Person)
                .Where(ua => ua.ScopeType == ancestorType && ua.ScopeId == ancestorId && ua.IsActive && viewAllRoleIds.Contains(ua.RoleId))
                .ToListAsync();
            implicitAccess.AddRange(rows);
        }

        return direct.Concat(implicitAccess).ToList();
    }

    /// <summary>
    /// Cross-workspace - deliberately not filtered by a single WorkspaceId, unlike every other
    /// query in this codebase. This is user-scoped ("what can this caller see"), the same
    /// category of exception as WorkspaceService.GetUserWorkspacesAsync and
    /// InvitationService.GetMyInvitationsAsync, both of which also span every workspace for the
    /// calling user. Every job returned is still independently permission-checked below.
    /// </summary>
    public async Task<List<AccessibleJob>> GetAccessibleJobsAsync(Guid userId)
    {
        // Workspace-level: workspaces where a held role carries job.view_all. A plain
        // Workspace-scope UserAccess row (e.g. Member) does NOT qualify on its own - only a role
        // whose permissions include job.view_all does. See spec's "qualifying grant" definition.
        var workspaceAccesses = await _context.UserAccesses
            .Where(ua => ua.UserId == userId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace)
            .ToListAsync();
        var workspaceRoleIds = workspaceAccesses.Select(a => a.RoleId).Distinct().ToList();
        var viewAllRoleIds = await _context.RolePermissions
            .Include(rp => rp.Permission)
            .Where(rp => workspaceRoleIds.Contains(rp.RoleId) && rp.Permission.Resource == "job" && rp.Permission.Action == "view_all")
            .Select(rp => rp.RoleId)
            .ToListAsync();
        var viewAllWorkspaceIds = workspaceAccesses
            .Where(a => viewAllRoleIds.Contains(a.RoleId))
            .Select(a => a.ScopeId)
            .Distinct()
            .ToList();

        var workspaceLevelJobs = await _context.Jobs
            .Where(j => viewAllWorkspaceIds.Contains(j.WorkspaceId))
            .ToListAsync();
        var claimedJobIds = workspaceLevelJobs.Select(j => j.Id).ToHashSet();

        // Job-level: direct job-scope grants not already claimed above. (Organization level, when
        // it exists, inserts here - broader than Workspace, narrower than nothing above it - as
        // one more block following this same shape: find qualifying grants at that level, add
        // to claimedJobIds, tag Constants.ScopeTypes.Organization.)
        var directJobIds = await AccessibleJobIds(userId).ToListAsync();
        var jobLevelJobs = await _context.Jobs
            .Where(j => directJobIds.Contains(j.Id) && !claimedJobIds.Contains(j.Id))
            .ToListAsync();

        var tagged = workspaceLevelJobs.Select(j => (Job: j, Scope: Constants.ScopeTypes.Workspace))
            .Concat(jobLevelJobs.Select(j => (Job: j, Scope: Constants.ScopeTypes.Job)))
            .ToList();

        var workspaceIds = tagged.Select(t => t.Job.WorkspaceId).Distinct().ToList();
        var workspaceNames = await _context.Workspaces
            .Where(w => workspaceIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.Name);

        return tagged
            .Select(t => new AccessibleJob(
                t.Job.Id, t.Job.JobNumber, t.Job.Title, t.Job.Status,
                t.Job.WorkspaceId, workspaceNames.GetValueOrDefault(t.Job.WorkspaceId, "Unknown workspace"),
                t.Scope))
            .ToList();
    }

    public async Task<Guid> ResolvePersonIdAsync(Guid userAccountId) =>
        await _context.UserAccounts.Where(a => a.Id == userAccountId).Select(a => a.PersonId).FirstAsync();
}
