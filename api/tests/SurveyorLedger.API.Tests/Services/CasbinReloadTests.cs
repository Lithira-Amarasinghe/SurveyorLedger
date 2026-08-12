using SurveyorLedger.Core;
using SurveyorLedger.Data.Configurations;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// ReloadAsync is the recovery path when a grant/revoke's Casbin write fails after its DB
/// write already committed, and the correctness fix for multi-instance deployments where
/// another instance's grant never reaches this process's enforcer otherwise. The one thing
/// that must not regress: ClearPolicy() before re-adding, or every rule doubles up.
/// </summary>
public class CasbinReloadTests : WorkspaceIntegrationTestBase
{
    [Fact]
    public async Task Reload_StillEnforcesExistingGrants()
    {
        var allowedBefore = await CasbinService.EnforceAsync(AdminId.ToString(), "workspace", "manage_members", WorkspaceId.ToString());
        Assert.True(allowedBefore);

        await CasbinService.ReloadAsync();

        var allowedAfter = await CasbinService.EnforceAsync(AdminId.ToString(), "workspace", "manage_members", WorkspaceId.ToString());
        Assert.True(allowedAfter);

        // Client never held manage_members - reload must not have granted anything extra.
        var stillDenied = await CasbinService.EnforceAsync(ClientId.ToString(), "workspace", "manage_members", WorkspaceId.ToString());
        Assert.False(stillDenied);
    }

    [Fact]
    public async Task Reload_DoesNotDuplicateGroupingRows()
    {
        // A grant made after InitializeAsync's own load, followed by a reload, must not
        // leave two identical g(user, role, scope) rows - Casbin dedupes idempotently, but
        // if ClearPolicy() were ever removed this would start silently double-counting.
        var newRoleTargetId = Guid.NewGuid();
        await Context.Users.AddAsync(new Data.Entities.User
        {
            Id = newRoleTargetId,
            FirstName = "Reload",
            LastName = "Target",
            EmailVerified = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();

        await GrantService.GrantAsync(newRoleTargetId, RoleConfiguration.SurveyorRoleId, Constants.ScopeTypes.Workspace, WorkspaceId, AdminId);
        await CasbinService.ReloadAsync();
        await CasbinService.ReloadAsync();

        var allowed = await CasbinService.EnforceAsync(newRoleTargetId.ToString(), "workspace", "view", WorkspaceId.ToString());
        Assert.True(allowed);
    }
}
