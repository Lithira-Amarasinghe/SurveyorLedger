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

            var response = workspaces.Select(w => new WorkspaceResponse
            {
                WorkspaceId = w.Id,
                Name = w.Name,
                Description = w.Description,
                CreatedAt = w.CreatedAt,
                IsActive = w.IsActive
            }).ToList();

            return Ok(ApiResponse<List<WorkspaceResponse>>.Ok(response));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<WorkspaceResponse>>> CreateWorkspace([FromBody] WorkspaceRequest request)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var workspace = await _workspaceService.CreateWorkspaceAsync(userId, request);

            return CreatedAtAction(nameof(GetWorkspaceById), new { id = workspace.Id },
                ApiResponse<WorkspaceResponse>.Ok(new WorkspaceResponse
                {
                    WorkspaceId = workspace.Id,
                    Name = workspace.Name,
                    Description = workspace.Description,
                    CreatedAt = workspace.CreatedAt,
                    IsActive = workspace.IsActive
                }));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<WorkspaceResponse>>> GetWorkspaceById(Guid id)
        {
            var workspace = await _workspaceService.GetWorkspaceByIdAsync(id);
            if (workspace == null)
                return NotFound(ApiResponse<object>.Fail("Workspace not found"));

            return Ok(ApiResponse<WorkspaceResponse>.Ok(new WorkspaceResponse
            {
                WorkspaceId = workspace.Id,
                Name = workspace.Name,
                Description = workspace.Description,
                CreatedAt = workspace.CreatedAt,
                IsActive = workspace.IsActive
            }));
        }
    }
}
