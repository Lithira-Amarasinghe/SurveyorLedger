using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Organization;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Configurations;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public record OrganizationInfo(Guid Id, string Name, string Tier, int WorkspaceCount, int MaxWorkspaces, List<string> CallerRoles);
public record OrganizationMemberInfo(Guid UserId, string Email, string FirstName, string LastName, List<string> Roles, bool IsOwner);

public interface IOrganizationService
{
    Task<OrganizationInfo> CreateOrganizationAsync(Guid userId, OrganizationRequest request);
    Task<List<OrganizationInfo>> GetUserOrganizationsAsync(Guid userId);
    Task<OrganizationInfo?> GetOrganizationAsync(Guid organizationId, Guid callerId);
    Task<List<OrganizationMemberInfo>> GetMembersAsync(Guid organizationId, Guid callerId);
    Task AddMemberAsync(Guid organizationId, Guid targetUserId, Guid callerId);
    Task RemoveMemberAsync(Guid organizationId, Guid targetUserId, Guid callerId);
    Task<OrganizationInfo> UpdateSubscriptionTierAsync(Guid organizationId, Guid callerId, string tier);
}

public class OrganizationService : IOrganizationService
{
    private readonly ApplicationDbContext _context;
    private readonly ICasbinService _casbinService;
    private readonly IUserAccessGrantService _grantService;
    private readonly ILogger<OrganizationService> _logger;

    public OrganizationService(ApplicationDbContext context, ICasbinService casbinService, IUserAccessGrantService grantService, ILogger<OrganizationService> logger)
    {
        _context = context;
        _casbinService = casbinService;
        _grantService = grantService;
        _logger = logger;
    }

    public async Task<OrganizationInfo> CreateOrganizationAsync(Guid userId, OrganizationRequest request)
    {
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            OwnerId = userId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.Organizations.AddAsync(org);

        var subscription = new OrganizationSubscription
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Tier = Constants.OrganizationTiers.Free,
            Status = "Active",
            StartDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.OrganizationSubscriptions.AddAsync(subscription);

        await _grantService.GrantAsync(userId, RoleConfiguration.OrgOwnerRoleId, Constants.ScopeTypes.Organization, org.Id, userId);
        await _context.SaveChangesAsync();
        await _casbinService.AddRoleForUserAsync(userId.ToString(), Constants.SystemRoles.OrgOwner, org.Id.ToString());

        _logger.LogInformation("Organization created: {OrganizationId} by {UserId}", org.Id, userId);
        return new OrganizationInfo(org.Id, org.Name, subscription.Tier, 0, Constants.OrganizationTiers.MaxWorkspaces[subscription.Tier], new List<string> { Constants.SystemRoles.OrgOwner });
    }

    public async Task<List<OrganizationInfo>> GetUserOrganizationsAsync(Guid userId)
    {
        var accesses = await _context.UserAccesses
            .Where(ua => ua.UserId == userId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Organization)
            .Include(ua => ua.Role)
            .ToListAsync();

        var rolesByOrg = accesses.GroupBy(a => a.ScopeId).ToDictionary(g => g.Key, g => g.Select(a => a.Role.Name).ToList());
        var orgIds = rolesByOrg.Keys.ToList();

        var orgs = await _context.Organizations
            .Include(o => o.Subscription)
            .Where(o => orgIds.Contains(o.Id) && o.IsActive)
            .ToListAsync();

        var workspaceCounts = await _context.Workspaces
            .Where(w => w.OrganizationId != null && orgIds.Contains(w.OrganizationId.Value) && w.IsActive)
            .GroupBy(w => w.OrganizationId!.Value)
            .Select(g => new { OrganizationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OrganizationId, x => x.Count);

        return orgs.Select(o => ToInfo(o, workspaceCounts.GetValueOrDefault(o.Id, 0), rolesByOrg[o.Id])).ToList();
    }

    public async Task<OrganizationInfo?> GetOrganizationAsync(Guid organizationId, Guid callerId)
    {
        var allowed = await _casbinService.EnforceAsync(callerId.ToString(), "organization", "view", organizationId.ToString());
        if (!allowed)
            return null;

        var org = await _context.Organizations.Include(o => o.Subscription)
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.IsActive);
        if (org == null)
            return null;

        var workspaceCount = await _context.Workspaces.CountAsync(w => w.OrganizationId == organizationId && w.IsActive);
        var roles = await _context.UserAccesses
            .Where(ua => ua.UserId == callerId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Organization && ua.ScopeId == organizationId)
            .Select(ua => ua.Role.Name)
            .ToListAsync();

        return ToInfo(org, workspaceCount, roles);
    }

