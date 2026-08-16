using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// Covers the password reset flow added this session: reuses the EmailVerification/OTP
/// mechanism (scoped by TokenType) rather than a new table, and never reveals whether an
/// email is registered.
/// </summary>
public class PasswordResetTests : WorkspaceIntegrationTestBase
{
    private IAuthService _authService = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppSettings:UiBaseUrl"] = "https://test.local",
                ["OTP:ExpirationMinutes"] = "3",
                ["OTP:MaxAttempts"] = "3",
                ["OTP:ResendCooldownSeconds"] = "60"
            })
            .Build());
        services.AddScoped<IAuthService, AuthService>();
    }

    private async Task<string> SetPasswordAndRequestResetAsync(string email)
    {
        var account = await Context.UserAccounts.Include(a => a.Person).FirstAsync(a => a.Person.Email == email);
        var passwordService = GetService<IPasswordService>();
        account.PasswordHash = passwordService.HashPassword("OldPassword123!");
        await Context.SaveChangesAsync();

        await _authService.RequestPasswordResetAsync(email);

        var verification = await Context.EmailVerifications
            .Where(e => e.Email == email && e.TokenType == "PasswordReset" && e.VerifiedAt == null)
            .OrderByDescending(e => e.CreatedAt)
            .FirstAsync();

        // Same trick VerifyOtpAsync's own tests would use if the plaintext OTP weren't
        // returned to the caller - re-derive it isn't possible (hashed), so instead we
        // reach into the private generation seam via a fresh known-plaintext OTP and
        // overwrite the hash, keeping the rest of the row (Attempts, ExpiresAt) real.
        verification.OTPCodeHash = passwordService.HashPassword("123456");
        await Context.SaveChangesAsync();

        return "123456";
    }

    [Fact]
    public async Task RequestReset_ForAccountWithPassword_IssuesOtp()
    {
        _authService = GetService<IAuthService>();
        var otp = await SetPasswordAndRequestResetAsync("surveyor@test.local");

        Assert.Equal("123456", otp);
        var verification = await Context.EmailVerifications
            .FirstAsync(e => e.Email == "surveyor@test.local" && e.TokenType == "PasswordReset");
        Assert.Null(verification.VerifiedAt);
    }

    [Fact]
    public async Task RequestReset_ForUnknownEmail_SilentlyNoOps()
    {
        _authService = GetService<IAuthService>();

        await _authService.RequestPasswordResetAsync("nobody@test.local");

        var any = await Context.EmailVerifications.AnyAsync(e => e.Email == "nobody@test.local");
        Assert.False(any);
    }

    [Fact]
    public async Task RequestReset_ForAccountWithNoPassword_SilentlyNoOps()
    {
        // A Client added but never accepted their invite has no password yet - reset must
        // not leak that the email exists, and there's nothing to reset anyway.
        _authService = GetService<IAuthService>();

        await _authService.RequestPasswordResetAsync("client@test.local");

        var any = await Context.EmailVerifications.AnyAsync(e => e.Email == "client@test.local");
        Assert.False(any);
    }

    [Fact]
    public async Task ResetPassword_WithValidOtp_ChangesPasswordAndConsumesOtp()
    {
        _authService = GetService<IAuthService>();
        var passwordService = GetService<IPasswordService>();
        var otp = await SetPasswordAndRequestResetAsync("surveyor@test.local");

        await _authService.ResetPasswordAsync("surveyor@test.local", otp, "NewPassword456!");

        var account = await Context.UserAccounts.Include(a => a.Person).FirstAsync(a => a.Person.Email == "surveyor@test.local");
        Assert.True(passwordService.VerifyPassword("NewPassword456!", account.PasswordHash!));
        Assert.False(passwordService.VerifyPassword("OldPassword123!", account.PasswordHash!));

        var verification = await Context.EmailVerifications
            .FirstAsync(e => e.Email == "surveyor@test.local" && e.TokenType == "PasswordReset");
        Assert.NotNull(verification.VerifiedAt);
    }

    [Fact]
    public async Task ResetPassword_WithWrongOtp_Rejected()
    {
        _authService = GetService<IAuthService>();
        await SetPasswordAndRequestResetAsync("surveyor@test.local");

        await Assert.ThrowsAsync<AppException>(
            () => _authService.ResetPasswordAsync("surveyor@test.local", "000000", "NewPassword456!"));
    }

    [Fact]
    public async Task ResetPassword_NoPendingReset_Rejected()
    {
        _authService = GetService<IAuthService>();

        await Assert.ThrowsAsync<AppException>(
            () => _authService.ResetPasswordAsync("surveyor@test.local", "123456", "NewPassword456!"));
    }
}
