using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Data.Configurations;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class OrganizationBackfillServiceTests : WorkspaceIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IOrganizationBackfillService, OrganizationBackfillService>();
    }

    [Fact]
    public async Task RunAsync_creates_one_organization_per_owner_and_links_their_workspaces()
    {
        // WorkspaceIntegrationTestBase seeds one Workspace owned by AdminId with OrganizationId == null.
        var backfill = GetService<IOrganizationBackfillService>();

        await backfill.RunAsync();

        var workspace = await Context.Workspaces.SingleAsync(w => w.Id == WorkspaceId);
        Assert.NotNull(workspace.OrganizationId);

        var org = await Context.Organizations.SingleAsync(o => o.Id == workspace.OrganizationId);
        Assert.Equal(AdminId, org.OwnerId);

        var subscription = await Context.OrganizationSubscriptions.SingleAsync(s => s.OrganizationId == org.Id);
        Assert.Equal(Constants.OrganizationTiers.Free, subscription.Tier);

        var ownerAccess = await Context.UserAccesses.SingleAsync(ua =>
            ua.UserId == AdminId && ua.ScopeType == Constants.ScopeTypes.Organization && ua.ScopeId == org.Id);
        Assert.Equal(RoleConfiguration.OrgOwnerRoleId, ownerAccess.RoleId);
    }

    [Fact]
    public async Task RunAsync_is_idempotent()
    {
        var backfill = GetService<IOrganizationBackfillService>();

        await backfill.RunAsync();
        var orgCountAfterFirstRun = await Context.Organizations.CountAsync();

        await backfill.RunAsync();
        var orgCountAfterSecondRun = await Context.Organizations.CountAsync();

        Assert.Equal(orgCountAfterFirstRun, orgCountAfterSecondRun);
    }
}