    public async Task<List<OrganizationMemberInfo>> GetMembersAsync(Guid organizationId, Guid callerId)
    {
        await EnsureViewAsync(organizationId, callerId);

        var org = await FindOrganizationAsync(organizationId);
        var accesses = await _context.UserAccesses
            .Where(ua => ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Organization && ua.ScopeId == organizationId)
            .Include(ua => ua.User).ThenInclude(u => u.Person)
            .Include(ua => ua.Role)
            .ToListAsync();

        return accesses
            .GroupBy(ua => ua.UserId)
            .Select(g => new OrganizationMemberInfo(
                g.Key, g.First().User.Person.Email!, g.First().User.Person.FirstName, g.First().User.Person.LastName,
                g.Select(ua => ua.Role.Name).ToList(), g.Key == org.OwnerId))
            .ToList();
    }

    public async Task AddMemberAsync(Guid organizationId, Guid targetUserId, Guid callerId)
    {
        await EnsureManageMembersAsync(organizationId, callerId);

        var alreadyMember = await _context.UserAccesses.AnyAsync(ua =>
            ua.UserId == targetUserId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Organization && ua.ScopeId == organizationId);
        if (alreadyMember)
            throw new AppException(Constants.ErrorCodes.AlreadyMember, "User is already a member of this organization.", 409);

        await _grantService.GrantAsync(targetUserId, RoleConfiguration.OrgMemberRoleId, Constants.ScopeTypes.Organization, organizationId, callerId);
        await _context.SaveChangesAsync();
        await _casbinService.AddRoleForUserAsync(targetUserId.ToString(), Constants.SystemRoles.OrgMember, organizationId.ToString());
    }

    public async Task RemoveMemberAsync(Guid organizationId, Guid targetUserId, Guid callerId)
    {
        await EnsureManageMembersAsync(organizationId, callerId);

        var org = await FindOrganizationAsync(organizationId);
        if (targetUserId == org.OwnerId)
            throw new AppException(Constants.ErrorCodes.CannotModifyOwner, "The organization owner cannot be removed.", 409);

        var access = await _context.UserAccesses.FirstOrDefaultAsync(ua =>
            ua.UserId == targetUserId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Organization && ua.ScopeId == organizationId)
            ?? throw new NotFoundException("Member not found");

        access.IsActive = false;
        await _context.SaveChangesAsync();
        await _casbinService.RemoveRoleForUserAsync(targetUserId.ToString(), Constants.SystemRoles.OrgMember, organizationId.ToString());
    }

    public async Task<OrganizationInfo> UpdateSubscriptionTierAsync(Guid organizationId, Guid callerId, string tier)
    {
        var allowed = await _casbinService.EnforceAsync(callerId.ToString(), "organization", "manage_subscription", organizationId.ToString());
        if (!allowed)
            throw new ForbiddenException("You do not have permission to manage this organization's subscription.");

        var subscription = await _context.OrganizationSubscriptions.FirstOrDefaultAsync(s => s.OrganizationId == organizationId)
            ?? throw new NotFoundException("Subscription not found");

        subscription.Tier = tier;
        subscription.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var org = await FindOrganizationAsync(organizationId);
        var workspaceCount = await _context.Workspaces.CountAsync(w => w.OrganizationId == organizationId && w.IsActive);
        return ToInfo(org, workspaceCount, new List<string>());
    }

    private async Task<Organization> FindOrganizationAsync(Guid organizationId) =>
        await _context.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId && o.IsActive)
        ?? throw new NotFoundException("Organization not found");

    private async Task EnsureViewAsync(Guid organizationId, Guid callerId)
    {
        var allowed = await _casbinService.EnforceAsync(callerId.ToString(), "organization", "view", organizationId.ToString());
        if (!allowed)
            throw new ForbiddenException("You do not have access to this organization.");
    }

    private async Task EnsureManageMembersAsync(Guid organizationId, Guid callerId)
    {
        var allowed = await _casbinService.EnforceAsync(callerId.ToString(), "organization", "manage_members", organizationId.ToString());
        if (!allowed)
            throw new ForbiddenException("You do not have permission to manage members of this organization.");
    }

    private static OrganizationInfo ToInfo(Organization org, int workspaceCount, List<string> callerRoles)
    {
        var tier = org.Subscription?.Tier ?? Constants.OrganizationTiers.Free;
        return new OrganizationInfo(org.Id, org.Name, tier, workspaceCount, Constants.OrganizationTiers.MaxWorkspaces[tier], callerRoles);
    }
}
