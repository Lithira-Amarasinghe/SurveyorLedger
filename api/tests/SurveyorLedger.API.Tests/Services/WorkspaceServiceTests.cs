using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Data.Configurations;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// GetMembersAsync sources member display fields (Email/FirstName/LastName) through the
/// UserAccess.User (UserAccount) -&gt; Person navigation now that those fields live on Person,
/// not UserAccount.
/// </summary>
public class WorkspaceServiceTests : WorkspaceIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IWorkspaceService, WorkspaceService>();
    }

    [Fact]
    public async Task GetMembersAsync_ReturnsNamesSourcedFromPerson()
    {
        var svc = GetService<IWorkspaceService>();
        var members = await svc.GetMembersAsync(WorkspaceId, AdminId);

        var admin = members.Single(m => m.UserId == AdminId);
        Assert.Equal("Admin", admin.FirstName);
        Assert.Equal("admin@test.local", admin.Email);
    }

    [Fact]
    public async Task OrgOwner_role_is_scoped_to_organization_and_holds_expected_permissions()
    {
        var eligibleRoles = await Context.RoleScopes
            .Where(rs => rs.ScopeType == Constants.ScopeTypes.Organization)
            .Select(rs => rs.Role.Name)
            .ToListAsync();

        Assert.Contains(Constants.SystemRoles.OrgOwner, eligibleRoles);
        Assert.Contains(Constants.SystemRoles.OrgMember, eligibleRoles);

        var ownerPermissions = await Context.RolePermissions
            .Where(rp => rp.RoleId == RoleConfiguration.OrgOwnerRoleId)
            .Select(rp => rp.Permission.Name)
            .ToListAsync();

        Assert.Contains("organization.create_workspace", ownerPermissions);
        Assert.Contains("organization.manage_subscription", ownerPermissions);
    }
}
