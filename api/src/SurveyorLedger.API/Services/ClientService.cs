using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Client;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IClientService
{
    Task<User> CreateAsync(Guid workspaceId, Guid callerUserId, ClientRequest request);

    /// <summary>
    /// Clients aren't workspace members (no UserAccess row - see JobService's access
    /// model comment), so "clients in this workspace" has no direct column to filter
    /// on. Scoped instead via JobParticipant: a client is searchable here once they've
    /// been attached to at least one job in this workspace.
    /// </summary>
    Task<List<User>> SearchAsync(Guid workspaceId, Guid callerUserId, string? query);
}

public class ClientService : IClientService
{
    private readonly ApplicationDbContext _context;
    private readonly ICasbinService _casbinService;
    private readonly ILogger<ClientService> _logger;

    public ClientService(ApplicationDbContext context, ICasbinService casbinService, ILogger<ClientService> logger)
    {
        _context = context;
        _casbinService = casbinService;
        _logger = logger;
    }

    public async Task<User> CreateAsync(Guid workspaceId, Guid callerUserId, ClientRequest request)
    {
        await EnsureAllowedAsync(callerUserId, "create", workspaceId);

        var user = new User
        {
            Id = Guid.NewGuid(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Phone = request.Phone?.Trim(),
            Email = null,
            PasswordHash = null,
            EmailVerified = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Client {UserId} created in workspace {WorkspaceId} by {CallerId}", user.Id, workspaceId, callerUserId);
        return user;
    }

    public async Task<List<User>> SearchAsync(Guid workspaceId, Guid callerUserId, string? query)
    {
        await EnsureAllowedAsync(callerUserId, "view", workspaceId);

        var candidateIds = _context.JobParticipants
            .Where(p => p.ParticipantType == "Client" && p.Job.WorkspaceId == workspaceId)
            .Select(p => p.UserId)
            .Distinct();

        var result = _context.Users.Where(u => candidateIds.Contains(u.Id));

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            result = result.Where(u =>
                EF.Functions.Like(u.FirstName, $"%{term}%") ||
                EF.Functions.Like(u.LastName, $"%{term}%") ||
                (u.Phone != null && EF.Functions.Like(u.Phone, $"%{term}%")));
        }

        return await result.OrderBy(u => u.FirstName).ToListAsync();
    }

    private async Task EnsureAllowedAsync(Guid callerUserId, string action, Guid workspaceId)
    {
        var allowed = await _casbinService.EnforceAsync(callerUserId.ToString(), "client", action, workspaceId.ToString());
        if (!allowed)
            throw new ForbiddenException($"You do not have permission to {action} clients in this workspace.");
    }
}
