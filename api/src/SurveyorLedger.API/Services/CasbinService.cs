using Casbin;
using Casbin.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;

namespace SurveyorLedger.API.Services;

public class CasbinService : ICasbinService
{
    private IEnforcer? _enforcer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CasbinService> _logger;

    public CasbinService(IServiceScopeFactory scopeFactory, ILogger<CasbinService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var modelText = @"
[request_definition]
r = sub, obj, act, scp

[policy_definition]
p = sub, obj, act

[role_definition]
g = _, _, _

[policy_effect]
e = some(where (p.eft == allow))

[matchers]
m = g(r.sub, p.sub, r.scp) && r.obj == p.obj && r.act == p.act
";

            var model = DefaultModel.CreateFromText(modelText);
            _enforcer = new Enforcer(model);

            await LoadRulesFromDatabaseAsync();

            var policyCount = _enforcer.GetPolicy().Count();
            var groupCount = _enforcer.GetGroupingPolicy().Count();
            _logger.LogInformation("Casbin initialized with {PolicyCount} policies and {GroupCount} groups", policyCount, groupCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Casbin");
            throw new AppException(Constants.ErrorCodes.AuthorizationSetupFailed, "Failed to setup authorization", 500);
        }
    }

    public Task<bool> EnforceAsync(string subject, string resource, string action, string scope)
    {
        if (_enforcer == null)
            throw new InvalidOperationException("Casbin not initialized");

        try
        {
            var result = _enforcer.Enforce(subject, resource, action, scope);
            _logger.LogDebug("Enforce({Subject}, {Resource}, {Action}, {Scope}) = {Result}", subject, resource, action, scope, result);
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Enforce check failed");
            return Task.FromResult(false);
        }
    }

    public async Task ReloadAsync()
    {
        if (_enforcer == null)
            throw new InvalidOperationException("Casbin not initialized");

        try
        {
            // ClearPolicy wipes both p and g rows; LoadRulesFromDatabaseAsync re-derives
            // everything from UserAccess/RolePermission, the actual source of truth. Without
            // the clear, reloading would duplicate every grouping rule already in memory.
            _enforcer.ClearPolicy();
            await LoadRulesFromDatabaseAsync();
            _logger.LogInformation("Casbin policy reloaded from database");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload Casbin policy");
            throw;
        }
    }

    /// <summary>
    /// Walks the fixed permission set (small and near-static, unlike UserAccess rows) and
    /// asks Casbin yes/no for each - simpler and more robust than relying on the generic
    /// "implicit permissions" API, which assumes a plainer domain shape than our
    /// g(sub, role, scope) matcher uses.
    /// </summary>
    public Task<List<(string Resource, string Action)>> GetPermissionsAsync(string subject, string scope)
    {
        if (_enforcer == null)
            throw new InvalidOperationException("Casbin not initialized");

        var candidates = _enforcer.GetPolicy()
            .Select(row => row.ToList())
            .Select(row => (Resource: row[1], Action: row[2]))
            .Distinct()
            .ToList();

        var granted = new List<(string, string)>();
        foreach (var (resource, action) in candidates)
        {
            if (_enforcer.Enforce(subject, resource, action, scope))
                granted.Add((resource, action));
        }
        return Task.FromResult(granted);
    }

    public Task AddRoleForUserAsync(string userId, string role, string scope)
    {
        if (_enforcer == null)
            throw new InvalidOperationException("Casbin not initialized");

        try
        {
            _enforcer.AddGroupingPolicy(userId, role, scope);
            _logger.LogInformation("Added role {Role} for user {UserId} in scope {Scope}", role, userId, scope);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding role {Role} for user {UserId}", role, userId);
            throw;
        }
    }

    public Task RemoveRoleForUserAsync(string userId, string role, string scope)
    {
        if (_enforcer == null)
            throw new InvalidOperationException("Casbin not initialized");

        try
        {
            _enforcer.RemoveGroupingPolicy(userId, role, scope);
            _logger.LogInformation("Removed role {Role} from user {UserId} in scope {Scope}", role, userId, scope);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing role {Role} from user {UserId}", role, userId);
            throw;
        }
    }

    private async Task LoadRulesFromDatabaseAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Load permissions: role -> (resource, action, scope)
        var permissions = await context.RolePermissions
            .Include(rp => rp.Role)
            .Include(rp => rp.Permission)
            .ToListAsync();

        foreach (var rp in permissions)
        {
            var role = rp.Role.Name;
            var resource = rp.Permission.Resource;
            var action = rp.Permission.Action;

            _enforcer!.AddPolicy(role, resource, action);
        }

        _logger.LogInformation("Loaded {PermissionCount} permission rules", permissions.Count);

        // Load user roles: user -> (role, scope_id)
        var userAccess = await context.UserAccesses
            .Include(ua => ua.Role)
            .ToListAsync();

        foreach (var ua in userAccess)
        {
            _enforcer!.AddGroupingPolicy(ua.UserId.ToString(), ua.Role.Name, ua.ScopeId.ToString());
        }

        _logger.LogInformation("Loaded {UserAccessCount} user role assignments", userAccess.Count);
    }
}
