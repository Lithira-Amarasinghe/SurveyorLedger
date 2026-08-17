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

        var existing = await _context.UserAccesses
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

                // Check if user already has ANY role at the ancestor scope
                var hasAnyRoleAtAncestor = await _context.UserAccesses
                    .Where(ua => ua.UserId == userId && ua.ScopeType == ancestorScopeType &&
                                 ua.ScopeId == parentScopeId && ua.IsActive)
                    .AnyAsync();

                if (!hasAnyRoleAtAncestor)
                {
                    // Grant the ancestor role
                    var ancestorRoleEntity = await _context.Roles
                        .Include(r => r.Policy)
                        .FirstOrDefaultAsync(r => r.Id == ancestorRoleId);

                    if (ancestorRoleEntity != null)
                    {
                        var ancestorAccess = new UserAccess
                        {
                            Id = Guid.NewGuid(),
                            UserId = userId,
                            RoleId = ancestorRoleId,
                            ScopeType = ancestorScopeType,
                            ScopeId = parentScopeId.Value,
                            AssignedAt = DateTime.UtcNow,
                            AssignedBy = assignedBy,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        await _context.UserAccesses.AddAsync(ancestorAccess);
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
            .Include(ua => ua.Role)
            .Where(ua => ua.UserId == userId && ua.IsActive && ua.ScopeType == scopeType && ua.ScopeId == scopeId
                && (roleId == null || ua.RoleId == roleId))
            .ToListAsync();

        bool isFullRemovalAtScope = roleId == null;

        foreach (var access in accesses)
        {
            access.IsActive = false;
            access.UpdatedAt = DateTime.UtcNow;
            await SyncCasbinAsync(() => _casbinService.RemoveRoleForUserAsync(userId.ToString(), access.Role.Name, scopeId.ToString()));
        }

        await _context.SaveChangesAsync();

        // Cascade revoke: if this was a full removal at the scope, revoke all roles at child scopes
        // that were only granted via the chaining mechanism (not directly assigned).
        if (isFullRemovalAtScope)
            await CascadeRevokeChildScopesAsync(userId, scopeType, scopeId);
    }

    private async Task CascadeRevokeChildScopesAsync(Guid userId, string parentScopeType, Guid parentScopeId)
    {
        try
        {
            // Find the child scope type(s) for this parent
            var childScopes = await _context.ScopeParentTypes
                .Where(spt => spt.ParentScopeType == parentScopeType)
                .ToListAsync();

            foreach (var childScopeMapping in childScopes)
            {
                var childScopeType = childScopeMapping.ScopeType;

                // Get all child scope IDs under this parent
                var childScopeIds = await _scopeIdResolver.GetChildIdsAsync(parentScopeType, childScopeType, parentScopeId);

                foreach (var childScopeId in childScopeIds)
                {
                    // Check if user has any DIRECT (non-chained) role grants at this child scope.
                    // A direct grant is one where the role's policy doesn't include this parent in its ancestors,
                    // or where the user was explicitly assigned to this scope.
                    // For now, use heuristic: roles granted via FullChain policy are considered chained.
                    var directRoleIds = new HashSet<Guid>();
                    var chainedRoleIds = new HashSet<Guid>();

                    var activeRolesAtChild = await _context.UserAccesses
                        .Include(ua => ua.Role)
                        .Where(ua => ua.UserId == userId && ua.ScopeType == childScopeType &&
                                     ua.ScopeId == childScopeId && ua.IsActive)
                        .ToListAsync();

                    foreach (var access in activeRolesAtChild)
                    {
                        var policy = access.Role.Policy;
                        if (policy != null && policy.Name == "FullChain")
                            chainedRoleIds.Add(access.RoleId);
                        else
                            directRoleIds.Add(access.RoleId);
                    }

                    // Only revoke chained roles; leave direct assignments alone
                    if (chainedRoleIds.Any())
                    {
                        foreach (var chainedRoleId in chainedRoleIds)
                        {
                            await RevokeAsync(userId, childScopeType, childScopeId, chainedRoleId);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cascading revoke for user {UserId} at {ScopeType}:{ScopeId}",
                userId, parentScopeType, parentScopeId);
            throw;
        }
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
