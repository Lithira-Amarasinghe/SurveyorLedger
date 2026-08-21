using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Workspace;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public record WorkspaceWithAccess(Workspace Workspace, string Tier, List<string> Roles);

/// <summary>Another scope this member holds access to beyond the workspace itself - e.g. a specific job.</summary>
public record MemberScopeGrant(string ScopeType, Guid ScopeId, string Label, string Role);

/// <summary>Blanket (view_all) access this member's role grants over an entire scope type -
/// e.g. Admin's job.view_all gives "Admin" role, "Job" scope, ["view","edit","manage_participants"] actions.</summary>
public record MemberFullAccessGrant(string ScopeType, string RoleName, List<string> Actions);

public record MemberInfo(
    Guid UserId, string Email, string FirstName, string LastName, List<string> Roles, DateTime AssignedAt, bool IsOwner,
    List<MemberFullAccessGrant> FullAccessGrants, List<MemberScopeGrant> AdditionalScopes);

public record PermissionInfo(string Name, string Resource, string Action, string Description);

public record RoleWithPermissions(Guid Id, string Name, string? Description, List<PermissionInfo> Permissions);

public record WorkspaceLetterhead(
    string? CompanyName, string? Address, string? Phone, string? Email, string? RegistrationNumber, bool HasLogo);

public interface IWorkspaceService
{
    Task<WorkspaceWithAccess> CreateWorkspaceAsync(Guid userId, WorkspaceRequest request);
    Task<List<WorkspaceWithAccess>> GetUserWorkspacesAsync(Guid userId);
    Task<WorkspaceWithAccess?> GetWorkspaceByIdAsync(Guid workspaceId, Guid userId);
    Task<List<MemberInfo>> GetMembersAsync(Guid workspaceId, Guid callerUserId);
    Task AddMemberRoleAsync(Guid workspaceId, Guid targetUserId, Guid callerUserId, string roleName);
    Task RemoveMemberRoleAsync(Guid workspaceId, Guid targetUserId, Guid callerUserId, string roleName);
    Task RemoveMemberAsync(Guid workspaceId, Guid targetUserId, Guid callerUserId);
    Task<List<RoleWithPermissions>> GetWorkspaceRolesAsync(Guid workspaceId, Guid callerUserId);

    /// <summary>
    /// Role names valid to pick in a given context - reads the RoleScopes table, the single
    /// source of truth for which roles apply at which scope (Workspace: Admin/Surveyor/Member,
    /// Job: Surveyor/Client today). Mirrors the RegularExpression validators on
    /// InvitationRequest/MemberRoleRequest/AddParticipantRequest - those stay as the
    /// actual server-side enforcement, this just lets the UI reflect the same rule instead
    /// of carrying its own hardcoded copy that can drift out of sync.
    /// </summary>
    Task<List<string>> GetEligibleRoleNamesAsync(string scopeType);

    Task<WorkspaceLetterhead> GetLetterheadAsync(Guid workspaceId, Guid callerUserId);
    Task<WorkspaceLetterhead> UpdateLetterheadAsync(Guid workspaceId, Guid callerUserId, LetterheadRequest request);
    Task<WorkspaceLetterhead> UploadLetterheadLogoAsync(Guid workspaceId, Guid callerUserId, IFormFile file);
    Task<WorkspaceLetterhead> DeleteLetterheadLogoAsync(Guid workspaceId, Guid callerUserId);
    Task<(Stream Content, string Path)> GetLetterheadLogoFileAsync(Guid workspaceId, Guid callerUserId);
}

public class WorkspaceService : IWorkspaceService
{
    private readonly ApplicationDbContext _context;
    private readonly ICasbinService _casbinService;
    private readonly IUserAccessGrantService _grantService;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<WorkspaceService> _logger;

