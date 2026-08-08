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
            var workspace = await _workspaceService.CreateWorkspaceAsync(userId, request);

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
                Role = m.Role,
                AssignedAt = m.AssignedAt,
                IsOwner = m.IsOwner
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

        [HttpPut("{id}/members/{userId}")]
        public async Task<ActionResult<ApiResponse<object>>> UpdateMemberRole(Guid id, Guid userId, [FromBody] UpdateMemberRoleRequest request)
        {
            var callerUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var role = await _workspaceService.UpdateMemberRoleAsync(id, userId, callerUserId, request.Role);

            return Ok(ApiResponse<object>.Ok(new { userId, role }));
        }

        [HttpDelete("{id}/members/{userId}")]
        public async Task<IActionResult> RemoveMember(Guid id, Guid userId)
        {
            var callerUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            await _workspaceService.RemoveMemberAsync(id, userId, callerUserId);

            return NoContent();
        }

        private static WorkspaceResponse ToResponse(WorkspaceWithAccess w) => new()
        {
            WorkspaceId = w.Workspace.Id,
            Name = w.Workspace.Name,
            Description = w.Workspace.Description,
            CreatedAt = w.Workspace.CreatedAt,
            IsActive = w.Workspace.IsActive,
            Tier = w.Tier,
            Role = w.Role
        };
    }
}
