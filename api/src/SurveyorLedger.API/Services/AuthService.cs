using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Auth;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

/// <summary>
/// Interface for authentication business logic.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Register a new user. Does NOT create a User row or issue tokens - signup data is
    /// held in PendingRegistration until the OTP is confirmed via VerifyOtpAsync. Throws
    /// AppException if the email is already registered, or if the verification email
    /// cannot be sent (registration is rolled back in that case).
    /// </summary>
    Task RegisterAsync(RegisterRequest request);

    /// <summary>
    /// Login user with email and password.
    /// Returns user, access token, refresh token, and expiration time.
    /// </summary>
    Task<(User user, string accessToken, string refreshToken, int expiresIn)> LoginAsync(LoginRequest request);

    /// <summary>
    /// Exchange a valid refresh token for a fresh access token, rotating the refresh token
    /// (the old one is revoked). Throws AppException if the token is unknown, expired, or
    /// already revoked.
    /// </summary>
    Task<(User user, string accessToken, string refreshToken, int expiresIn)> RefreshTokenAsync(string refreshToken);

    /// <summary>Revokes a single refresh token - the logout path. Silently no-ops if unknown.</summary>
    Task LogoutAsync(string refreshToken);

    /// <summary>
    /// Verify the OTP code for a pending registration. On success, creates the User row
    /// from the matching PendingRegistration and consumes it. Does NOT issue tokens - the
    /// caller must log in separately. Throws AppException if the OTP is invalid, expired,
    /// or the pending registration is missing/expired.
    /// </summary>
    Task VerifyOtpAsync(string email, string otpCode);

    /// <summary>
    /// Resend a registration OTP for a still-live PendingRegistration. Silently no-ops
    /// (does not throw) if no pending registration exists for the email, to avoid leaking
    /// which emails have registered. Throws AppException on cooldown or send failure.
    /// </summary>
    Task ResendOtpAsync(string email);

    /// <summary>
    /// Request a password reset code for an account that already has a password. Silently
    /// no-ops if the email doesn't match a real, password-having account - never reveals
    /// whether an email is registered.
    /// </summary>
    Task RequestPasswordResetAsync(string email);

    /// <summary>
    /// Confirm a password reset with the emailed OTP and set a new password. Throws
    /// AppException if the code is invalid, expired, or attempts are exhausted.
    /// </summary>
    Task ResetPasswordAsync(string email, string otpCode, string newPassword);

    /// <summary>
    /// Get active user by email.
    /// </summary>
    Task<User?> GetUserByEmailAsync(string email);
    Task<User?> GetUserByIdAsync(Guid id);

    /// <summary>
    /// System-wide account search by name/email, not scoped to any workspace - used for
    /// picking a land owner, which is a data-tracking reference, not an access grant.
    /// </summary>
    Task<List<User>> SearchUsersAsync(string query);

    /// <summary>
    /// Updates the caller's own name/phone/address. Email/password are not touched here -
    /// those go through the dedicated verification/invite-accept flows.
    /// </summary>
    Task<User> UpdateProfileAsync(Guid userId, Models.User.UpdateProfileRequest request);
}

/// <summary>
/// Authentication service handling user registration, login, and OTP verification.
/// </summary>
public class AuthService : IAuthService
{
    private const string RegistrationTokenType = "Registration";
    private const string PasswordResetTokenType = "PasswordReset";
    private const string RefreshTokenType = "Refresh";

