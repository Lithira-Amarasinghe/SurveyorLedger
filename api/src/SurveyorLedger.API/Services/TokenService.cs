using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SurveyorLedger.Core;

namespace SurveyorLedger.API.Services;

public interface ITokenService
{
    (string accessToken, string refreshToken, int expiresIn) GenerateTokens(Guid userId, string? email);
    Guid? ValidateAccessToken(string token);
    Guid? ValidateRefreshToken(string token);
}

public class TokenService : ITokenService
{
    private readonly IConfiguration _config;
    private readonly ILogger<TokenService> _logger;

    public TokenService(IConfiguration config, ILogger<TokenService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public (string accessToken, string refreshToken, int expiresIn) GenerateTokens(Guid userId, string? email)
    {
        var jwtKey = _config["JwtSettings:Key"] ?? throw new InvalidOperationException("JwtSettings:Key not configured");
        var issuer = _config["JwtSettings:Issuer"] ?? throw new InvalidOperationException("JwtSettings:Issuer not configured");
        var audience = _config["JwtSettings:Audience"] ?? throw new InvalidOperationException("JwtSettings:Audience not configured");
        var accessExpiryMinutes = int.Parse(_config["JwtSettings:ExpirationMinutes"] ?? "15");
        var refreshExpiryDays = int.Parse(_config["JwtSettings:RefreshTokenExpirationDays"] ?? "7");

        if (jwtKey.Length < 32)
        {
            throw new InvalidOperationException("JwtSettings:Key must be at least 32 characters for HS256");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Email claim omitted entirely when null (client user not yet invited) - a
        // Claim with a null/empty value is a common source of downstream NREs, so
        // don't add it rather than add it defensively.
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(Constants.ClaimNames.UserId, userId.ToString())
        };
        if (!string.IsNullOrEmpty(email))
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
        }

        // Access token (short-lived JWT)
        var accessToken = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(accessExpiryMinutes),
            signingCredentials: credentials
        );

        var accessTokenString = new JwtSecurityTokenHandler().WriteToken(accessToken);

        // Refresh token (opaque string, stored in DB by AuthService)
        var refreshTokenString = Guid.NewGuid().ToString("N");

        _logger.LogInformation("Generated tokens for user {UserId}", userId);

        return (accessTokenString, refreshTokenString, accessExpiryMinutes * 60);
    }

    public Guid? ValidateAccessToken(string token)
    {
        try
        {
            var jwtKey = _config["JwtSettings:Key"] ?? throw new InvalidOperationException("JwtSettings:Key not configured");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

            var handler = new JwtSecurityTokenHandler();
            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = _config["JwtSettings:Issuer"],
                ValidateAudience = true,
                ValidAudience = _config["JwtSettings:Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out SecurityToken validatedToken);

            var jwtToken = validatedToken as JwtSecurityToken;
            var userIdClaim = jwtToken?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);

            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
            {
                return userId;
            }

            _logger.LogWarning("Token validation failed: UserId claim missing or invalid");
            return null;
        }
        catch (SecurityTokenExpiredException ex)
        {
            _logger.LogWarning(ex, "Access token expired");
            return null;
        }
        catch (SecurityTokenInvalidSignatureException ex)
        {
            _logger.LogWarning(ex, "Token has invalid signature");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return null;
        }
    }

    public Guid? ValidateRefreshToken(string token)
    {
        // Refresh token validation done in AuthService with DB lookup
        // This is placeholder for future DB-backed token validation if needed
        // ponytail: refresh token stored in AuthToken table, validated via DB lookup in AuthService
        return null;
    }
}
