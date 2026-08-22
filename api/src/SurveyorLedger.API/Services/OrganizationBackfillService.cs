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
    private static readonly string[] TierRank = { Constants.OrganizationTiers.Business, Constants.OrganizationTiers.Pro, Constants.OrganizationTiers.Free };

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
        var orphanOwnerIds = await _context.Workspaces
            .Where(w => w.OrganizationId == null)
            .Select(w => w.OwnerId)
            .Distinct()
            .ToListAsync();

        if (orphanOwnerIds.Count == 0)
            return;

        foreach (var ownerId in orphanOwnerIds)
        {
            var ownerWorkspaces = await _context.Workspaces
                .Where(w => w.OwnerId == ownerId && w.OrganizationId == null)
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

            var workspaceIds = ownerWorkspaces.Select(w => w.Id).ToList();
            var oldTiers = await _context.Subscriptions
                .Where(s => workspaceIds.Contains(s.WorkspaceId))
                .Select(s => s.Tier)
                .ToListAsync();
            var resolvedTier = TierRank.FirstOrDefault(t => oldTiers.Contains(t)) ?? Constants.OrganizationTiers.Free;

            await _context.OrganizationSubscriptions.AddAsync(new OrganizationSubscription
            {
                Id = Guid.NewGuid(),
                OrganizationId = org.Id,
                Tier = resolvedTier,
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

            _logger.LogInformation("Backfilled organization {OrganizationId} for owner {OwnerId} ({WorkspaceCount} workspaces, tier {Tier})",
                org.Id, ownerId, ownerWorkspaces.Count, resolvedTier);
        }
    }
}
