using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Services;
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
}