    private static readonly HashSet<string> AllowedLogoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };

    public WorkspaceService(ApplicationDbContext context, ICasbinService casbinService, IUserAccessGrantService grantService, IFileStorageService fileStorage, ILogger<WorkspaceService> logger)
    {
        _context = context;
        _casbinService = casbinService;
        _grantService = grantService;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<WorkspaceWithAccess> CreateWorkspaceAsync(Guid userId, WorkspaceRequest request)
    {
        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            OwnerId = userId,
            SubscriptionTier = request.Tier,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Workspaces.AddAsync(workspace);

        // Assign creator as Admin
        var adminRole = await _context.Roles
            .FirstOrDefaultAsync(r => r.Name == Constants.SystemRoles.Admin && r.IsSystem);

        if (adminRole == null)
        {
            throw new InvalidOperationException("Admin role not found");
        }

        var userAccess = new UserAccess
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = adminRole.Id,
            ScopeType = Constants.ScopeTypes.Workspace,
            ScopeId = workspace.Id,
            AssignedAt = DateTime.UtcNow,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _context.UserAccesses.AddAsync(userAccess);

        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Tier = request.Tier,
            Status = "Active",
            StartDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Subscriptions.AddAsync(subscription);
        await _context.SaveChangesAsync();

        await _casbinService.AddRoleForUserAsync(userId.ToString(), adminRole.Name, workspace.Id.ToString());

        _logger.LogInformation("Workspace created: {WorkspaceId} by {UserId}", workspace.Id, userId);
        return new WorkspaceWithAccess(workspace, workspace.SubscriptionTier, new List<string> { adminRole.Name });
    }

    public async Task<List<WorkspaceWithAccess>> GetUserWorkspacesAsync(Guid userId)
    {
        var accesses = await _context.UserAccesses
            .Where(ua => ua.UserId == userId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace)
            .Include(ua => ua.Role)
            .ToListAsync();

        var rolesByWorkspace = accesses.GroupBy(a => a.ScopeId).ToDictionary(g => g.Key, g => g.Select(a => a.Role.Name).ToList());
        var workspaceIds = rolesByWorkspace.Keys.ToList();

        var workspaces = await _context.Workspaces
            .Where(w => workspaceIds.Contains(w.Id) && w.IsActive)
            .ToListAsync();

        var result = new List<WorkspaceWithAccess>();
        foreach (var w in workspaces)
        {
            var allowed = await _casbinService.EnforceAsync(userId.ToString(), "workspace", "view", w.Id.ToString());
            if (!allowed)
                continue;

            result.Add(new WorkspaceWithAccess(w, w.SubscriptionTier, rolesByWorkspace[w.Id]));
        }

        return result;
    }

    public async Task<WorkspaceWithAccess?> GetWorkspaceByIdAsync(Guid workspaceId, Guid userId)
    {
        var roles = await _context.UserAccesses
            .Where(ua => ua.UserId == userId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId)
            .Select(ua => ua.Role.Name)
            .ToListAsync();

        if (roles.Count == 0)
            return null;

        var allowed = await _casbinService.EnforceAsync(userId.ToString(), "workspace", "view", workspaceId.ToString());
        if (!allowed)
            return null;

        var workspace = await _context.Workspaces
            .FirstOrDefaultAsync(w => w.Id == workspaceId && w.IsActive);

        if (workspace == null)
            return null;

        return new WorkspaceWithAccess(workspace, workspace.SubscriptionTier, roles);
    }

    public async Task<List<MemberInfo>> GetMembersAsync(Guid workspaceId, Guid callerUserId)
    {
        // Viewing the roster is available to any member (needed for self-leave — a
        // non-Admin has to be able to see the list to find their own row). Only the
        // mutating actions (invite/role-change/remove-others) require manage_members.
        var allowed = await _casbinService.EnforceAsync(callerUserId.ToString(), "workspace", "view", workspaceId.ToString());
        if (!allowed)
            throw new ForbiddenException("You do not have access to this workspace.");

        var workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId && w.IsActive)
            ?? throw new NotFoundException("Workspace not found");

        var accesses = await _context.UserAccesses
            .Where(ua => ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId)
            .Include(ua => ua.User).ThenInclude(a => a.Person)
            .Include(ua => ua.Role)
            .ToListAsync();

        // Clients are guest-like: they can see their own membership row but not the
        // rest of the roster. Every other role keeps the full-roster view.
        var callerRoles = accesses.Where(ua => ua.UserId == callerUserId).Select(ua => ua.Role.Name).ToList();
        var isGuestView = callerRoles.Count > 0 && callerRoles.All(r => r == Constants.SystemRoles.Client);
        if (isGuestView)
            accesses = accesses.Where(ua => ua.UserId == callerUserId).ToList();

        // Which scope types each distinct role has blanket ("view_all") access to - e.g.
        // Admin holds job.view_all, so they implicitly see every job without an
        // explicit per-job UserAccess row. Computed from whatever view_all permissions
        // are actually seeded, not hardcoded to "Job" - a future organization.view_all
        // grant falls out of this the same way with zero code change here.
        var roleIds = accesses.Select(a => a.RoleId).Distinct().ToList();
        var roleNames = await _context.Roles.Where(r => roleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name);
        var viewAllByRole = await _context.RolePermissions
            .Include(rp => rp.Permission)
            .Where(rp => roleIds.Contains(rp.RoleId) && rp.Permission.Action == "view_all")
            .Select(rp => new { rp.RoleId, rp.Permission.Resource })
            .ToListAsync();
        // For each (role, resource) with blanket access, also surface every other action that
        // role holds on the same resource - "Admin (view, edit, manage_participants)" tells the
        // reader what the blanket access actually lets them do, not just that it exists.
        var resourcesWithFullAccess = viewAllByRole.Select(x => x.Resource).Distinct().ToList();
        var actionsByRoleResource = await _context.RolePermissions
            .Include(rp => rp.Permission)
            .Where(rp => roleIds.Contains(rp.RoleId) && resourcesWithFullAccess.Contains(rp.Permission.Resource) && rp.Permission.Action != "view_all")
            .Select(rp => new { rp.RoleId, rp.Permission.Resource, rp.Permission.Action })
            .ToListAsync();
        var fullAccessByRole = viewAllByRole
            .GroupBy(x => x.RoleId)
            .ToDictionary(g => g.Key, g => g.Select(x => new MemberFullAccessGrant(
                Capitalize(x.Resource),
                roleNames.GetValueOrDefault(g.Key, "Unknown"),
                actionsByRoleResource.Where(a => a.RoleId == g.Key && a.Resource == x.Resource).Select(a => a.Action).OrderBy(a => a).ToList()
            )).ToList());

        // Job-scope grants under THIS workspace's jobs only - scoped by job, not by who's
        // already a workspace member, so a job-only participant (no Workspace-scope row at
        // all) is captured here too, not just "extra" scopes for existing members. Filtering
        // by job ownership (not just user id) also stops a person's job grant in a *different*
        // workspace from leaking into this list.
        var workspaceJobIds = await _context.Jobs
            .Where(j => j.WorkspaceId == workspaceId)
            .Select(j => j.Id)
            .ToListAsync();
        var jobScopes = await _context.UserAccesses
            .Include(ua => ua.Role)
            .Include(ua => ua.User).ThenInclude(a => a.Person)
            .Where(ua => ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Job && workspaceJobIds.Contains(ua.ScopeId))
            .ToListAsync();

        var jobIds = jobScopes.Select(s => s.ScopeId).Distinct().ToList();
        var jobLabels = await _context.Jobs
            .Where(j => jobIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, j => $"{j.JobNumber} · {j.Title}");

        var jobScopesByUser = jobScopes
            .GroupBy(s => s.UserId)
            .ToDictionary(g => g.Key, g => g
                .Select(s => new MemberScopeGrant(s.ScopeType, s.ScopeId, jobLabels.GetValueOrDefault(s.ScopeId, "Unknown job"), s.Role.Name))
                .ToList());

        // view_all can in principle be granted through a job-scope role too, not just a
        // workspace-scope one - fold both role sets in so this stays correct either way.
        var jobRoleIds = jobScopes.Select(s => s.RoleId).Distinct().Except(roleIds).ToList();
        if (jobRoleIds.Count > 0)
        {
            var jobRoleNames = await _context.Roles.Where(r => jobRoleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name);
            var extraViewAll = await _context.RolePermissions
                .Include(rp => rp.Permission)
                .Where(rp => jobRoleIds.Contains(rp.RoleId) && rp.Permission.Action == "view_all")
                .Select(rp => new { rp.RoleId, rp.Permission.Resource })
                .ToListAsync();
            var extraResources = extraViewAll.Select(x => x.Resource).Distinct().ToList();
            var extraActions = await _context.RolePermissions
                .Include(rp => rp.Permission)
                .Where(rp => jobRoleIds.Contains(rp.RoleId) && extraResources.Contains(rp.Permission.Resource) && rp.Permission.Action != "view_all")
                .Select(rp => new { rp.RoleId, rp.Permission.Resource, rp.Permission.Action })
                .ToListAsync();
            foreach (var group in extraViewAll.GroupBy(x => x.RoleId))
                fullAccessByRole[group.Key] = group.Select(x => new MemberFullAccessGrant(
                    Capitalize(x.Resource),
                    jobRoleNames.GetValueOrDefault(group.Key, "Unknown"),
                    extraActions.Where(a => a.RoleId == group.Key && a.Resource == x.Resource).Select(a => a.Action).OrderBy(a => a).ToList()
                )).ToList();
        }

        var workspaceMembers = accesses
            .GroupBy(ua => ua.UserId)
            .Select(g =>
            {
                var first = g.OrderBy(ua => ua.AssignedAt).First();
                return new MemberInfo(
                    first.UserId, first.User.Person.Email!, first.User.Person.FirstName, first.User.Person.LastName,
                    g.Select(ua => ua.Role.Name).ToList(), first.AssignedAt, first.UserId == workspace.OwnerId,
                    g.SelectMany(ua => fullAccessByRole.GetValueOrDefault(ua.RoleId, new List<MemberFullAccessGrant>())).ToList(),
                    jobScopesByUser.GetValueOrDefault(first.UserId, new List<MemberScopeGrant>()));
            })
            .ToDictionary(m => m.UserId);

        // Job-only people: hold a job-scope grant under this workspace but no Workspace-scope
        // row at all, so they're absent from `accesses` entirely - add them as their own rows.
        // Skipped entirely for a guest (Client-only) caller - same roster restriction as above.
        var jobOnlyMembers = isGuestView
            ? Enumerable.Empty<MemberInfo>()
            : jobScopes
            .Where(s => !workspaceMembers.ContainsKey(s.UserId))
            .GroupBy(s => s.UserId)
            .Select(g =>
            {
                var first = g.OrderBy(s => s.AssignedAt).First();
                return new MemberInfo(
                    first.UserId, first.User.Person.Email!, first.User.Person.FirstName, first.User.Person.LastName,
                    new List<string>(), first.AssignedAt, false,
                    g.SelectMany(s => fullAccessByRole.GetValueOrDefault(s.RoleId, new List<MemberFullAccessGrant>())).ToList(),
                    jobScopesByUser.GetValueOrDefault(first.UserId, new List<MemberScopeGrant>()));
            });

        return workspaceMembers.Values.Concat(jobOnlyMembers).ToList();
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public Task<List<string>> GetEligibleRoleNamesAsync(string scopeType) =>
        _context.RoleScopes
            .Where(rs => rs.ScopeType == scopeType)
            .Select(rs => rs.Role.Name)
            .OrderBy(n => n)
            .ToListAsync();

    public async Task<List<RoleWithPermissions>> GetWorkspaceRolesAsync(Guid workspaceId, Guid callerUserId)
    {
        // Read-only, but mirrors the "manage members" gate so it lines up with the
        // Admin-only Roles menu in the UI - non-admins can't reach it via direct API call either.
        await EnsureManageMembersAsync(workspaceId, callerUserId);

        var roles = await _context.Roles
            .Where(r => r.IsSystem)
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .OrderBy(r => r.Name)
            .ToListAsync();

        return roles
            .Select(r => new RoleWithPermissions(
                r.Id,
                r.Name,
                r.Description,
                r.RolePermissions
                    .Select(rp => new PermissionInfo(rp.Permission.Name, rp.Permission.Resource, rp.Permission.Action, rp.Permission.Description))
                    .OrderBy(p => p.Name)
                    .ToList()))
            .ToList();
    }

    public async Task AddMemberRoleAsync(Guid workspaceId, Guid targetUserId, Guid callerUserId, string roleName)
    {
        await EnsureManageMembersAsync(workspaceId, callerUserId);

        var workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId && w.IsActive)
            ?? throw new NotFoundException("Workspace not found");

        var isMember = await _context.UserAccesses
            .AnyAsync(ua => ua.UserId == targetUserId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId);
        if (!isMember)
            throw new NotFoundException("Member not found");

        // Scope-checked against RoleScopes, the single source of truth for which roles are
        // valid at which scope - not just "does a role with this name exist" (mirrors
        // JobService.ResolveJobRoleAsync). A role that exists but isn't scoped to Workspace
        // (e.g. Client, which is Job-only) is rejected here even if it slipped past the DTO.
        var role = await _context.Roles
            .Where(r => r.Name == roleName && r.IsSystem)
            .Where(r => r.RoleScopes.Any(rs => rs.ScopeType == Constants.ScopeTypes.Workspace))
            .FirstOrDefaultAsync()
            ?? throw new AppException(Constants.ErrorCodes.ValidationFailed, $"'{roleName}' is not a valid workspace role.", 400);

        var access = await _grantService.GrantAsync(targetUserId, role.Id, Constants.ScopeTypes.Workspace, workspaceId, callerUserId);
        AddAudit("MemberRoleAdded", "UserAccess", access.Id, workspaceId, callerUserId, null, role.Name);
        await _context.SaveChangesAsync();
    }

    public async Task RemoveMemberRoleAsync(Guid workspaceId, Guid targetUserId, Guid callerUserId, string roleName)
    {
        await EnsureManageMembersAsync(workspaceId, callerUserId);

        var workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId && w.IsActive)
            ?? throw new NotFoundException("Workspace not found");

        if (targetUserId == workspace.OwnerId)
            throw new AppException(Constants.ErrorCodes.CannotModifyOwner, "The workspace owner's role cannot be changed.", 409);

        var roles = await _context.UserAccesses
            .Where(ua => ua.UserId == targetUserId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId)
            .Include(ua => ua.Role)
            .ToListAsync();

        var access = roles.FirstOrDefault(ua => ua.Role.Name == roleName)
            ?? throw new NotFoundException("Member does not hold that role.");

        if (roles.Count == 1)
            throw new AppException(Constants.ErrorCodes.ValidationFailed,
                "Cannot remove a member's last role - remove the member instead.", 409);

        // Serializable so a concurrent role-change/removal against the same workspace can't
        // also pass the "at least one other Admin exists" read before either commits its write.
        // Must run through the DB's execution strategy (EnableRetryOnFailure) rather than a
        // bare BeginTransactionAsync - the retrying strategy refuses user-managed transactions
        // it doesn't control the retry boundary for.
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            if (roleName == Constants.SystemRoles.Admin)
                await EnsureNotLastAdminAsync(workspaceId, targetUserId);

            access.IsActive = false;
            AddAudit("MemberRoleRemoved", "UserAccess", access.Id, workspaceId, callerUserId, roleName, null);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        await _casbinService.RemoveRoleForUserAsync(targetUserId.ToString(), roleName, workspaceId.ToString());
    }

    public async Task RemoveMemberAsync(Guid workspaceId, Guid targetUserId, Guid callerUserId)
    {
        var isSelf = targetUserId == callerUserId;
        if (!isSelf)
            await EnsureManageMembersAsync(workspaceId, callerUserId);

        var workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId && w.IsActive)
            ?? throw new NotFoundException("Workspace not found");

        if (targetUserId == workspace.OwnerId)
            throw new AppException(Constants.ErrorCodes.CannotModifyOwner, "The workspace owner cannot be removed.", 409);

        var accesses = await _context.UserAccesses
            .Where(ua => ua.UserId == targetUserId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId)
            .Include(ua => ua.Role)
            .ToListAsync();
        if (accesses.Count == 0)
            throw new NotFoundException("Member not found");

        var roleNames = accesses.Select(a => a.Role.Name).ToList();
        var isAdmin = roleNames.Contains(Constants.SystemRoles.Admin);

        // Job-scope grants for this workspace's jobs don't disappear on their own -
        // UserAccess for Workspace and Job scope are separate rows. Leaving them active
        // would let a removed member still show up as having access to jobs they were
        // assigned to, despite no longer being a workspace member at all.
        var workspaceJobIds = await _context.Jobs
            .Where(j => j.WorkspaceId == workspaceId)
            .Select(j => j.Id)
            .ToListAsync();
        var jobGrants = await _context.UserAccesses
            .Include(ua => ua.Role)
            .Where(ua => ua.UserId == targetUserId && ua.IsActive &&
                ua.ScopeType == Constants.ScopeTypes.Job && workspaceJobIds.Contains(ua.ScopeId))
            .ToListAsync();

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            if (isAdmin)
                await EnsureNotLastAdminAsync(workspaceId, targetUserId);

            foreach (var access in accesses)
            {
                access.IsActive = false;
                AddAudit("MemberRemoved", "UserAccess", access.Id, workspaceId, callerUserId, access.Role.Name, null);
            }

            foreach (var jobGrant in jobGrants)
                jobGrant.IsActive = false;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        foreach (var roleName in roleNames)
            await _casbinService.RemoveRoleForUserAsync(targetUserId.ToString(), roleName, workspaceId.ToString());
        foreach (var jobGrant in jobGrants)
            await _casbinService.RemoveRoleForUserAsync(targetUserId.ToString(), jobGrant.Role.Name, jobGrant.ScopeId.ToString());
    }

    private async Task EnsureManageMembersAsync(Guid workspaceId, Guid callerUserId)
    {
        var allowed = await _casbinService.EnforceAsync(callerUserId.ToString(), "workspace", "manage_members", workspaceId.ToString());
        if (!allowed)
            throw new ForbiddenException("You do not have permission to manage members of this workspace.");
    }

    private async Task<Workspace> FindWorkspaceForViewAsync(Guid workspaceId, Guid callerUserId)
    {
        var allowed = await _casbinService.EnforceAsync(callerUserId.ToString(), "workspace", "view", workspaceId.ToString());
        if (!allowed)
            throw new ForbiddenException("You do not have access to this workspace.");
        return await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId && w.IsActive)
            ?? throw new NotFoundException("Workspace not found");
    }

    private async Task<Workspace> FindWorkspaceForEditAsync(Guid workspaceId, Guid callerUserId)
    {
        var allowed = await _casbinService.EnforceAsync(callerUserId.ToString(), "workspace", "edit", workspaceId.ToString());
        if (!allowed)
            throw new ForbiddenException("You do not have permission to edit this workspace's settings.");
        return await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId && w.IsActive)
            ?? throw new NotFoundException("Workspace not found");
    }

    private static WorkspaceLetterhead ToLetterhead(Workspace w) => new(
        w.LetterheadCompanyName, w.LetterheadAddress, w.LetterheadPhone, w.LetterheadEmail,
        w.LetterheadRegistrationNumber, w.LetterheadLogoPath != null);

    public async Task<WorkspaceLetterhead> GetLetterheadAsync(Guid workspaceId, Guid callerUserId)
    {
        var workspace = await FindWorkspaceForViewAsync(workspaceId, callerUserId);
        return ToLetterhead(workspace);
    }

    public async Task<WorkspaceLetterhead> UpdateLetterheadAsync(Guid workspaceId, Guid callerUserId, LetterheadRequest request)
    {
        var workspace = await FindWorkspaceForEditAsync(workspaceId, callerUserId);

        workspace.LetterheadCompanyName = request.CompanyName?.Trim();
        workspace.LetterheadAddress = request.Address?.Trim();
        workspace.LetterheadPhone = request.Phone?.Trim();
        workspace.LetterheadEmail = request.Email?.Trim();
        workspace.LetterheadRegistrationNumber = request.RegistrationNumber?.Trim();
        workspace.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return ToLetterhead(workspace);
    }

    public async Task<WorkspaceLetterhead> UploadLetterheadLogoAsync(Guid workspaceId, Guid callerUserId, IFormFile file)
    {
        var workspace = await FindWorkspaceForEditAsync(workspaceId, callerUserId);

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedLogoExtensions.Contains(extension))
            throw new ValidationException($"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", AllowedLogoExtensions)}.");
        if (file.Length > DocumentService.MaxFileSizeBytes)
            throw new ValidationException($"File exceeds the {DocumentService.MaxFileSizeBytes / (1024 * 1024)}MB size limit.");

        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var relativePath = $"{workspaceId}/letterhead/{storedFileName}";

        await using (var stream = file.OpenReadStream())
        {
            await _fileStorage.SaveAsync(stream, relativePath, CancellationToken.None);
        }

        var previousPath = workspace.LetterheadLogoPath;
        workspace.LetterheadLogoPath = relativePath;
        workspace.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        if (previousPath != null)
            await _fileStorage.DeleteAsync(previousPath, CancellationToken.None);

        return ToLetterhead(workspace);
    }

    public async Task<WorkspaceLetterhead> DeleteLetterheadLogoAsync(Guid workspaceId, Guid callerUserId)
    {
        var workspace = await FindWorkspaceForEditAsync(workspaceId, callerUserId);

        if (workspace.LetterheadLogoPath != null)
        {
            await _fileStorage.DeleteAsync(workspace.LetterheadLogoPath, CancellationToken.None);
            workspace.LetterheadLogoPath = null;
            workspace.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return ToLetterhead(workspace);
    }

    public async Task<(Stream Content, string Path)> GetLetterheadLogoFileAsync(Guid workspaceId, Guid callerUserId)
    {
        var workspace = await FindWorkspaceForViewAsync(workspaceId, callerUserId);
        if (workspace.LetterheadLogoPath == null)
            throw new NotFoundException("No logo uploaded.");

        var content = await _fileStorage.OpenAsync(workspace.LetterheadLogoPath, CancellationToken.None);
        return (content, workspace.LetterheadLogoPath);
    }

    private void AddAudit(string action, string resourceType, Guid resourceId, Guid? workspaceId, Guid userId, string? oldValue, string? newValue)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = userId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ScopeType = Constants.ScopeTypes.Workspace,
            ScopeId = workspaceId,
            OldValue = oldValue,
            NewValue = newValue,
            CreatedAt = DateTime.UtcNow
        });
    }

    private async Task EnsureNotLastAdminAsync(Guid workspaceId, Guid excludingUserId)
    {
        var otherActiveAdmins = await _context.UserAccesses
            .Where(ua => ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId
                && ua.UserId != excludingUserId)
            .Include(ua => ua.Role)
            .AnyAsync(ua => ua.Role.Name == Constants.SystemRoles.Admin);

        if (!otherActiveAdmins)
            throw new AppException(Constants.ErrorCodes.LastAdminRequired, "The workspace must have at least one Admin.", 409);
    }
}
