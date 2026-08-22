using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Workspace;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class WorkspaceController : ControllerBase
    {
        private readonly IWorkspaceService _workspaceService;
        private readonly ILogger<WorkspaceController> _logger;

        public WorkspaceController(IWorkspaceService workspaceService, ILogger<WorkspaceController> logger)
        {
            _workspaceService = workspaceService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<WorkspaceResponse>>>> ListWorkspaces()
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var workspaces = await _workspaceService.GetUserWorkspacesAsync(userId);

            var response = workspaces.Select(ToResponse).ToList();

            return Ok(ApiResponse<List<WorkspaceResponse>>.Ok(response));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<WorkspaceResponse>>> CreateWorkspace([FromBody] WorkspaceRequest request)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var workspace = await _workspaceService.CreateWorkspaceAsync(userId, request.OrganizationId, request);

            return CreatedAtAction(nameof(GetWorkspaceById), new { id = workspace.Workspace.Id },
                ApiResponse<WorkspaceResponse>.Ok(ToResponse(workspace)));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<WorkspaceResponse>>> GetWorkspaceById(Guid id)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var workspace = await _workspaceService.GetWorkspaceByIdAsync(id, userId);
            if (workspace == null)
                return NotFound(ApiResponse<object>.Fail("Workspace not found"));

            return Ok(ApiResponse<WorkspaceResponse>.Ok(ToResponse(workspace)));
        }

        [HttpGet("{id}/members")]
        public async Task<ActionResult<ApiResponse<List<MemberResponse>>>> GetMembers(Guid id)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var members = await _workspaceService.GetMembersAsync(id, userId);

            var response = members.Select(m => new MemberResponse
            {
                UserId = m.UserId,
                Email = m.Email,
                FirstName = m.FirstName,
                LastName = m.LastName,
                Roles = m.Roles,
                AssignedAt = m.AssignedAt,
                IsOwner = m.IsOwner,
                FullAccessGrants = m.FullAccessGrants.Select(g => new MemberFullAccessGrantResponse
                {
                    ScopeType = g.ScopeType,
                    RoleName = g.RoleName,
                    Actions = g.Actions
                }).ToList(),
                AdditionalScopes = m.AdditionalScopes.Select(s => new MemberScopeGrantResponse
                {
                    ScopeType = s.ScopeType,
                    ScopeId = s.ScopeId,
                    Label = s.Label,
                    Role = s.Role
                }).ToList()
            }).ToList();

            return Ok(ApiResponse<List<MemberResponse>>.Ok(response));
        }

        [HttpGet("{id}/roles")]
        public async Task<ActionResult<ApiResponse<List<RoleResponse>>>> GetRoles(Guid id)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var roles = await _workspaceService.GetWorkspaceRolesAsync(id, userId);

            var response = roles.Select(r => new RoleResponse
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                Permissions = r.Permissions
                    .Select(p => new PermissionResponse
                    {
                        Name = p.Name,
                        Resource = p.Resource,
                        Action = p.Action,
                        Description = p.Description
                    })
                    .ToList()
            }).ToList();

            return Ok(ApiResponse<List<RoleResponse>>.Ok(response));
        }

        /// <summary>Role names valid to pick for the given scope - "Workspace" for invite/role-change, "Job" for assigning someone to a job.</summary>
        [HttpGet("{id}/roles/eligible")]
        public async Task<ActionResult<ApiResponse<List<string>>>> GetEligibleRoles(Guid id, [FromQuery] string scope)
        {
            var names = await _workspaceService.GetEligibleRoleNamesAsync(scope);
            return Ok(ApiResponse<List<string>>.Ok(names));
        }

        [HttpPost("{id}/members/{userId}/roles")]
        public async Task<ActionResult<ApiResponse<object>>> AddMemberRole(Guid id, Guid userId, [FromBody] MemberRoleRequest request)
        {
            var callerUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            await _workspaceService.AddMemberRoleAsync(id, userId, callerUserId, request.Role);

            return Ok(ApiResponse<object>.Ok(new { userId, role = request.Role }));
        }

        [HttpDelete("{id}/members/{userId}/roles/{roleName}")]
        public async Task<IActionResult> RemoveMemberRole(Guid id, Guid userId, string roleName)
        {
            var callerUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            await _workspaceService.RemoveMemberRoleAsync(id, userId, callerUserId, roleName);

            return NoContent();
        }

        [HttpDelete("{id}/members/{userId}")]
        public async Task<IActionResult> RemoveMember(Guid id, Guid userId)
        {
            var callerUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            await _workspaceService.RemoveMemberAsync(id, userId, callerUserId);

            return NoContent();
        }

        [HttpGet("{id}/letterhead")]
        public async Task<ActionResult<ApiResponse<LetterheadResponse>>> GetLetterhead(Guid id)
        {
            var callerUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var letterhead = await _workspaceService.GetLetterheadAsync(id, callerUserId);
            return Ok(ApiResponse<LetterheadResponse>.Ok(ToResponse(letterhead)));
        }

        [HttpPut("{id}/letterhead")]
        public async Task<ActionResult<ApiResponse<LetterheadResponse>>> UpdateLetterhead(Guid id, [FromBody] LetterheadRequest request)
        {
            var callerUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var letterhead = await _workspaceService.UpdateLetterheadAsync(id, callerUserId, request);
            return Ok(ApiResponse<LetterheadResponse>.Ok(ToResponse(letterhead)));
        }

        [HttpPost("{id}/letterhead/logo")]
        public async Task<ActionResult<ApiResponse<LetterheadResponse>>> UploadLetterheadLogo(Guid id, IFormFile file)
        {
            var callerUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var letterhead = await _workspaceService.UploadLetterheadLogoAsync(id, callerUserId, file);
            return Ok(ApiResponse<LetterheadResponse>.Ok(ToResponse(letterhead)));
        }

        [HttpDelete("{id}/letterhead/logo")]
        public async Task<ActionResult<ApiResponse<LetterheadResponse>>> DeleteLetterheadLogo(Guid id)
        {
            var callerUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var letterhead = await _workspaceService.DeleteLetterheadLogoAsync(id, callerUserId);
            return Ok(ApiResponse<LetterheadResponse>.Ok(ToResponse(letterhead)));
        }

        [HttpGet("{id}/letterhead/logo")]
        public async Task<IActionResult> GetLetterheadLogo(Guid id)
        {
            var callerUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var (content, path) = await _workspaceService.GetLetterheadLogoFileAsync(id, callerUserId);
            var contentType = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                _ => "application/octet-stream"
            };
            return File(content, contentType);
        }

        private static LetterheadResponse ToResponse(WorkspaceLetterhead l) => new()
        {
            CompanyName = l.CompanyName,
            Address = l.Address,
            Phone = l.Phone,
            Email = l.Email,
            RegistrationNumber = l.RegistrationNumber,
            HasLogo = l.HasLogo
        };

        private static WorkspaceResponse ToResponse(WorkspaceWithAccess w) => new()
        {
            WorkspaceId = w.Workspace.Id,
            Name = w.Workspace.Name,
            Description = w.Workspace.Description,
            CreatedAt = w.Workspace.CreatedAt,
            IsActive = w.Workspace.IsActive,
            Tier = w.Tier,
            Roles = w.Roles
        };
    }
}
