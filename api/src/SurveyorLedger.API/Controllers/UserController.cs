using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Models.User;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;
using System.Security.Claims;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ICasbinService _casbinService;
        private readonly ILogger<UserController> _logger;

        public UserController(IAuthService authService, ICasbinService casbinService, ILogger<UserController> logger)
        {
            _authService = authService;
            _casbinService = casbinService;
            _logger = logger;
        }

        [HttpGet("profile")]
        public async Task<ActionResult<ApiResponse<UserProfileResponse>>> GetProfile()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userId, out var id))
                return Unauthorized(ApiResponse<object>.Fail("Invalid user ID"));

            var result = await _authService.GetAccountByIdAsync(id);
            if (result == null)
                return NotFound(ApiResponse<object>.Fail("User not found"));

            return Ok(ApiResponse<UserProfileResponse>.Ok(ToResponse(result.Value.person, result.Value.account)));
        }

        [HttpGet("search")]
        public async Task<ActionResult<ApiResponse<List<UserSearchResponse>>>> Search([FromQuery] string q)
        {
            var people = await _authService.SearchPeopleAsync(q ?? "");
            var results = people.Select(p => new UserSearchResponse
            {
                UserId = p.Id,
                FirstName = p.FirstName,
                LastName = p.LastName,
                Email = p.Email
            }).ToList();
            return Ok(ApiResponse<List<UserSearchResponse>>.Ok(results));
        }

        /// <summary>
        /// The caller's actual permission set in one workspace, straight from Casbin -
        /// lets the UI hide/show actions by real capability instead of guessing from a
        /// role-name string comparison.
        /// </summary>
        [HttpGet("me/permissions")]
        public async Task<ActionResult<ApiResponse<List<string>>>> GetMyPermissions([FromQuery] Guid workspaceId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userId, out var id))
                return Unauthorized(ApiResponse<object>.Fail("Invalid user ID"));

            var permissions = await _casbinService.GetPermissionsAsync(id.ToString(), workspaceId.ToString());
            var names = permissions.Select(p => $"{p.Resource}.{p.Action}").OrderBy(n => n).ToList();
            return Ok(ApiResponse<List<string>>.Ok(names));
        }

        [HttpPut("profile")]
        public async Task<ActionResult<ApiResponse<UserProfileResponse>>> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userId, out var id))
                return Unauthorized(ApiResponse<object>.Fail("Invalid user ID"));

            var person = await _authService.UpdateProfileAsync(id, request);
            var result = await _authService.GetAccountByIdAsync(id);
            return Ok(ApiResponse<UserProfileResponse>.Ok(ToResponse(person, result!.Value.account)));
        }

        public static UserProfileResponse ToResponse(Person person, UserAccount account) => new()
        {
            UserId = account.Id,
            Email = person.Email,
            FirstName = person.FirstName,
            LastName = person.LastName,
            Phone = person.Phone,
            EmailVerified = account.EmailVerified,
            Address = new AddressDto
            {
                Street = person.Address.Street,
                City = person.Address.City,
                District = person.Address.District,
                PostalCode = person.Address.PostalCode,
                Country = person.Address.Country
            },
            CreatedAt = person.CreatedAt
        };
    }
}
