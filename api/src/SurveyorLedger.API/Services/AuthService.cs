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
    /// Get active user by email.
    /// </summary>
    Task<User?> GetUserByEmailAsync(string email);
    Task<User?> GetUserByIdAsync(Guid id);
}

/// <summary>
/// Authentication service handling user registration, login, and OTP verification.
/// </summary>
public class AuthService : IAuthService
{
    private const string RegistrationTokenType = "Registration";

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

            var otp = await IssueRegistrationOtpAsync(email, otpExpiryMinutes);
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

        // Check user exists and password matches. PasswordHash is null for client-only users
        // (created without login credentials) - treat that as invalid credentials, not a crash.
        if (user == null || user.PasswordHash == null || !_passwordService.VerifyPassword(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed for email: {Email} - invalid credentials", request.Email);
            throw new AppException(Constants.ErrorCodes.InvalidCredentials, "Invalid email or password");
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

        _logger.LogInformation("User logged in: {Email}", user.Email);

        return (user, accessToken, refreshToken, expiresIn);
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

            var otp = await IssueRegistrationOtpAsync(email, otpExpiryMinutes);

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

    /// <summary>
    /// Enforces the resend cooldown, removes any prior unverified OTP row for this
    /// (email, Registration) pair, and stages a fresh one. Does not save or send - caller
    /// owns the transaction and the actual send. Guarantees at most one live unverified
    /// EmailVerification row per (Email, TokenType) at a time.
    /// </summary>
    /// <returns>The plaintext OTP to send.</returns>
    private async Task<string> IssueRegistrationOtpAsync(string email, int otpExpiryMinutes)
    {
        var cooldownSeconds = int.Parse(_config["OTP:ResendCooldownSeconds"] ?? "60");

        var existing = await _context.EmailVerifications
            .Where(e => e.Email == email && e.TokenType == RegistrationTokenType && e.VerifiedAt == null)
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
            TokenType = RegistrationTokenType,
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
