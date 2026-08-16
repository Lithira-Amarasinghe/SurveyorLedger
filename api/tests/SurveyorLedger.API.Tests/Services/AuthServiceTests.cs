using SurveyorLedger.API.Models.Auth;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class AuthServiceTests : WorkspaceIntegrationTestBase
{
    protected override void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        services.AddScoped<IPasswordService, PasswordService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IEmailService, NoopEmailService>();
        services.AddScoped<IAuthService, AuthService>();
    }

    [Fact]
    public async Task Login_CreatesAccessTokenAndReturnsBothPersonAndAccount()
    {
        var authService = GetService<IAuthService>();
        var person = new SurveyorLedger.Data.Entities.Person
        {
            Id = Guid.NewGuid(), FirstName = "Nimal", LastName = "Perera", Email = "nimal@test.local",
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        var passwordService = GetService<IPasswordService>();
        var account = new SurveyorLedger.Data.Entities.UserAccount
        {
            Id = Guid.NewGuid(), PersonId = person.Id, PasswordHash = passwordService.HashPassword("Passw0rd!"),
            EmailVerified = true, HasCompletedSignup = true, IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        Context.People.Add(person);
        Context.UserAccounts.Add(account);
        await Context.SaveChangesAsync();

        var (loggedInPerson, loggedInAccount, accessToken, refreshToken, expiresIn) =
            await authService.LoginAsync(new LoginRequest { Email = "nimal@test.local", Password = "Passw0rd!" });

        Assert.Equal(person.Id, loggedInPerson.Id);
        Assert.Equal(account.Id, loggedInAccount.Id);
        Assert.NotEmpty(accessToken);
    }

    [Fact]
    public async Task Login_WithNoUserAccount_ThrowsInvalidCredentials()
    {
        var authService = GetService<IAuthService>();
        var person = new SurveyorLedger.Data.Entities.Person
        {
            Id = Guid.NewGuid(), FirstName = "Kamal", LastName = "Silva", Email = "kamal@test.local",
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        Context.People.Add(person);
        await Context.SaveChangesAsync();

        await Assert.ThrowsAsync<AppException>(() =>
            authService.LoginAsync(new LoginRequest { Email = "kamal@test.local", Password = "whatever" }));
    }
}
