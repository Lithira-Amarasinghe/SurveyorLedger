using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SurveyorLedger.API.Models.Auth;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Every endpoint here is unauthenticated and credential- or email-sending-related,
    // so the whole controller is rate limited per IP rather than picking endpoints.
    [EnableRateLimiting("auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IAuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<ActionResult<ApiResponse<object>>> Register([FromBody] RegisterRequest request)
        {
            await _authService.RegisterAsync(request);
            return Ok(ApiResponse<object>.Ok(new { message = "Check your email for a verification code." }));
        }

        [HttpPost("login")]
        public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest request)
        {
            var (user, accessToken, refreshToken, expiresIn) = await _authService.LoginAsync(request);
            SetRefreshTokenCookie(refreshToken);

            return Ok(ApiResponse<AuthResponse>.Ok(new AuthResponse
            {
                UserId = user.Id,
                // LoginAsync matches on request.Email (non-null), so a user reaching this
                // point always has an Email - the ! here reflects that, not a real risk.
                Email = user.Email!,
                FirstName = user.FirstName,
                LastName = user.LastName,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = expiresIn
            }));
        }

        [HttpPost("verify-otp")]
        public async Task<ActionResult<ApiResponse<object>>> VerifyOtp([FromBody] VerifyOtpRequest request)
        {
            await _authService.VerifyOtpAsync(request.Email, request.OtpCode);
            return Ok(ApiResponse<object>.Ok(new { message = "Email verified. Please log in to continue." }));
        }

        [HttpPost("resend-otp")]
        public async Task<ActionResult<ApiResponse<object>>> ResendOtp([FromBody] ResendOtpRequest request)
        {
            await _authService.ResendOtpAsync(request.Email);
            return Ok(ApiResponse<object>.Ok(new { message = "If a pending registration exists for this email, a new code has been sent." }));
        }

        [HttpPost("forgot-password")]
        public async Task<ActionResult<ApiResponse<object>>> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            await _authService.RequestPasswordResetAsync(request.Email);
            return Ok(ApiResponse<object>.Ok(new { message = "If an account exists for this email, a reset code has been sent." }));
        }

        [HttpPost("reset-password")]
        public async Task<ActionResult<ApiResponse<object>>> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            await _authService.ResetPasswordAsync(request.Email, request.OtpCode, request.NewPassword);
            return Ok(ApiResponse<object>.Ok(new { message = "Password reset. Please log in with your new password." }));
        }

        [HttpPost("refresh-token")]
        public async Task<ActionResult<ApiResponse<AuthResponse>>> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            var refreshToken = request.RefreshToken ?? Request.Cookies["refreshToken"];
            if (string.IsNullOrEmpty(refreshToken))
                return Unauthorized(ApiResponse<object>.Fail("Refresh token required"));

            var (user, accessToken, newRefreshToken, expiresIn) = await _authService.RefreshTokenAsync(refreshToken);
            SetRefreshTokenCookie(newRefreshToken);

            return Ok(ApiResponse<AuthResponse>.Ok(new AuthResponse
            {
                UserId = user.Id,
                Email = user.Email!,
                FirstName = user.FirstName,
                LastName = user.LastName,
                AccessToken = accessToken,
                RefreshToken = newRefreshToken,
                ExpiresIn = expiresIn
            }));
        }

        [HttpPost("logout")]
        public async Task<ActionResult<ApiResponse<object>>> Logout([FromBody] RefreshTokenRequest request)
        {
            var refreshToken = request.RefreshToken ?? Request.Cookies["refreshToken"];
            if (!string.IsNullOrEmpty(refreshToken))
                await _authService.LogoutAsync(refreshToken);

            Response.Cookies.Delete("refreshToken");
            return Ok(ApiResponse<object>.Ok(new { message = "Logged out." }));
        }

        private void SetRefreshTokenCookie(string refreshToken)
        {
            Response.Cookies.Append("refreshToken", refreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddDays(7)
            });
        }
    }
}
