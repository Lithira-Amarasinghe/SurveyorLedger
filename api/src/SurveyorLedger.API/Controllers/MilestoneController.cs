using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Milestone;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/job/{jobId}/milestone")]
    [Authorize]
    public class MilestoneController : ControllerBase
    {
        private readonly IMilestoneService _milestoneService;

        public MilestoneController(IMilestoneService milestoneService)
        {
            _milestoneService = milestoneService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<MilestoneResponse>>>> List(Guid workspaceId, Guid jobId)
        {
            var milestones = await _milestoneService.GetMilestonesAsync(workspaceId, CallerId(), jobId);
            return Ok(ApiResponse<List<MilestoneResponse>>.Ok(milestones.Select(ToResponse).ToList()));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<MilestoneResponse>>> GetById(Guid workspaceId, Guid jobId, Guid id)
        {
            var milestone = await _milestoneService.GetByIdAsync(workspaceId, CallerId(), jobId, id);
            return Ok(ApiResponse<MilestoneResponse>.Ok(ToResponse(milestone)));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<MilestoneResponse>>> Create(Guid workspaceId, Guid jobId, [FromBody] MilestoneRequest request)
        {
            var milestone = await _milestoneService.CreateAsync(workspaceId, CallerId(), jobId, request);
            return CreatedAtAction(nameof(GetById), new { workspaceId, jobId, id = milestone.Id }, ApiResponse<MilestoneResponse>.Ok(ToResponse(milestone)));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<MilestoneResponse>>> Update(Guid workspaceId, Guid jobId, Guid id, [FromBody] MilestoneRequest request)
        {
            var milestone = await _milestoneService.UpdateAsync(workspaceId, CallerId(), jobId, id, request);
            return Ok(ApiResponse<MilestoneResponse>.Ok(ToResponse(milestone)));
        }

        [HttpPut("{id}/status")]
        public async Task<ActionResult<ApiResponse<MilestoneResponse>>> UpdateStatus(Guid workspaceId, Guid jobId, Guid id, [FromBody] MilestoneStatusRequest request)
        {
            var milestone = await _milestoneService.UpdateStatusAsync(workspaceId, CallerId(), jobId, id, request.Status);
            return Ok(ApiResponse<MilestoneResponse>.Ok(ToResponse(milestone)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid workspaceId, Guid jobId, Guid id)
        {
            await _milestoneService.DeleteAsync(workspaceId, CallerId(), jobId, id);
            return NoContent();
        }

        [HttpPut("reorder")]
        public async Task<ActionResult<ApiResponse<List<MilestoneResponse>>>> Reorder(Guid workspaceId, Guid jobId, [FromBody] MilestoneReorderRequest request)
        {
            var milestones = await _milestoneService.ReorderAsync(workspaceId, CallerId(), jobId, request.MilestoneIds);
            return Ok(ApiResponse<List<MilestoneResponse>>.Ok(milestones.Select(ToResponse).ToList()));
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        private static MilestoneResponse ToResponse(Milestone m) => new()
        {
            MilestoneId = m.Id,
            JobId = m.JobId,
            Title = m.Title,
            Description = m.Description,
            DueDate = m.DueDate,
            Amount = m.Amount,
            Status = m.Status,
            SortOrder = m.SortOrder,
            CompletedAt = m.CompletedAt,
            CompletedBy = m.CompletedBy,
            CreatedBy = m.CreatedBy,
            CreatedAt = m.CreatedAt,
            UpdatedAt = m.UpdatedAt
        };
    }
}
