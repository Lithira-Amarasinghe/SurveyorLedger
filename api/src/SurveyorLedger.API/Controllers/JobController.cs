using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/job")]
    [Authorize]
    public class JobController : ControllerBase
    {
        private readonly IJobService _jobService;
        private readonly ILogger<JobController> _logger;

        public JobController(IJobService jobService, ILogger<JobController> logger)
        {
            _jobService = jobService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<JobResponse>>>> List(Guid workspaceId)
        {
            var callerId = CallerId();
            var jobs = await _jobService.GetJobsAsync(workspaceId, callerId);
            return Ok(ApiResponse<List<JobResponse>>.Ok(jobs.Select(ToResponse).ToList()));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<JobResponse>>> Create(Guid workspaceId, [FromBody] JobRequest request)
        {
            var callerId = CallerId();
            var job = await _jobService.CreateAsync(workspaceId, callerId, request);
            return CreatedAtAction(nameof(GetById), new { workspaceId, id = job.Id }, ApiResponse<JobResponse>.Ok(ToResponse(job)));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<JobResponse>>> GetById(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var job = await _jobService.GetByIdAsync(workspaceId, callerId, id);
            return Ok(ApiResponse<JobResponse>.Ok(ToResponse(job)));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<JobResponse>>> Update(Guid workspaceId, Guid id, [FromBody] JobRequest request)
        {
            var callerId = CallerId();
            var job = await _jobService.UpdateAsync(workspaceId, callerId, id, request);
            return Ok(ApiResponse<JobResponse>.Ok(ToResponse(job)));
        }

        [HttpPut("{id}/status")]
        public async Task<ActionResult<ApiResponse<JobResponse>>> UpdateStatus(Guid workspaceId, Guid id, [FromBody] JobStatusRequest request)
        {
            var callerId = CallerId();
            var job = await _jobService.UpdateStatusAsync(workspaceId, callerId, id, request.Status);
            return Ok(ApiResponse<JobResponse>.Ok(ToResponse(job)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            await _jobService.DeleteAsync(workspaceId, callerId, id);
            return NoContent();
        }

        [HttpGet("{id}/participants")]
        public async Task<ActionResult<ApiResponse<List<JobParticipantResponse>>>> GetParticipants(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var participants = await _jobService.GetParticipantsAsync(workspaceId, callerId, id);
            return Ok(ApiResponse<List<JobParticipantResponse>>.Ok(participants.Select(ToResponse).ToList()));
        }

        [HttpPost("{id}/participants/{userId}")]
        public async Task<ActionResult<ApiResponse<JobParticipantResponse>>> AddParticipant(Guid workspaceId, Guid id, Guid userId)
        {
            var callerId = CallerId();
            var participant = await _jobService.AddParticipantAsync(workspaceId, callerId, id, userId);
            return Ok(ApiResponse<JobParticipantResponse>.Ok(ToResponse(participant)));
        }

        [HttpDelete("{id}/participants/{userId}")]
        public async Task<IActionResult> RemoveParticipant(Guid workspaceId, Guid id, Guid userId)
        {
            var callerId = CallerId();
            await _jobService.RemoveParticipantAsync(workspaceId, callerId, id, userId);
            return NoContent();
        }

        [HttpGet("{id}/lands")]
        public async Task<ActionResult<ApiResponse<List<LandResponse>>>> GetLands(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var lands = await _jobService.GetLandsAsync(workspaceId, callerId, id);
            return Ok(ApiResponse<List<LandResponse>>.Ok(lands.Select(ToResponse).ToList()));
        }

        [HttpPost("{id}/lands/{landId}")]
        public async Task<IActionResult> AddLand(Guid workspaceId, Guid id, Guid landId)
        {
            var callerId = CallerId();
            await _jobService.AddLandAsync(workspaceId, callerId, id, landId);
            return NoContent();
        }

        [HttpDelete("{id}/lands/{landId}")]
        public async Task<IActionResult> RemoveLand(Guid workspaceId, Guid id, Guid landId)
        {
            var callerId = CallerId();
            await _jobService.RemoveLandAsync(workspaceId, callerId, id, landId);
            return NoContent();
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        private static JobResponse ToResponse(Job j) => new()
        {
            JobId = j.Id,
            JobNumber = j.JobNumber,
            Title = j.Title,
            Description = j.Description,
            Status = j.Status,
            CreatedBy = j.CreatedBy,
            CreatedAt = j.CreatedAt,
            UpdatedAt = j.UpdatedAt
        };

        private static JobParticipantResponse ToResponse(UserAccess p) => new()
        {
            UserId = p.UserId,
            FirstName = p.User.FirstName,
            LastName = p.User.LastName,
            Email = p.User.Email,
            Role = p.Role.Name,
            AssignedAt = p.AssignedAt
        };

        private static LandResponse ToResponse(Land l) => new()
        {
            LandId = l.Id,
            Address = new AddressDto
            {
                Street = l.Address.Street,
                City = l.Address.City,
                District = l.Address.District,
                PostalCode = l.Address.PostalCode,
                Country = l.Address.Country
            },
            Size = l.Size,
            SizeUnit = l.SizeUnit,
            GpsCoordinates = l.GpsCoordinates,
            Notes = l.Notes,
            CreatedAt = l.CreatedAt,
            UpdatedAt = l.UpdatedAt
        };
    }
}
