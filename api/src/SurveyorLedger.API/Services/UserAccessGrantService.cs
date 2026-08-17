using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

/// <summary>
/// Single place that grants/revokes a scoped UserAccess row and keeps Casbin's in-memory
/// enforcer in sync with it. UserAccess is used for both workspace-scope membership and
/// job-scope assignment (same shape, different ScopeType) - this is the one code path
/// both go through, instead of each caller reimplementing the upsert/reactivate dance.
/// </summary>
public interface IUserAccessGrantService
{
    /// <summary>
    /// Grants (or reactivates) a UserAccess row for (userId, scopeType, scopeId, roleId).
    /// Matched on user+scope+role - a user can hold more than one role at the same scope,
    /// each as its own row. Re-granting the same (user, scope, role) reactivates it instead
    /// of leaving a stale duplicate.
    /// </summary>
    Task<UserAccess> GrantAsync(Guid userId, Guid roleId, string scopeType, Guid scopeId, Guid assignedBy);

    /// <summary>
    /// Soft-revokes UserAccess row(s) for (userId, scopeType, scopeId). When <paramref name="roleId"/>
    /// is given, only that one role is revoked, leaving any other roles the user holds at this
    /// scope active. When omitted, every active role at this scope is revoked (full removal).
    /// </summary>
    Task RevokeAsync(Guid userId, string scopeType, Guid scopeId, Guid? roleId = null);
}

public class UserAccessGrantService : IUserAccessGrantService
{
    private readonly ApplicationDbContext _context;
    private readonly ICasbinService _casbinService;
    private readonly IScopeIdResolver _scopeIdResolver;
    private readonly ILogger<UserAccessGrantService> _logger;

    public UserAccessGrantService(ApplicationDbContext context, ICasbinService casbinService,
        IScopeIdResolver scopeIdResolver, ILogger<UserAccessGrantService> logger)
    {
        _context = context;
        _casbinService = casbinService;
        _scopeIdResolver = scopeIdResolver;
        _logger = logger;
    }

    public async Task<UserAccess> GrantAsync(Guid userId, Guid roleId, string scopeType, Guid scopeId, Guid assignedBy)
    {
        var role = await _context.Roles
            .Include(r => r.Policy)
            .FirstAsync(r => r.Id == roleId);

        // IgnoreQueryFilters: ApplicationDbContext filters UserAccess to IsActive by default -
        // a previously-revoked row for this exact (user, role, scope) must still be found here
        // so it gets reactivated instead of colliding with a fresh duplicate insert.
        var existing = await _context.UserAccesses.IgnoreQueryFilters()
            .Include(ua => ua.User)
            .Include(ua => ua.Role)
            .FirstOrDefaultAsync(ua => ua.UserId == userId && ua.ScopeType == scopeType && ua.ScopeId == scopeId && ua.RoleId == roleId);

        if (existing == null)
        {
            var account = await _context.UserAccounts.FirstAsync(a => a.Id == userId);
            var access = new UserAccess
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                RoleId = roleId,
                ScopeType = scopeType,
                ScopeId = scopeId,
                AssignedAt = DateTime.UtcNow,
                AssignedBy = assignedBy,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _context.UserAccesses.AddAsync(access);
            await _context.SaveChangesAsync();
            await SyncCasbinAsync(() => _casbinService.AddRoleForUserAsync(userId.ToString(), role.Name, scopeId.ToString()));

            access.Role = role;
            access.User = account;

            // Grant ancestor roles via the role's assignment policy
            await GrantAncestorRolesAsync(userId, scopeType, scopeId, role, assignedBy);

            return access;
        }

        var wasInactive = !existing.IsActive;

        existing.IsActive = true;
        existing.AssignedBy = assignedBy;
        existing.AssignedAt = DateTime.UtcNow;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        if (wasInactive)
            await SyncCasbinAsync(() => _casbinService.AddRoleForUserAsync(userId.ToString(), role.Name, scopeId.ToString()));

        // If reactivating, also reactivate any inactive ancestor roles if they exist
        if (wasInactive)
            await GrantAncestorRolesAsync(userId, scopeType, scopeId, role, assignedBy);

        return existing;
    }

