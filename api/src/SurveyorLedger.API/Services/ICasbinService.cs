namespace SurveyorLedger.API.Services;

public interface ICasbinService
{
    Task InitializeAsync();
    Task<bool> EnforceAsync(string subject, string resource, string action, string scope);
    Task AddRoleForUserAsync(string userId, string role, string scope);
    Task RemoveRoleForUserAsync(string userId, string role, string scope);

    /// <summary>
    /// Re-derives the in-memory grouping policy from UserAccess (the source of truth).
    /// Used as the recovery path when a grant/revoke's Casbin write fails after its DB
    /// write already committed, and as the correctness fix for multi-instance deployments
    /// where another instance's grant never reaches this process's enforcer otherwise.
    /// </summary>
    Task ReloadAsync();

    /// <summary>Every (resource, action) the user's role grants in this scope - lets the UI ask Casbin directly instead of guessing from a role name.</summary>
    Task<List<(string Resource, string Action)>> GetPermissionsAsync(string subject, string scope);
}