    private readonly ApplicationDbContext _context;
    private readonly IPasswordService _passwordService;
    private readonly ITokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        ApplicationDbContext context,
        IPasswordService passwordService,
        ITokenService tokenService,
        IEmailService emailService,
        IConfiguration config,
        ILogger<AuthService> logger)
    {
        _context = context;
        _passwordService = passwordService;
        _tokenService = tokenService;
        _emailService = emailService;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Stage a registration: validates the email isn't taken, upserts a PendingRegistration
    /// row, issues a fresh OTP, and sends it. The whole thing is transactional - if the
    /// email send fails, nothing is persisted, so registration visibly fails instead of
    /// silently succeeding with an unreachable OTP.
    /// </summary>
    public async Task RegisterAsync(RegisterRequest request)
    {
        var email = request.Email.Trim();

        var existingUser = await _context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email);
        if (existingUser != null)
        {
            _logger.LogWarning("Registration attempted for existing email: {Email}", email);
            throw new AppException(Constants.ErrorCodes.UserAlreadyExists, "Email already registered");
        }

        var otpExpiryMinutes = GetOtpExpiryMinutes();
        var passwordHash = _passwordService.HashPassword(request.Password);
        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            // A second registration attempt for the same still-pending email overwrites the
            // prior pending row and OTP rather than stacking duplicates - this is also what
            // makes "register again" behave like a resend for an abandoned/failed attempt.
            var pending = await _context.PendingRegistrations.FirstOrDefaultAsync(p => p.Email == email);
            if (pending == null)
            {
                pending = new PendingRegistration { Id = Guid.NewGuid(), Email = email, CreatedAt = DateTime.UtcNow };
                await _context.PendingRegistrations.AddAsync(pending);
            }
            pending.PasswordHash = passwordHash;
            pending.FirstName = firstName;
            pending.LastName = lastName;
            pending.ExpiresAt = DateTime.UtcNow.AddMinutes(otpExpiryMinutes);

            var otp = await IssueOtpAsync(email, RegistrationTokenType, otpExpiryMinutes);
            await _context.SaveChangesAsync();

            try
            {
                await _emailService.SendVerificationOtpAsync(email, otp, otpExpiryMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send OTP email to {Email} during registration - rolling back", email);
                throw new AppException(Constants.ErrorCodes.EmailSendFailed,
                    "Could not send the verification email. Please check the address and try again.", 502);
            }

            await transaction.CommitAsync();
        });

        _logger.LogInformation("Registration pending, awaiting OTP verification: {Email}", email);
    }

    /// <summary>
    /// Login with email and password. User must be verified and active.
    /// Returns tokens on success, throws AppException on failure.
    /// </summary>
    public async Task<(User user, string accessToken, string refreshToken, int expiresIn)> LoginAsync(LoginRequest request)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email && u.IsActive);

        // No such account - nothing to count attempts against, and the generic message
        // keeps this indistinguishable from a wrong password.
        if (user == null)
        {
            _logger.LogWarning("Login failed for email: {Email} - no such account", request.Email);
            throw new AppException(Constants.ErrorCodes.InvalidCredentials, "Invalid email or password");
        }

        if (user.LockoutEndsAt is DateTime lockedUntil && lockedUntil > DateTime.UtcNow)
        {
            var minutesLeft = (int)Math.Ceiling((lockedUntil - DateTime.UtcNow).TotalMinutes);
            _logger.LogWarning("Login blocked for {Email} - account locked for another {Minutes} minute(s)", request.Email, minutesLeft);
            throw new AppException(Constants.ErrorCodes.AccountLocked,
                $"Too many failed attempts. Try again in {minutesLeft} minute{(minutesLeft == 1 ? "" : "s")}.", 423);
        }

        // PasswordHash is null for a person added but not yet accepted - no password has
        // ever been set, so treat it as invalid credentials rather than a crash.
        if (user.PasswordHash == null || !_passwordService.VerifyPassword(request.Password, user.PasswordHash))
        {
            var maxAttempts = int.Parse(_config["Lockout:MaxFailedAttempts"] ?? "5");
            var lockoutMinutes = int.Parse(_config["Lockout:DurationMinutes"] ?? "15");

            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= maxAttempts)
            {
                user.LockoutEndsAt = DateTime.UtcNow.AddMinutes(lockoutMinutes);
                user.FailedLoginAttempts = 0;
                _logger.LogWarning("Account locked for {Email} after {MaxAttempts} failed attempts", request.Email, maxAttempts);
            }
            await _context.SaveChangesAsync();

            _logger.LogWarning("Login failed for email: {Email} - invalid credentials", request.Email);
            throw new AppException(Constants.ErrorCodes.InvalidCredentials, "Invalid email or password");
        }

        if (user.FailedLoginAttempts != 0 || user.LockoutEndsAt != null)
        {
            user.FailedLoginAttempts = 0;
            user.LockoutEndsAt = null;
        }

        // Defensive only - under the current flow a User row can't exist unverified, but
        // this stays in place in case of legacy data or a future path that creates one early.
        if (!user.EmailVerified)
        {
            _logger.LogWarning("Login attempted with unverified email: {Email}", request.Email);
            throw new AppException(
                Constants.ErrorCodes.EmailNotVerified,
                "Email not verified. Please verify your email first.");
        }

        // Generate tokens
        var (accessToken, refreshToken, expiresIn) = _tokenService.GenerateTokens(user.Id, user.Email);
        await PersistRefreshTokenAsync(user.Id, refreshToken);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User logged in: {Email}", user.Email);

        return (user, accessToken, refreshToken, expiresIn);
    }

    /// <summary>
    /// Rotate a refresh token: the presented one is revoked and a fresh pair is issued.
    /// Presenting an already-revoked token is treated as theft - the whole token family for
    /// that user is revoked, since a legitimate client never replays a rotated token.
    /// </summary>
    public async Task<(User user, string accessToken, string refreshToken, int expiresIn)> RefreshTokenAsync(string refreshToken)
    {
        var hash = HashToken(refreshToken);

        var stored = await _context.AuthTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.TokenType == RefreshTokenType);

        if (stored == null)
        {
            _logger.LogWarning("Refresh attempted with an unknown token");
            throw new AppException(Constants.ErrorCodes.InvalidToken, "Invalid refresh token", 401);
        }

        if (stored.RevokedAt != null)
        {
            _logger.LogWarning("Refresh attempted with a revoked token for user {UserId} - revoking all sessions", stored.UserId);
            await RevokeAllRefreshTokensAsync(stored.UserId);
            await _context.SaveChangesAsync();
            throw new AppException(Constants.ErrorCodes.InvalidToken, "Invalid refresh token", 401);
        }

        if (stored.ExpiresAt <= DateTime.UtcNow)
        {
            _logger.LogInformation("Refresh attempted with an expired token for user {UserId}", stored.UserId);
            throw new AppException(Constants.ErrorCodes.TokenExpired, "Refresh token expired", 401);
        }

        if (!stored.User.IsActive)
            throw new AppException(Constants.ErrorCodes.InvalidToken, "Invalid refresh token", 401);

        var (accessToken, newRefreshToken, expiresIn) = _tokenService.GenerateTokens(stored.User.Id, stored.User.Email);

        stored.RevokedAt = DateTime.UtcNow;
        await PersistRefreshTokenAsync(stored.User.Id, newRefreshToken);
        await _context.SaveChangesAsync();

        return (stored.User, accessToken, newRefreshToken, expiresIn);
    }

    public async Task LogoutAsync(string refreshToken)
    {
        var hash = HashToken(refreshToken);
        var stored = await _context.AuthTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.TokenType == RefreshTokenType && t.RevokedAt == null);

        if (stored == null)
            return;

        stored.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    private async Task PersistRefreshTokenAsync(Guid userId, string refreshToken)
    {
        var refreshExpiryDays = int.Parse(_config["JwtSettings:RefreshTokenExpirationDays"] ?? "7");
        await _context.AuthTokens.AddAsync(new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenType = RefreshTokenType,
            TokenHash = HashToken(refreshToken),
            ExpiresAt = DateTime.UtcNow.AddDays(refreshExpiryDays),
            CreatedAt = DateTime.UtcNow
        });
    }

    private async Task RevokeAllRefreshTokensAsync(Guid userId)
    {
        var active = await _context.AuthTokens
            .Where(t => t.UserId == userId && t.TokenType == RefreshTokenType && t.RevokedAt == null)
            .ToListAsync();

        foreach (var token in active)
            token.RevokedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// SHA-256, deliberately not BCrypt: a refresh token is a 122-bit random value, so slow
    /// hashing buys nothing, and BCrypt's per-hash salt would make lookup-by-hash impossible
    /// (every row would have to be loaded and verified one by one).
    /// </summary>
    private static string HashToken(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Verify OTP code and complete registration by creating the User row. Does not issue
    /// tokens - the caller must log in separately.
    /// </summary>
    public async Task VerifyOtpAsync(string email, string otpCode)
    {
        email = email.Trim();

        // Scoped by TokenType so an OTP for a different flow (e.g. a future password-reset
        // or email-change) can never be accepted here. Latest row wins in case more than one
        // ever exists momentarily (defensive - RegisterAsync/ResendOtpAsync keep this to one).
        var verification = await _context.EmailVerifications
            .Where(e => e.Email == email && e.TokenType == RegistrationTokenType && e.VerifiedAt == null)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync();

        if (verification == null)
        {
            _logger.LogWarning("OTP verification attempted but no pending verification for email: {Email}", email);
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "No pending OTP verification for this email");
        }

        if (verification.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("OTP verification failed - expired for email: {Email}", email);
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "OTP is expired");
        }

        var maxAttempts = int.Parse(_config["OTP:MaxAttempts"] ?? "3");
        if (verification.Attempts >= maxAttempts)
        {
            _logger.LogWarning("OTP verification failed - max attempts exceeded for email: {Email}", email);
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "Maximum OTP attempts exceeded");
        }

        if (!_passwordService.VerifyPassword(otpCode, verification.OTPCodeHash))
        {
            verification.Attempts++;
            await _context.SaveChangesAsync();
            _logger.LogWarning("OTP verification failed - invalid OTP for email: {Email}", email);
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "OTP is invalid");
        }

        var pending = await _context.PendingRegistrations.FirstOrDefaultAsync(p => p.Email == email);
        if (pending == null)
        {
            _logger.LogError("OTP verified but no pending registration found for email: {Email}", email);
            throw new AppException(Constants.ErrorCodes.RegistrationExpired,
                "Your registration session has expired. Please sign up again.", 410);
        }

        // Defensive: guards against a race where the email got registered through another
        // path between OTP send and verification. RegisterAsync already blocks this in the
        // common case, so this should be unreachable in practice.
        var alreadyExists = await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email);
        if (alreadyExists)
        {
            _context.PendingRegistrations.Remove(pending);
            verification.VerifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            _logger.LogError("OTP verified but a User already exists for email: {Email}", email);
            throw new AppException(Constants.ErrorCodes.UserAlreadyExists, "Email already registered", 409);
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = pending.Email,
            FirstName = pending.FirstName,
            LastName = pending.LastName,
            PasswordHash = pending.PasswordHash,
            EmailVerified = true,
            EmailVerifiedAt = DateTime.UtcNow,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.Users.AddAsync(user);
        _context.PendingRegistrations.Remove(pending);
        verification.VerifiedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Registration completed for: {Email}", email);
    }

    /// <summary>
    /// Resend a registration OTP. No-ops silently if there's no live pending registration
    /// for the email (don't reveal whether it exists), otherwise regenerates and resends
    /// subject to a cooldown.
    /// </summary>
    public async Task ResendOtpAsync(string email)
    {
        email = email.Trim();

        var pending = await _context.PendingRegistrations.FirstOrDefaultAsync(p => p.Email == email);
        if (pending == null || pending.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogInformation("Resend OTP requested for {Email} - no live pending registration", email);
            return;
        }

        var otpExpiryMinutes = GetOtpExpiryMinutes();

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var otp = await IssueOtpAsync(email, RegistrationTokenType, otpExpiryMinutes);

            var pendingInTx = await _context.PendingRegistrations.FirstOrDefaultAsync(p => p.Email == email)
                ?? throw new AppException(Constants.ErrorCodes.RegistrationExpired,
                    "Your registration session has expired. Please sign up again.", 410);
            // Extend the pending registration so it doesn't expire out from under the new OTP.
            pendingInTx.ExpiresAt = DateTime.UtcNow.AddMinutes(otpExpiryMinutes);

            await _context.SaveChangesAsync();

            try
            {
                await _emailService.SendVerificationOtpAsync(email, otp, otpExpiryMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send OTP resend email to {Email} - rolling back", email);
                throw new AppException(Constants.ErrorCodes.EmailSendFailed,
                    "Could not send the verification email. Please try again.", 502);
            }

            await transaction.CommitAsync();
        });

        _logger.LogInformation("OTP resent for: {Email}", email);
    }

    /// <summary>
    /// Get active user by email.
    /// </summary>
    public async Task<User?> GetUserByEmailAsync(string email)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
    }

    /// <summary>
    /// Get active user by id. Preferred over email lookup for resolving the
    /// authenticated caller - a user may have no email yet (client, not invited).
    /// </summary>
    public async Task<User?> GetUserByIdAsync(Guid id)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Id == id && u.IsActive);
    }

    public async Task<List<User>> SearchUsersAsync(string query)
    {
        var term = query.Trim();
        if (term.Length < 2)
            return new List<User>();

        return await _context.Users
            .Where(u => u.IsActive && (
                EF.Functions.Like(u.FirstName, $"%{term}%") ||
                EF.Functions.Like(u.LastName, $"%{term}%") ||
                (u.Email != null && EF.Functions.Like(u.Email, $"%{term}%"))))
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Take(20)
            .ToListAsync();
    }

    public async Task<User> UpdateProfileAsync(Guid userId, Models.User.UpdateProfileRequest request)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive)
            ?? throw new AppException(Constants.ErrorCodes.UserNotFound, "User not found", 404);

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Phone = request.Phone?.Trim();
        user.Address = new Address
        {
            Street = request.Address?.Street,
            City = request.Address?.City,
            District = request.Address?.District,
            PostalCode = request.Address?.PostalCode,
            Country = request.Address?.Country
        };
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Request a password reset code. Reuses the same EmailVerification/OTP mechanism as
    /// registration, scoped by TokenType=PasswordReset so the codes can never cross flows.
    /// Only issued for an account that already has a password - a not-yet-accepted invitee
    /// has nothing to "reset" and should use the invite-accept flow instead. Silently
    /// no-ops for an unknown email or a password-less account, same anti-enumeration
    /// pattern as ResendOtpAsync.
    /// </summary>
    public async Task RequestPasswordResetAsync(string email)
    {
        email = email.Trim();

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
        if (user == null || user.PasswordHash == null)
        {
            _logger.LogInformation("Password reset requested for {Email} - no eligible account", email);
            return;
        }

        var otpExpiryMinutes = GetOtpExpiryMinutes();

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var otp = await IssueOtpAsync(email, PasswordResetTokenType, otpExpiryMinutes);
            await _context.SaveChangesAsync();

            try
            {
                await _emailService.SendPasswordResetOtpAsync(email, otp, otpExpiryMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email to {Email} - rolling back", email);
                throw new AppException(Constants.ErrorCodes.EmailSendFailed,
                    "Could not send the reset email. Please try again.", 502);
            }

            await transaction.CommitAsync();
        });

        _logger.LogInformation("Password reset OTP sent for: {Email}", email);
    }

    /// <summary>
    /// Verify a password reset OTP and set the new password. Same verify-and-consume logic
    /// as VerifyOtpAsync, scoped to TokenType=PasswordReset.
    /// </summary>
    public async Task ResetPasswordAsync(string email, string otpCode, string newPassword)
    {
        email = email.Trim();

        var verification = await _context.EmailVerifications
            .Where(e => e.Email == email && e.TokenType == PasswordResetTokenType && e.VerifiedAt == null)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync();

        if (verification == null)
        {
            _logger.LogWarning("Password reset attempted but no pending reset for email: {Email}", email);
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "No pending password reset for this email");
        }

        if (verification.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Password reset failed - code expired for email: {Email}", email);
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "OTP is expired");
        }

        var maxAttempts = int.Parse(_config["OTP:MaxAttempts"] ?? "3");
        if (verification.Attempts >= maxAttempts)
        {
            _logger.LogWarning("Password reset failed - max attempts exceeded for email: {Email}", email);
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "Maximum OTP attempts exceeded");
        }

        if (!_passwordService.VerifyPassword(otpCode, verification.OTPCodeHash))
        {
            verification.Attempts++;
            await _context.SaveChangesAsync();
            _logger.LogWarning("Password reset failed - invalid code for email: {Email}", email);
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "OTP is invalid");
        }

        // Defensive: the account could in principle have been deactivated between request
        // and confirm. Same InvalidOtp error, not a distinct message - don't leak account state.
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive)
            ?? throw new AppException(Constants.ErrorCodes.InvalidOtp, "No pending password reset for this email");

        user.PasswordHash = _passwordService.HashPassword(newPassword);
        user.UpdatedAt = DateTime.UtcNow;
        verification.VerifiedAt = DateTime.UtcNow;

        // Resetting the password is the intended way out of a lockout - clearing it here
        // means a locked-out owner isn't stuck waiting once they've proven inbox control.
        user.FailedLoginAttempts = 0;
        user.LockoutEndsAt = null;

        // Resetting a password is the standard response to a suspected compromise, so every
        // existing session must die with it - otherwise an attacker holding a refresh token
        // keeps their access despite the reset.
        await RevokeAllRefreshTokensAsync(user.Id);

        await _context.SaveChangesAsync();
        _logger.LogInformation("Password reset completed for: {Email}", email);
    }

    /// <summary>
    /// Enforces the resend cooldown, removes any prior unverified OTP row for this
    /// (email, tokenType) pair, and stages a fresh one. Does not save or send - caller
    /// owns the transaction and the actual send. Guarantees at most one live unverified
    /// EmailVerification row per (Email, TokenType) at a time.
    /// </summary>
    /// <returns>The plaintext OTP to send.</returns>
    private async Task<string> IssueOtpAsync(string email, string tokenType, int otpExpiryMinutes)
    {
        var cooldownSeconds = int.Parse(_config["OTP:ResendCooldownSeconds"] ?? "60");

        var existing = await _context.EmailVerifications
            .Where(e => e.Email == email && e.TokenType == tokenType && e.VerifiedAt == null)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync();

        if (existing?.LastSentAt is DateTime lastSent)
        {
            var cooldownEndsAt = lastSent.AddSeconds(cooldownSeconds);
            if (cooldownEndsAt > DateTime.UtcNow)
            {
                var waitSeconds = (int)Math.Ceiling((cooldownEndsAt - DateTime.UtcNow).TotalSeconds);
                throw new AppException(Constants.ErrorCodes.ResendCooldown,
                    $"Please wait {waitSeconds} second{(waitSeconds == 1 ? "" : "s")} before requesting another code.", 429);
            }
        }

        if (existing != null)
            _context.EmailVerifications.Remove(existing);

        var otp = GenerateOtp();
        var emailVerification = new EmailVerification
        {
            Id = Guid.NewGuid(),
            Email = email,
            OTPCodeHash = _passwordService.HashPassword(otp),
            TokenType = tokenType,
            ExpiresAt = DateTime.UtcNow.AddMinutes(otpExpiryMinutes),
            Attempts = 0,
            LastSentAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        await _context.EmailVerifications.AddAsync(emailVerification);

        return otp;
    }

    private int GetOtpExpiryMinutes() => int.Parse(_config["OTP:ExpirationMinutes"] ?? "3");

    /// <summary>
    /// Generate a 6-digit OTP code.
    /// </summary>
    private static string GenerateOtp()
    {
        var random = new Random();
        return random.Next(100000, 999999).ToString();
    }
}
