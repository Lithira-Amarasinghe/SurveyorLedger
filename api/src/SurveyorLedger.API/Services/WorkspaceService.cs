using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Workspace;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IWorkspaceService
{
    Task<Workspace> CreateWorkspaceAsync(Guid userId, WorkspaceRequest request);
    Task<List<Workspace>> GetUserWorkspacesAsync(Guid userId);
    Task<Workspace?> GetWorkspaceByIdAsync(Guid workspaceId);
}

public class WorkspaceService : IWorkspaceService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<WorkspaceService> _logger;

    public WorkspaceService(ApplicationDbContext context, ILogger<WorkspaceService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Workspace> CreateWorkspaceAsync(Guid userId, WorkspaceRequest request)
    {
        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            OwnerId = userId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Workspaces.AddAsync(workspace);

        // Assign creator as Admin
        var adminRole = await _context.Roles
            .FirstOrDefaultAsync(r => r.Name == "Admin" && r.IsSystem);

        if (adminRole == null)
        {
            throw new InvalidOperationException("Admin role not found");
        }

        var userAccess = new UserAccess
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = adminRole.Id,
            ScopeType = "Workspace",
            ScopeId = workspace.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _context.UserAccesses.AddAsync(userAccess);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Workspace created: {WorkspaceId} by {UserId}", workspace.Id, userId);
        return workspace;
    }

    public async Task<List<Workspace>> GetUserWorkspacesAsync(Guid userId)
    {
        return await _context.UserAccesses
            .Where(ua => ua.UserId == userId && ua.IsActive && ua.ScopeType == "Workspace")
            .Include(ua => ua.Role)
            .Select(ua => ua.ScopeId)
            .Distinct()
            .Join(
                _context.Workspaces,
                scopeId => scopeId,
                workspace => workspace.Id,
                (_, workspace) => workspace
            )
            .Where(w => w.IsActive)
            .ToListAsync();
    }

    public async Task<Workspace?> GetWorkspaceByIdAsync(Guid workspaceId)
    {
        return await _context.Workspaces
            .FirstOrDefaultAsync(w => w.Id == workspaceId && w.IsActive);
    }
}
