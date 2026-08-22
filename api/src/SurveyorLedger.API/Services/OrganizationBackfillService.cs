using Microsoft.EntityFrameworkCore;
using SurveyorLedger.Core;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Configurations;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IOrganizationBackfillService
{
    /// <summary>Idempotent: creates one Organization per distinct owner of any workspace still
    /// missing an OrganizationId, links those workspaces to it, migrates their subscription
    /// tier, and grants the owner OrgOwner. Safe to call on every startup - a no-op once every
    /// workspace has an OrganizationId.</summary>
    Task RunAsync();
}

public class OrganizationBackfillService : IOrganizationBackfillService
{
    private readonly ApplicationDbContext _context;
    private readonly IUserAccessGrantService _grantService;
    private readonly ICasbinService _casbinService;
    private readonly ILogger<OrganizationBackfillService> _logger;

    public OrganizationBackfillService(ApplicationDbContext context, IUserAccessGrantService grantService, ICasbinService casbinService, ILogger<OrganizationBackfillService> logger)
    {
        _context = context;
        _grantService = grantService;
        _casbinService = casbinService;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        // Workspace.OrganizationId is required now - CreateWorkspaceAsync always sets a real
        // Organization, so Guid.Empty can only appear on rows this backfill hasn't reached yet.
        // Kept as a no-op-forever-after safety net (same pattern as the Client->Member fixup
        // above), not expected to find anything once every workspace has been backfilled once.
        var orphanOwnerIds = await _context.Workspaces
            .Where(w => w.OrganizationId == Guid.Empty)
            .Select(w => w.OwnerId)
            .Distinct()
            .ToListAsync();

        foreach (var ownerId in orphanOwnerIds)
        {
            var ownerWorkspaces = await _context.Workspaces
                .Where(w => w.OwnerId == ownerId && w.OrganizationId == Guid.Empty)
                .ToListAsync();

            var owner = await _context.UserAccounts.Include(u => u.Person).FirstAsync(u => u.Id == ownerId);

            var org = new Organization
            {
                Id = Guid.NewGuid(),
                Name = $"{owner.Person.FirstName}'s Organization",
                OwnerId = ownerId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _context.Organizations.AddAsync(org);

            foreach (var workspace in ownerWorkspaces)
                workspace.OrganizationId = org.Id;

            await _context.OrganizationSubscriptions.AddAsync(new OrganizationSubscription
            {
                Id = Guid.NewGuid(),
                OrganizationId = org.Id,
                Tier = Constants.OrganizationTiers.Free,
                Status = "Active",
                StartDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await _context.UserAccesses.AddAsync(new UserAccess
            {
                Id = Guid.NewGuid(),
                UserId = ownerId,
                RoleId = RoleConfiguration.OrgOwnerRoleId,
                ScopeType = Constants.ScopeTypes.Organization,
                ScopeId = org.Id,
                AssignedAt = DateTime.UtcNow,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            await _casbinService.AddRoleForUserAsync(ownerId.ToString(), Constants.SystemRoles.OrgOwner, org.Id.ToString());

            _logger.LogInformation("Backfilled organization {OrganizationId} for owner {OwnerId} ({WorkspaceCount} workspaces)",
                org.Id, ownerId, ownerWorkspaces.Count);
        }

        await BackfillMemberOrgAccessAsync();
    }

    /// <summary>
    /// Idempotent: for every active Workspace-scope or Job-scope UserAccess row whose user has
    /// no Organization-scope grant on that workspace's org yet, grants OrgMember there directly.
    /// Covers data from before invites/direct grants started reaching Organization (this
    /// feature's rollout) - safe to call on every startup, a no-op once every member has caught up.
    /// </summary>
    private async Task BackfillMemberOrgAccessAsync()
    {
        var workspaceScopeUserWorkspacePairs = await _context.UserAccesses
            .Where(ua => ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace)
            .Select(ua => new { ua.UserId, WorkspaceId = ua.ScopeId })
            .Distinct()
            .ToListAsync();

        var jobScopeUserJobPairs = await _context.UserAccesses
            .Where(ua => ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Job)
            .Select(ua => new { ua.UserId, ua.ScopeId })
            .Distinct()
            .ToListAsync();
        var jobWorkspaceIds = jobScopeUserJobPairs.Select(p => p.ScopeId).Distinct().ToList();
        var jobToWorkspace = await _context.Jobs
            .Where(j => jobWorkspaceIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, j => j.WorkspaceId);

        var userWorkspacePairs = workspaceScopeUserWorkspacePairs
            .Select(p => (p.UserId, WorkspaceId: p.WorkspaceId))
            .Concat(jobScopeUserJobPairs
                .Where(p => jobToWorkspace.ContainsKey(p.ScopeId))
                .Select(p => (p.UserId, WorkspaceId: jobToWorkspace[p.ScopeId])))
            .Distinct()
            .ToList();

        if (userWorkspacePairs.Count == 0)
            return;

        var workspaceIds = userWorkspacePairs.Select(p => p.WorkspaceId).Distinct().ToList();
        var workspaceToOrg = await _context.Workspaces
            .Where(w => workspaceIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.OrganizationId);

        foreach (var (userId, workspaceId) in userWorkspacePairs)
        {
            if (!workspaceToOrg.TryGetValue(workspaceId, out var organizationId))
                continue;

            var alreadyMember = await _context.UserAccesses.AnyAsync(ua =>
                ua.UserId == userId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Organization && ua.ScopeId == organizationId);
            if (alreadyMember)
                continue;

            await _grantService.GrantAsync(userId, RoleConfiguration.OrgMemberRoleId, Constants.ScopeTypes.Organization, organizationId, userId);
            _logger.LogInformation("Backfilled OrgMember for user {UserId} on organization {OrganizationId}", userId, organizationId);
        }
    }
}
