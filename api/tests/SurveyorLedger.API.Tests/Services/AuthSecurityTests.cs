using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Auth;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// Covers the auth hardening added this session: per-account lockout after repeated failed
/// logins, and refresh-token rotation with reuse detection.
/// </summary>
public class AuthSecurityTests : WorkspaceIntegrationTestBase
{
    private IAuthService _authService = null!;
    private const string KnownPassword = "CorrectPassword123!";

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<ITokenService, TokenService>();
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
        services.AddScoped<IAuthService, AuthService>();
    }

    /// <summary>Gives the seeded Surveyor a real, verified password so they can actually log in.</summary>
    private async Task<string> MakeLoginableAsync()
    {
        var user = await Context.Users.FirstAsync(u => u.Id == SurveyorId);
        user.PasswordHash = GetService<IPasswordService>().HashPassword(KnownPassword);
        user.EmailVerified = true;
        await Context.SaveChangesAsync();
        return user.Email!;
    }

    private LoginRequest Login(string email, string password) => new() { Email = email, Password = password };

    [Fact]
    public async Task Login_WithCorrectPassword_Succeeds()
    {
        _authService = GetService<IAuthService>();
        var email = await MakeLoginableAsync();

        var (user, accessToken, refreshToken, _) = await _authService.LoginAsync(Login(email, KnownPassword));

        Assert.Equal(SurveyorId, user.Id);
        Assert.NotEmpty(accessToken);
        Assert.NotEmpty(refreshToken);
    }

    [Fact]
    public async Task Login_LocksAccountAfterMaxFailedAttempts()
    {
        _authService = GetService<IAuthService>();
        var email = await MakeLoginableAsync();

        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<AppException>(() => _authService.LoginAsync(Login(email, "WrongPassword!")));

        // Sixth attempt - even with the CORRECT password - must be refused while locked.
        var locked = await Assert.ThrowsAsync<AppException>(() => _authService.LoginAsync(Login(email, KnownPassword)));
        Assert.Equal(Constants.ErrorCodes.AccountLocked, locked.Code);
    }

    [Fact]
    public async Task Login_SuccessfulLogin_ResetsFailedAttemptCounter()
    {
        _authService = GetService<IAuthService>();
        var email = await MakeLoginableAsync();

        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<AppException>(() => _authService.LoginAsync(Login(email, "WrongPassword!")));

        await _authService.LoginAsync(Login(email, KnownPassword));

        var user = await Context.Users.FirstAsync(u => u.Id == SurveyorId);
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockoutEndsAt);
    }

    [Fact]
    public async Task RefreshToken_RotatesAndInvalidatesTheOldToken()
    {
        _authService = GetService<IAuthService>();
        var email = await MakeLoginableAsync();
        var (_, _, originalRefresh, _) = await _authService.LoginAsync(Login(email, KnownPassword));

        var (_, newAccess, newRefresh, _) = await _authService.RefreshTokenAsync(originalRefresh);

        Assert.NotEmpty(newAccess);
        Assert.NotEqual(originalRefresh, newRefresh);

        // The rotated-away token must no longer work.
        await Assert.ThrowsAsync<AppException>(() => _authService.RefreshTokenAsync(originalRefresh));
    }

    [Fact]
    public async Task RefreshToken_ReplayingARotatedToken_RevokesEverySession()
    {
        // Replay of an already-rotated token is a theft signal: no honest client does it,
        // so the whole family dies rather than letting the thief keep refreshing.
        _authService = GetService<IAuthService>();
        var email = await MakeLoginableAsync();
        var (_, _, originalRefresh, _) = await _authService.LoginAsync(Login(email, KnownPassword));
        var (_, _, currentRefresh, _) = await _authService.RefreshTokenAsync(originalRefresh);

        await Assert.ThrowsAsync<AppException>(() => _authService.RefreshTokenAsync(originalRefresh));

        // The legitimate, most-recent token is now dead too - the session is fully cut.
        await Assert.ThrowsAsync<AppException>(() => _authService.RefreshTokenAsync(currentRefresh));
    }

    [Fact]
    public async Task RefreshToken_UnknownToken_Rejected()
    {
        _authService = GetService<IAuthService>();
        await Assert.ThrowsAsync<AppException>(() => _authService.RefreshTokenAsync("not-a-real-token"));
    }

    [Fact]
    public async Task Logout_InvalidatesTheRefreshToken()
    {
        _authService = GetService<IAuthService>();
        var email = await MakeLoginableAsync();
        var (_, _, refreshToken, _) = await _authService.LoginAsync(Login(email, KnownPassword));

        await _authService.LogoutAsync(refreshToken);

        await Assert.ThrowsAsync<AppException>(() => _authService.RefreshTokenAsync(refreshToken));
    }

    [Fact]
    public async Task PasswordReset_KillsExistingSessions()
    {
        // Resetting a password is the standard response to a suspected compromise - an
        // attacker holding a refresh token must not survive it.
        _authService = GetService<IAuthService>();
        var passwordService = GetService<IPasswordService>();
        var email = await MakeLoginableAsync();
        var (_, _, refreshToken, _) = await _authService.LoginAsync(Login(email, KnownPassword));

        await _authService.RequestPasswordResetAsync(email);
        var verification = await Context.EmailVerifications
            .Where(e => e.Email == email && e.TokenType == "PasswordReset" && e.VerifiedAt == null)
            .OrderByDescending(e => e.CreatedAt)
            .FirstAsync();
        verification.OTPCodeHash = passwordService.HashPassword("123456");
        await Context.SaveChangesAsync();

        await _authService.ResetPasswordAsync(email, "123456", "BrandNewPassword456!");

        await Assert.ThrowsAsync<AppException>(() => _authService.RefreshTokenAsync(refreshToken));
    }

    [Fact]
    public async Task PasswordReset_ClearsAnActiveLockout()
    {
        // A locked-out owner must have a way back in without waiting - proving inbox
        // control via the reset code is that way.
        _authService = GetService<IAuthService>();
        var passwordService = GetService<IPasswordService>();
        var email = await MakeLoginableAsync();

        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<AppException>(() => _authService.LoginAsync(Login(email, "WrongPassword!")));

        await _authService.RequestPasswordResetAsync(email);
        var verification = await Context.EmailVerifications
            .Where(e => e.Email == email && e.TokenType == "PasswordReset" && e.VerifiedAt == null)
            .OrderByDescending(e => e.CreatedAt)
            .FirstAsync();
        verification.OTPCodeHash = passwordService.HashPassword("123456");
        await Context.SaveChangesAsync();

        await _authService.ResetPasswordAsync(email, "123456", "BrandNewPassword456!");

        var (user, _, _, _) = await _authService.LoginAsync(Login(email, "BrandNewPassword456!"));
        Assert.Equal(SurveyorId, user.Id);
    }
}
