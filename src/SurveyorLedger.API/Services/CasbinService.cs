using Casbin;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;

namespace SurveyorLedger.API.Services;

public class CasbinService : ICasbinService
{
    private Enforcer? _enforcer;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CasbinService> _logger;

    public CasbinService(ApplicationDbContext context, ILogger<CasbinService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            // RBAC model: (subject, object, action, scope)
            // subject = user, object = resource, action = operation, scope = workspace/job/org
            var modelText = @"
[request_definition]
r = sub, obj, act, scp

[policy_definition]
p = sub, obj, act, scp

[role_definition]
g = _, _

[policy_effect]
e = some(where (p.eft == allow))

[matchers]
m = g(r.sub, p.sub) && r.obj == p.obj && r.act == p.act && r.scp == p.scp
";

            var model = DefaultModel.CreateFromText(modelText);
            _enforcer = new Enforcer(model);

            // Load rules from database
            await LoadRulesFromDatabaseAsync();

            _logger.LogInformation("Casbin initialized with {RuleCount} permission rules and {GroupCount} role assignments",
                _enforcer.GetPolicy().Count, _enforcer.GetGroupingPolicy().Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Casbin");
            throw new AppException(Constants.ErrorCodes.AuthorizationSetupFailed, "Failed to setup authorization", 500);
        }
    }

    public async Task<bool> EnforceAsync(string subject, string resource, string action, string scope)
    {
        if (_enforcer == null)
            throw new InvalidOperationException("Casbin not initialized");

        try
        {
            var result = _enforcer.Enforce(subject, resource, action, scope);
            _logger.LogDebug("Enforce({Subject}, {Resource}, {Action}, {Scope}) = {Result}",
                subject, resource, action, scope, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Enforce check failed for subject: {Subject}", subject);
            return false;
        }
    }

    public async Task AddRoleForUserAsync(string userId, string role, string scope)
    {
        if (_enforcer == null)
            throw new InvalidOperationException("Casbin not initialized");

        try
        {
            var result = _enforcer.AddGroupingPolicy(userId, role, scope);
            if (result)
                _logger.LogInformation("Added role {Role} for user {UserId} in scope {Scope}", role, userId, scope);
            else
                _logger.LogWarning("Failed to add role {Role} for user {UserId} in scope {Scope}", role, userId, scope);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding role {Role} for user {UserId}", role, userId);
            throw;
        }
    }

    public async Task RemoveRoleForUserAsync(string userId, string role, string scope)
    {
        if (_enforcer == null)
            throw new InvalidOperationException("Casbin not initialized");

        try
        {
            var result = _enforcer.RemoveGroupingPolicy(userId, role, scope);
            if (result)
                _logger.LogInformation("Removed role {Role} from user {UserId} in scope {Scope}", role, userId, scope);
            else
                _logger.LogWarning("Failed to remove role {Role} from user {UserId} in scope {Scope}", role, userId, scope);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing role {Role} from user {UserId}", role, userId);
            throw;
        }
    }

    private async Task LoadRulesFromDatabaseAsync()
    {
        // Load permissions: role -> (resource, action, scope)
        var permissions = await _context.RolePermissions
            .Include(rp => rp.Role)
            .Include(rp => rp.Permission)
            .ToListAsync();

        foreach (var rp in permissions)
        {
            var role = rp.Role.Name;
            var resource = rp.Permission.Resource;
            var action = rp.Permission.Action;
            var scope = rp.Permission.Scope ?? "*"; // * = all scopes

            _enforcer!.AddPolicy(role, resource, action, scope);
        }

        _logger.LogInformation("Loaded {PermissionCount} permission rules", permissions.Count);

        // Load user roles: user -> (role, scope)
        var userRoles = await _context.UserAccesses
            .Include(ua => ua.Role)
            .Where(ua => ua.IsActive)
            .ToListAsync();

        foreach (var ua in userRoles)
        {
            _enforcer!.AddGroupingPolicy(ua.UserId.ToString(), ua.Role.Name, ua.ScopeId.ToString());
        }

        _logger.LogInformation("Loaded {UserRoleCount} user role assignments", userRoles.Count);
    }
}
