using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Organization;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class OrganizationServiceTests : WorkspaceIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IOrganizationService, OrganizationService>();
    }

    [Fact]
    public async Task CreateOrganizationAsync_creates_org_with_Free_subscription_and_grants_OrgOwner()
    {
        var service = GetService<IOrganizationService>();
        var newOwner = await CreateUserAccountAsync("Nina", "Owner", "nina@test.local");

        var result = await service.CreateOrganizationAsync(newOwner, new OrganizationRequest { Name = "Nina Surveys" });

        Assert.Equal("Nina Surveys", result.Name);
        Assert.Equal(Constants.OrganizationTiers.Free, result.Tier);

        var allowed = await CasbinService.EnforceAsync(newOwner.ToString(), "organization", "create_workspace", result.Id.ToString());
        Assert.True(allowed);
    }

    [Fact]
    public async Task AddMemberAsync_by_non_owner_throws_Forbidden()
    {
        var service = GetService<IOrganizationService>();
        var owner = await CreateUserAccountAsync("Owner", "Person", "owner2@test.local");
        var intruder = await CreateUserAccountAsync("Intruder", "Person", "intruder@test.local");
        var target = await CreateUserAccountAsync("Target", "Person", "target@test.local");

        var org = await service.CreateOrganizationAsync(owner, new OrganizationRequest { Name = "Owner's Org" });

        await Assert.ThrowsAsync<ForbiddenException>(() => service.AddMemberAsync(org.Id, target, intruder));
    }
}
