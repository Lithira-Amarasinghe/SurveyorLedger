using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Organization;
using SurveyorLedger.API.Models.Workspace;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
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
        services.AddScoped<IOrganizationService, OrganizationService>();
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

    [Fact]
    public async Task CreateWorkspaceAsync_beyond_tier_limit_throws_WorkspaceLimitReached()
    {
        var orgService = GetService<IOrganizationService>();
        var workspaceService = GetService<IWorkspaceService>();
        var owner = await CreateUserAccountAsync("Cap", "Owner", "cap-owner@test.local");

        var org = await orgService.CreateOrganizationAsync(owner, new OrganizationRequest { Name = "Cap Org" });
        // Free tier caps at 1 workspace (Constants.OrganizationTiers.MaxWorkspaces[Free] == 1).
        await workspaceService.CreateWorkspaceAsync(owner, org.Id, new WorkspaceRequest { Name = "First", OrganizationId = org.Id });

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            workspaceService.CreateWorkspaceAsync(owner, org.Id, new WorkspaceRequest { Name = "Second", OrganizationId = org.Id }));

        Assert.Equal(Constants.ErrorCodes.WorkspaceLimitReached, ex.Code);
    }

    [Fact]
    public async Task GetUserWorkspacesAsync_IncludesOrganizationId()
    {
        var svc = GetService<IWorkspaceService>();
        var workspaces = await svc.GetUserWorkspacesAsync(AdminId);

        var workspace = workspaces.Single(w => w.Workspace.Id == WorkspaceId);
        Assert.NotEqual(Guid.Empty, workspace.Workspace.OrganizationId);
    }
}