    private async Task GrantAncestorRolesAsync(Guid userId, string scopeType, Guid scopeId, Role grantedRole, Guid assignedBy)
    {
        try
        {
            var policy = grantedRole.Policy;
            if (policy == null)
                return;

            var policyDoc = JsonSerializer.Deserialize<JsonElement>(policy.RulesJson);
            if (!policyDoc.TryGetProperty("ancestors", out var ancestorsArray) || ancestorsArray.ValueKind != JsonValueKind.Array)
                return;

            // Walk ancestors in order (nearest ancestor first)
            string? currentScopeType = scopeType;
            Guid currentScopeId = scopeId;

            foreach (var ancestorRule in ancestorsArray.EnumerateArray())
            {
                if (!ancestorRule.TryGetProperty("scopeType", out var ancestorScopeTypeEl) ||
                    !ancestorRule.TryGetProperty("grantRoleId", out var ancestorRoleIdEl))
                    continue;

                var ancestorScopeType = ancestorScopeTypeEl.GetString();
                if (!Guid.TryParse(ancestorRoleIdEl.GetString(), out var ancestorRoleId))
                    continue;

                // Get the parent scope ID
                var parentScopeId = await _scopeIdResolver.GetParentIdAsync(currentScopeType, currentScopeId);
                if (parentScopeId == null)
                {
                    _logger.LogWarning("No parent scope found for {ScopeType}:{ScopeId}. Ancestor chain stops.",
                        currentScopeType, currentScopeId);
                    break;
                }

                // Check if user already has ANY active role at the ancestor scope - if so,
                // they've already earned baseline presence there and this policy shouldn't
                // add or touch anything.
                var hasAnyRoleAtAncestor = await _context.UserAccesses
                    .Where(ua => ua.UserId == userId && ua.ScopeType == ancestorScopeType &&
                                 ua.ScopeId == parentScopeId && ua.IsActive)
                    .AnyAsync();

                if (!hasAnyRoleAtAncestor)
                {
                    // Reactivate a prior chain-granted row if one exists (e.g. revoked, then
                    // re-granted) rather than inserting a duplicate that would collide with
                    // history and leave two rows chasing the same (user, role, scope).
                    // IgnoreQueryFilters: ApplicationDbContext filters UserAccess to IsActive
                    // by default - the one row we need to find here is exactly the inactive one.
                    var existingAncestorAccess = await _context.UserAccesses.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(ua => ua.UserId == userId && ua.RoleId == ancestorRoleId &&
                            ua.ScopeType == ancestorScopeType && ua.ScopeId == parentScopeId);

                    if (existingAncestorAccess != null)
                    {
                        existingAncestorAccess.IsActive = true;
                        existingAncestorAccess.IsChainGranted = true;
                        existingAncestorAccess.AssignedBy = assignedBy;
                        existingAncestorAccess.AssignedAt = DateTime.UtcNow;
                        existingAncestorAccess.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        existingAncestorAccess = new UserAccess
                        {
                            Id = Guid.NewGuid(),
                            UserId = userId,
                            RoleId = ancestorRoleId,
                            ScopeType = ancestorScopeType,
                            ScopeId = parentScopeId.Value,
                            AssignedAt = DateTime.UtcNow,
                            AssignedBy = assignedBy,
                            IsActive = true,
                            IsChainGranted = true,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        await _context.UserAccesses.AddAsync(existingAncestorAccess);
                    }

                    var ancestorRoleEntity = await _context.Roles.FirstOrDefaultAsync(r => r.Id == ancestorRoleId);
                    if (ancestorRoleEntity != null)
                    {
                        await SyncCasbinAsync(() => _casbinService.AddRoleForUserAsync(
                            userId.ToString(), ancestorRoleEntity.Name, parentScopeId.ToString()));
                    }
                }

                // Move up the chain
                currentScopeType = ancestorScopeType;
                currentScopeId = parentScopeId.Value;
            }

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error granting ancestor roles for user {UserId} at {ScopeType}:{ScopeId}",
                userId, scopeType, scopeId);
            throw;
        }
    }

    public async Task RevokeAsync(Guid userId, string scopeType, Guid scopeId, Guid? roleId = null)
    {
        var accesses = await _context.UserAccesses
            .Include(ua => ua.Role).ThenInclude(r => r.Policy)
            .Where(ua => ua.UserId == userId && ua.IsActive && ua.ScopeType == scopeType && ua.ScopeId == scopeId
                && (roleId == null || ua.RoleId == roleId))
            .ToListAsync();

        foreach (var access in accesses)
        {
            access.IsActive = false;
            access.UpdatedAt = DateTime.UtcNow;
            await SyncCasbinAsync(() => _casbinService.RemoveRoleForUserAsync(userId.ToString(), access.Role.Name, scopeId.ToString()));
        }

        await _context.SaveChangesAsync();

        // Cascade UP, not down: revoking a role that chain-granted an ancestor role (e.g.
        // Surveyor at Job auto-granting WorkspaceMember at Workspace) should also drop that
        // ancestor role, but only if nothing else still needs it. Runs per revoked role since
        // each can chain to a different ancestor.
        foreach (var access in accesses)
            await CascadeRevokeAncestorsAsync(userId, scopeType, scopeId, access.Role);
    }

