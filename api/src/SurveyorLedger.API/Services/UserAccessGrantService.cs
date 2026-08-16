using Microsoft.EntityFrameworkCore;
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

    public UserAccessGrantService(ApplicationDbContext context, ICasbinService casbinService)
    {
        _context = context;
        _casbinService = casbinService;
    }

    public async Task<UserAccess> GrantAsync(Guid userId, Guid roleId, string scopeType, Guid scopeId, Guid assignedBy)
    {
        var role = await _context.Roles.FirstAsync(r => r.Id == roleId);

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

        return existing;
    }

    public async Task RevokeAsync(Guid userId, string scopeType, Guid scopeId, Guid? roleId = null)
    {
        var accesses = await _context.UserAccesses
            .Include(ua => ua.Role)
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
