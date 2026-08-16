using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Services;
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
}
