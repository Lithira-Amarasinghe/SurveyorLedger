using SurveyorLedger.Core;
using SurveyorLedger.Data.Configurations;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class UserAccessGrantServiceTests : WorkspaceIntegrationTestBase
{
    [Fact]
    public async Task GrantAsync_ResolvesUserAccountNav_NotPerson()
    {
        var access = await GrantService.GrantAsync(SurveyorId, RoleConfiguration.SurveyorRoleId,
            Constants.ScopeTypes.Job, Guid.NewGuid(), AdminId);

        Assert.Equal(SurveyorId, access.UserId);
        Assert.NotNull(access.User); // UserAccount nav, must be loaded
        Assert.IsType<SurveyorLedger.Data.Entities.UserAccount>(access.User);
    }
}