    /// <summary>
    /// Walks the just-revoked role's own policy ancestors upward, undoing exactly what
    /// GrantAncestorRolesAsync would have done for it. Stops the moment it finds an ancestor
    /// row that either isn't chain-granted (the user earned it independently - never touch
    /// it) or is still required by some other active grant elsewhere in the tree.
    /// </summary>
    private async Task CascadeRevokeAncestorsAsync(Guid userId, string revokedScopeType, Guid revokedScopeId, Role revokedRole)
    {
        try
        {
            var policy = revokedRole.Policy;
            if (policy == null)
                return;

            var policyDoc = JsonSerializer.Deserialize<JsonElement>(policy.RulesJson);
            if (!policyDoc.TryGetProperty("ancestors", out var ancestorsArray) || ancestorsArray.ValueKind != JsonValueKind.Array)
                return;

            string currentScopeType = revokedScopeType;
            Guid currentScopeId = revokedScopeId;

            foreach (var ancestorRule in ancestorsArray.EnumerateArray())
            {
                if (!ancestorRule.TryGetProperty("scopeType", out var ancestorScopeTypeEl) ||
                    !ancestorRule.TryGetProperty("grantRoleId", out var ancestorRoleIdEl))
                    continue;

                var ancestorScopeType = ancestorScopeTypeEl.GetString();
                if (ancestorScopeType == null || !Guid.TryParse(ancestorRoleIdEl.GetString(), out var ancestorRoleId))
                    continue;

                var parentScopeId = await _scopeIdResolver.GetParentIdAsync(currentScopeType, currentScopeId);
                if (parentScopeId == null)
                    break;

                var ancestorAccess = await _context.UserAccesses
                    .FirstOrDefaultAsync(ua => ua.UserId == userId && ua.ScopeType == ancestorScopeType &&
                        ua.ScopeId == parentScopeId && ua.RoleId == ancestorRoleId && ua.IsActive);

                // Nothing active there, or the user holds it for a reason other than this
                // chain (direct grant/invite) - never auto-remove something we didn't grant.
                if (ancestorAccess == null || !ancestorAccess.IsChainGranted)
                    break;

                if (await AnyOtherActiveGrantStillChainsToAsync(userId, ancestorScopeType, parentScopeId.Value, ancestorRoleId, currentScopeType, currentScopeId))
                    break;

                // Nothing else needs it - revoke, and let this continue cascading further up
                // (e.g. Workspace -> future Organization) via the recursive RevokeAsync call.
                await RevokeAsync(userId, ancestorScopeType, parentScopeId.Value, ancestorRoleId);

                currentScopeType = ancestorScopeType;
                currentScopeId = parentScopeId.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cascading ancestor revoke for user {UserId} from {ScopeType}:{ScopeId}",
                userId, revokedScopeType, revokedScopeId);
            throw;
        }
    }

    /// <summary>
    /// True if the user holds any other active role, anywhere under <paramref name="ancestorScopeId"/>,
    /// whose policy also chains into (<paramref name="ancestorScopeType"/>, <paramref name="ancestorRoleId"/>) -
    /// i.e. someone else still has a reason for this ancestor access to exist. Child scope
    /// types are discovered from ScopeParentType (data-driven, same as the grant path), so
    /// this needs no change when a new scope level is added.
    /// </summary>
    private async Task<bool> AnyOtherActiveGrantStillChainsToAsync(
        Guid userId, string ancestorScopeType, Guid ancestorScopeId, Guid ancestorRoleId,
        string excludeScopeType, Guid excludeScopeId)
    {
        var childScopeTypes = await _context.ScopeParentTypes
            .Where(spt => spt.ParentScopeType == ancestorScopeType)
            .Select(spt => spt.ScopeType)
            .ToListAsync();

        foreach (var childScopeType in childScopeTypes)
        {
            var childScopeIds = await _scopeIdResolver.GetChildIdsAsync(ancestorScopeType, childScopeType, ancestorScopeId);

            foreach (var childScopeId in childScopeIds)
            {
                if (childScopeType == excludeScopeType && childScopeId == excludeScopeId)
                    continue; // the scope we're revoking from doesn't count as "still needing" it

                var activeRoles = await _context.UserAccesses
                    .Include(ua => ua.Role).ThenInclude(r => r.Policy)
                    .Where(ua => ua.UserId == userId && ua.ScopeType == childScopeType &&
                                 ua.ScopeId == childScopeId && ua.IsActive)
                    .ToListAsync();

                if (activeRoles.Any(ua => RoleChainsTo(ua.Role, ancestorScopeType, ancestorRoleId)))
                    return true;
            }
        }

        return false;
    }

    private static bool RoleChainsTo(Role role, string ancestorScopeType, Guid ancestorRoleId)
    {
        if (role.Policy == null)
            return false;

        JsonElement doc;
        try
        {
            doc = JsonSerializer.Deserialize<JsonElement>(role.Policy.RulesJson);
        }
        catch (JsonException)
        {
            return false;
        }

        if (!doc.TryGetProperty("ancestors", out var ancestors) || ancestors.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var rule in ancestors.EnumerateArray())
        {
            if (rule.TryGetProperty("scopeType", out var scopeTypeEl) &&
                rule.TryGetProperty("grantRoleId", out var roleIdEl) &&
                scopeTypeEl.GetString() == ancestorScopeType &&
                Guid.TryParse(roleIdEl.GetString(), out var roleId) && roleId == ancestorRoleId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The DB row is always committed first - it's the source of truth. If the matching
    /// Casbin write then fails, the enforcer's in-memory state has drifted from what the DB
    /// says. Rather than leave that silently wrong until the next process restart, fall back
    /// to a full reload so the enforcer catches up to the DB immediately.
    /// </summary>
    private async Task SyncCasbinAsync(Func<Task> casbinWrite)
    {
        try
        {
            await casbinWrite();
        }
        catch
        {
            await _casbinService.ReloadAsync();
        }
    }
}
