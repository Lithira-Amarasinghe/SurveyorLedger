using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Auth;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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
        public async Task<ActionResult<ApiResponse<AuthResponse>>> Register([FromBody] RegisterRequest request)
        {
            var (user, accessToken, refreshToken, expiresIn) = await _authService.RegisterAsync(request);
            SetRefreshTokenCookie(refreshToken);

            return Ok(ApiResponse<AuthResponse>.Ok(new AuthResponse
            {
                UserId = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = expiresIn
            }));
        }

        [HttpPost("login")]
        public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest request)
        {
            var (user, accessToken, refreshToken, expiresIn) = await _authService.LoginAsync(request);
            SetRefreshTokenCookie(refreshToken);

            return Ok(ApiResponse<AuthResponse>.Ok(new AuthResponse
            {
                UserId = user.Id,
                Email = user.Email,
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
            return Ok(ApiResponse<object>.Ok(new { message = "Email verified successfully" }));
        }

        [HttpPost("refresh-token")]
        public async Task<ActionResult<ApiResponse<AuthResponse>>> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            var refreshToken = request.RefreshToken ?? Request.Cookies["refreshToken"];
            if (string.IsNullOrEmpty(refreshToken))
                return Unauthorized(ApiResponse<object>.Fail("Refresh token required"));

            // TODO: Implement refresh token validation + new token generation
            return Unauthorized(ApiResponse<object>.Fail("Not implemented"));
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
