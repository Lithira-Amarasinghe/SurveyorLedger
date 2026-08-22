using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// Workspace.OrganizationId is a required FK, so a genuinely orphaned workspace (the case this
/// service exists to fix) can no longer exist in this DB - the FK constraint itself rejects it.
/// These tests exercise the safe-no-op path that runs on every real startup once every
/// workspace has already been backfilled once.
/// </summary>
public class OrganizationBackfillServiceTests : WorkspaceIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IOrganizationBackfillService, OrganizationBackfillService>();
    }

    [Fact]
    public async Task RunAsync_is_a_safe_noop_when_every_workspace_already_has_an_organization()
    {
        var backfill = GetService<IOrganizationBackfillService>();
        var orgCountBefore = await Context.Organizations.CountAsync();

        await backfill.RunAsync();

        var orgCountAfter = await Context.Organizations.CountAsync();
        Assert.Equal(orgCountBefore, orgCountAfter);
    }

    [Fact]
    public async Task RunAsync_is_idempotent_across_repeated_calls()
    {
        var backfill = GetService<IOrganizationBackfillService>();

        await backfill.RunAsync();
        var orgCountAfterFirstRun = await Context.Organizations.CountAsync();

        await backfill.RunAsync();
        var orgCountAfterSecondRun = await Context.Organizations.CountAsync();

        Assert.Equal(orgCountAfterFirstRun, orgCountAfterSecondRun);
    }

    [Fact]
    public async Task RunAsync_grants_OrgMember_to_a_preexisting_workspace_member_without_org_access()
    {
        // Simulates data from before this feature: SurveyorId has active Workspace-scope
        // access (seeded by WorkspaceIntegrationTestBase) but no Organization-scope grant -
        // the base seed's direct GrantAsync call now chain-grants OrgMember automatically
        // (Surveyor's policy reaches Organization as of this feature), so remove that grant
        // here to reproduce the actual pre-existing-data scenario this backfill exists for.
        await Context.UserAccesses
            .Where(ua => ua.UserId == SurveyorId && ua.ScopeType == Constants.ScopeTypes.Organization)
            .ExecuteDeleteAsync();

        var backfill = GetService<IOrganizationBackfillService>();
        await backfill.RunAsync();

        var organizationId = await Context.Workspaces.Where(w => w.Id == WorkspaceId).Select(w => w.OrganizationId).FirstAsync();
        var org = await Context.Organizations.SingleAsync(o => o.Id == organizationId);

        var hasOrgMember = await Context.UserAccesses.AnyAsync(ua =>
            ua.UserId == SurveyorId && ua.ScopeType == Constants.ScopeTypes.Organization && ua.ScopeId == org.Id && ua.IsActive);
        Assert.True(hasOrgMember);
    }
}
