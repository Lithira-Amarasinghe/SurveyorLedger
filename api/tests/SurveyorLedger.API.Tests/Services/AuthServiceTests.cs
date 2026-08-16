using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Auth;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class AuthServiceTests : WorkspaceIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IPasswordService, PasswordService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IEmailService, NoOpEmailService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Key"] = "test-signing-key-at-least-32-characters-long-for-hs256",
                ["JwtSettings:Issuer"] = "https://test.local",
                ["JwtSettings:Audience"] = "test-api",
                ["JwtSettings:ExpirationMinutes"] = "60",
                ["JwtSettings:RefreshTokenExpirationDays"] = "7",
                ["OTP:ExpirationMinutes"] = "3",
                ["OTP:MaxAttempts"] = "3",
                ["Lockout:MaxFailedAttempts"] = "5",
                ["Lockout:DurationMinutes"] = "15"
            })
            .Build());
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
