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
    /// Grants (or reactivates/re-roles) a UserAccess row for (userId, scopeType, scopeId).
    /// Matched on user+scope alone, not role - re-granting with a different role updates
    /// the existing row instead of leaving a stale duplicate, and keeps Casbin consistent.
    /// </summary>
    Task<UserAccess> GrantAsync(Guid userId, Guid roleId, string scopeType, Guid scopeId, Guid assignedBy);

    /// <summary>Soft-revokes every active UserAccess row for (userId, scopeType, scopeId).</summary>
    Task RevokeAsync(Guid userId, string scopeType, Guid scopeId);
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
            .FirstOrDefaultAsync(ua => ua.UserId == userId && ua.ScopeType == scopeType && ua.ScopeId == scopeId);

        if (existing == null)
        {
            var user = await _context.Users.FirstAsync(u => u.Id == userId);
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
            access.User = user;
            return access;
        }

        var roleChanged = existing.RoleId != roleId;
        var wasInactive = !existing.IsActive;

        if (roleChanged && existing.IsActive)
            await SyncCasbinAsync(() => _casbinService.RemoveRoleForUserAsync(userId.ToString(), existing.Role.Name, scopeId.ToString()));

        existing.RoleId = roleId;
        existing.Role = role;
        existing.IsActive = true;
        existing.AssignedBy = assignedBy;
        existing.AssignedAt = DateTime.UtcNow;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        if (roleChanged || wasInactive)
            await SyncCasbinAsync(() => _casbinService.AddRoleForUserAsync(userId.ToString(), role.Name, scopeId.ToString()));

        return existing;
    }

    public async Task RevokeAsync(Guid userId, string scopeType, Guid scopeId)
    {
        var accesses = await _context.UserAccesses
            .Include(ua => ua.Role)
            .Where(ua => ua.UserId == userId && ua.IsActive && ua.ScopeType == scopeType && ua.ScopeId == scopeId)
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
