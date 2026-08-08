using System.Data;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Workspace;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public record WorkspaceWithAccess(Workspace Workspace, string Tier, string Role);

public record WorkspaceMember(Guid UserId, string Email, string FirstName, string LastName, string Role, DateTime AssignedAt, bool IsOwner);

public interface IWorkspaceService
{
    Task<WorkspaceWithAccess> CreateWorkspaceAsync(Guid userId, WorkspaceRequest request);
    Task<List<WorkspaceWithAccess>> GetUserWorkspacesAsync(Guid userId);
    Task<WorkspaceWithAccess?> GetWorkspaceByIdAsync(Guid workspaceId, Guid userId);
    Task<List<WorkspaceMember>> GetMembersAsync(Guid workspaceId, Guid callerUserId);
    Task<string> UpdateMemberRoleAsync(Guid workspaceId, Guid targetUserId, Guid callerUserId, string newRoleName);
    Task RemoveMemberAsync(Guid workspaceId, Guid targetUserId, Guid callerUserId);
}

public class WorkspaceService : IWorkspaceService
{
    private readonly ApplicationDbContext _context;
    private readonly ICasbinService _casbinService;
    private readonly ILogger<WorkspaceService> _logger;

    public WorkspaceService(ApplicationDbContext context, ICasbinService casbinService, ILogger<WorkspaceService> logger)
    {
        _context = context;
        _casbinService = casbinService;
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
        return new WorkspaceWithAccess(workspace, workspace.SubscriptionTier, adminRole.Name);
    }

    public async Task<List<WorkspaceWithAccess>> GetUserWorkspacesAsync(Guid userId)
    {
        var accesses = await _context.UserAccesses
            .Where(ua => ua.UserId == userId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace)
            .Include(ua => ua.Role)
            .ToListAsync();

        var workspaceIds = accesses.Select(a => a.ScopeId).Distinct().ToList();

        var workspaces = await _context.Workspaces
            .Where(w => workspaceIds.Contains(w.Id) && w.IsActive)
            .ToListAsync();

        var result = new List<WorkspaceWithAccess>();
        foreach (var w in workspaces)
        {
            var access = accesses.First(a => a.ScopeId == w.Id);
            var allowed = await _casbinService.EnforceAsync(userId.ToString(), "workspace", "view", w.Id.ToString());
            if (!allowed)
                continue;

            result.Add(new WorkspaceWithAccess(w, w.SubscriptionTier, access.Role.Name));
        }

        return result;
    }

    public async Task<WorkspaceWithAccess?> GetWorkspaceByIdAsync(Guid workspaceId, Guid userId)
    {
        var access = await _context.UserAccesses
            .Where(ua => ua.UserId == userId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId)
            .Include(ua => ua.Role)
            .FirstOrDefaultAsync();

        if (access == null)
            return null;

        var allowed = await _casbinService.EnforceAsync(userId.ToString(), "workspace", "view", workspaceId.ToString());
        if (!allowed)
            return null;

        var workspace = await _context.Workspaces
            .FirstOrDefaultAsync(w => w.Id == workspaceId && w.IsActive);

        if (workspace == null)
            return null;

        return new WorkspaceWithAccess(workspace, workspace.SubscriptionTier, access.Role.Name);
    }

    public async Task<List<WorkspaceMember>> GetMembersAsync(Guid workspaceId, Guid callerUserId)
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
            .Include(ua => ua.User)
            .Include(ua => ua.Role)
            .ToListAsync();

        return accesses
            .Select(ua => new WorkspaceMember(
                ua.UserId, ua.User.Email, ua.User.FirstName, ua.User.LastName,
                ua.Role.Name, ua.AssignedAt, ua.UserId == workspace.OwnerId))
            .ToList();
    }

    public async Task<string> UpdateMemberRoleAsync(Guid workspaceId, Guid targetUserId, Guid callerUserId, string newRoleName)
    {
        await EnsureManageMembersAsync(workspaceId, callerUserId);

        var workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId && w.IsActive)
            ?? throw new NotFoundException("Workspace not found");

        if (targetUserId == workspace.OwnerId)
            throw new AppException(Constants.ErrorCodes.CannotModifyOwner, "The workspace owner's role cannot be changed.", 409);

        var access = await _context.UserAccesses
            .Where(ua => ua.UserId == targetUserId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId)
            .Include(ua => ua.Role)
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException("Member not found");

        var newRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == newRoleName && r.IsSystem)
            ?? throw new InvalidOperationException($"Role '{newRoleName}' not found");

        if (access.Role.Name == newRole.Name)
            return newRole.Name;

        var oldRoleName = access.Role.Name;

        // Serializable so a concurrent role-change/removal against the same workspace can't
        // also pass the "at least one other Admin exists" read before either commits its write.
        // Must run through the DB's execution strategy (EnableRetryOnFailure) rather than a
        // bare BeginTransactionAsync - the retrying strategy refuses user-managed transactions
        // it doesn't control the retry boundary for.
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            if (oldRoleName == Constants.SystemRoles.Admin)
                await EnsureNotLastAdminAsync(workspaceId, targetUserId);

            access.RoleId = newRole.Id;
            AddAudit("MemberRoleChanged", "UserAccess", access.Id, workspaceId, callerUserId, oldRoleName, newRole.Name);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        await _casbinService.RemoveRoleForUserAsync(targetUserId.ToString(), oldRoleName, workspaceId.ToString());
        await _casbinService.AddRoleForUserAsync(targetUserId.ToString(), newRole.Name, workspaceId.ToString());

        return newRole.Name;
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

        var access = await _context.UserAccesses
            .Where(ua => ua.UserId == targetUserId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId)
            .Include(ua => ua.Role)
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException("Member not found");

        var roleName = access.Role.Name;

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            if (roleName == Constants.SystemRoles.Admin)
                await EnsureNotLastAdminAsync(workspaceId, targetUserId);

            access.IsActive = false;
            AddAudit("MemberRemoved", "UserAccess", access.Id, workspaceId, callerUserId, roleName, null);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        await _casbinService.RemoveRoleForUserAsync(targetUserId.ToString(), roleName, workspaceId.ToString());
    }

    private async Task EnsureManageMembersAsync(Guid workspaceId, Guid callerUserId)
    {
        var allowed = await _casbinService.EnforceAsync(callerUserId.ToString(), "workspace", "manage_members", workspaceId.ToString());
        if (!allowed)
            throw new ForbiddenException("You do not have permission to manage members of this workspace.");
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
