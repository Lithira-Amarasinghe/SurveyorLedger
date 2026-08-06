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
    /// Register a new user with email and password.
    /// Returns user, access token, refresh token, and expiration time.
    /// </summary>
    Task<(User user, string accessToken, string refreshToken, int expiresIn)> RegisterAsync(RegisterRequest request);

    /// <summary>
    /// Login user with email and password.
    /// Returns user, access token, refresh token, and expiration time.
    /// </summary>
    Task<(User user, string accessToken, string refreshToken, int expiresIn)> LoginAsync(LoginRequest request);

    /// <summary>
    /// Verify OTP code sent to user's email.
    /// Returns true if verification successful, throws AppException if OTP invalid or expired.
    /// </summary>
    Task<bool> VerifyOtpAsync(string email, string otpCode);

    /// <summary>
    /// Get user by email if active.
    /// </summary>
    Task<User?> GetUserByEmailAsync(string email);
}

/// <summary>
/// Authentication service handling user registration, login, and OTP verification.
/// </summary>
public class AuthService : IAuthService
{
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
    /// Register a new user and send OTP verification email.
    /// User is created but not verified until OTP is confirmed.
    /// </summary>
    public async Task<(User user, string accessToken, string refreshToken, int expiresIn)> RegisterAsync(RegisterRequest request)
    {
        // Check if user already exists
        var existing = await _context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == request.Email);
        if (existing != null)
        {
            _logger.LogWarning("Registration attempted for existing email: {Email}", request.Email);
            throw new AppException(Constants.ErrorCodes.UserAlreadyExists, "Email already registered");
        }

        // Create new user
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PasswordHash = _passwordService.HashPassword(request.Password),
            EmailVerified = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Users.AddAsync(user);

        // Generate OTP
        var otp = GenerateOtp();
        var otpHash = _passwordService.HashPassword(otp);
        var otpExpiryMinutes = int.Parse(_config["OTP:ExpirationMinutes"] ?? "10");

        var emailVerification = new EmailVerification
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            OTPCodeHash = otpHash,
            TokenType = "Registration",
            ExpiresAt = DateTime.UtcNow.AddMinutes(otpExpiryMinutes),
            Attempts = 0,
            CreatedAt = DateTime.UtcNow
        };

        await _context.EmailVerifications.AddAsync(emailVerification);
        await _context.SaveChangesAsync();

        // Send OTP to email
        try
        {
            await _emailService.SendVerificationOtpAsync(request.Email, otp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send OTP email to {Email}", request.Email);
        }

        // Generate tokens for unverified user
        var (accessToken, refreshToken, expiresIn) = _tokenService.GenerateTokens(user.Id, user.Email);

        _logger.LogInformation("User registered: {Email}", user.Email);

        return (user, accessToken, refreshToken, expiresIn);
    }

    /// <summary>
    /// Login with email and password. User must be verified and active.
    /// Returns tokens on success, throws AppException on failure.
    /// </summary>
    public async Task<(User user, string accessToken, string refreshToken, int expiresIn)> LoginAsync(LoginRequest request)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email && u.IsActive);

        // Check user exists and password matches
        if (user == null || !_passwordService.VerifyPassword(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed for email: {Email} - invalid credentials", request.Email);
            throw new AppException(Constants.ErrorCodes.InvalidCredentials, "Invalid email or password");
        }

        // Check email is verified
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
    /// Verify OTP code. Marks user's email as verified on success.
    /// Throws AppException if OTP invalid, expired, or user not found.
    /// </summary>
    public async Task<bool> VerifyOtpAsync(string email, string otpCode)
    {
        var verification = await _context.EmailVerifications
            .FirstOrDefaultAsync(e => e.Email == email && e.VerifiedAt == null);

        if (verification == null)
        {
            _logger.LogWarning("OTP verification attempted but no pending verification for email: {Email}", email);
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "No pending OTP verification for this email");
        }

        // Check if OTP expired
        if (verification.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("OTP verification failed - expired for email: {Email}", email);
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "OTP is expired");
        }

        // Check if max attempts exceeded
        var maxAttempts = int.Parse(_config["OTP:MaxAttempts"] ?? "3");
        if (verification.Attempts >= maxAttempts)
        {
            _logger.LogWarning("OTP verification failed - max attempts exceeded for email: {Email}", email);
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "Maximum OTP attempts exceeded");
        }

        // Verify OTP
        if (!_passwordService.VerifyPassword(otpCode, verification.OTPCodeHash))
        {
            verification.Attempts++;
            await _context.SaveChangesAsync();
            _logger.LogWarning("OTP verification failed - invalid OTP for email: {Email}", email);
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "OTP is invalid");
        }

        // Find user
        var user = await _context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
        {
            _logger.LogError("User not found during OTP verification for email: {Email}", email);
            throw new AppException(Constants.ErrorCodes.UserNotFound, "User not found");
        }

        // Mark verification as complete
        verification.VerifiedAt = DateTime.UtcNow;

        // Mark user email as verified
        user.EmailVerified = true;
        user.EmailVerifiedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Email verified for user: {Email}", email);

        return true;
    }

    /// <summary>
    /// Get active user by email.
    /// </summary>
    public async Task<User?> GetUserByEmailAsync(string email)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
    }

    /// <summary>
    /// Generate a 6-digit OTP code.
    /// </summary>
    private static string GenerateOtp()
    {
        var random = new Random();
        return random.Next(100000, 999999).ToString();
    }
}
