using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Data.Configurations;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class InvitationServiceTests : WorkspaceIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IEmailService, NoOpEmailService>();
        services.AddScoped<IPasswordService, PasswordService>();
        services.AddScoped<IInvitationService, InvitationService>();
        // CreateScopedInvitationAsync sends an invite email, which needs UiBaseUrl configured.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppSettings:UiBaseUrl"] = "https://test.local" })
            .Build());
    }

    [Fact]
    public async Task CreateScopedInvitationAsync_ForNewEmail_CreatesPersonOnly_NoUserAccount()
    {
        var svc = GetService<IInvitationService>();
        var invitation = await svc.CreateScopedInvitationAsync(
            Constants.ScopeTypes.Workspace, WorkspaceId, RoleConfiguration.MemberRoleId,
            "Test Workspace", AdminId, "newperson@test.local", "New", "Person", null, null);

        var person = await Context.People.FirstAsync(p => p.Id == invitation.UserId);
        Assert.Equal("newperson@test.local", person.Email);

        var hasAccount = await Context.UserAccounts.AnyAsync(a => a.PersonId == person.Id);
        Assert.False(hasAccount);
    }

    [Fact]
    public async Task CompleteInvitationAsync_CreatesUserAccountForExistingPerson()
    {
        var svc = GetService<IInvitationService>();
        var invitation = await svc.CreateScopedInvitationAsync(
            Constants.ScopeTypes.Workspace, WorkspaceId, RoleConfiguration.MemberRoleId,
            "Test Workspace", AdminId, "complete@test.local", "Complete", "Me", null, null);

        await svc.CompleteInvitationAsync(invitation.Token, new SurveyorLedger.API.Models.Invitation.CompleteInvitationRequest
        {
            FirstName = "Complete", LastName = "Me", Password = "Passw0rd!", ConfirmPassword = "Passw0rd!"
        });

        var account = await Context.UserAccounts.FirstOrDefaultAsync(a => a.PersonId == invitation.UserId);
        Assert.NotNull(account);
        Assert.True(account!.HasCompletedSignup);
    }
}
