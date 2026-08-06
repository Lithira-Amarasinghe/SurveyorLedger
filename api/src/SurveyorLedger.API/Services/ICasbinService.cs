namespace SurveyorLedger.API.Services;

public interface ICasbinService
{
    Task InitializeAsync();
    Task<bool> EnforceAsync(string subject, string resource, string action, string scope);
    Task AddRoleForUserAsync(string userId, string role, string scope);
    Task RemoveRoleForUserAsync(string userId, string role, string scope);
}
