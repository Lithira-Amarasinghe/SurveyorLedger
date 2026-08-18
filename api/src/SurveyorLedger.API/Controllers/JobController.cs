using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Invitation;
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
        private readonly IScopedAccessService _access;
        private readonly IInvitationService _invitationService;
        private readonly ILogger<JobController> _logger;

        public JobController(IJobService jobService, IScopedAccessService access, IInvitationService invitationService, ILogger<JobController> logger)
        {
            _jobService = jobService;
            _access = access;
            _invitationService = invitationService;
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
            var response = ToResponse(job);
            response.CanManageParticipants = await _access.CanAccessJobAsync(callerId, workspaceId, id, "manage_participants");
            response.CanViewBudget = await _access.CanAsync(callerId, "budget", "view", workspaceId);
            response.CanEditBudget = await _access.CanAsync(callerId, "budget", "create", workspaceId)
                || await _access.CanAsync(callerId, "budget", "edit", workspaceId);
            return Ok(ApiResponse<JobResponse>.Ok(response));
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

        /// <summary>Direct participants plus anyone with blanket job.view_all access from an ancestor scope (e.g. Admin) - read-only, tagged AccessType so the UI can tell the two apart.</summary>
        [HttpGet("{id}/effective-participants")]
        public async Task<ActionResult<ApiResponse<List<JobParticipantResponse>>>> GetEffectiveParticipants(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var participants = await _jobService.GetEffectiveParticipantsAsync(workspaceId, callerId, id);
            return Ok(ApiResponse<List<JobParticipantResponse>>.Ok(participants.Select(ToEffectiveResponse).ToList()));
        }

        /// <summary>Pending invitations that resolve to this job (plain job invite, or a chained workspace invite carrying this job as descendant) - lets the job's people table show a row for someone invited but not yet accepted.</summary>
        [HttpGet("{id}/pending-invitations")]
        public async Task<ActionResult<ApiResponse<List<InvitationResponse>>>> GetPendingInvitations(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var invitations = await _invitationService.GetPendingInvitationsForJobAsync(workspaceId, callerId, id);
            return Ok(ApiResponse<List<InvitationResponse>>.Ok(invitations.Select(ToResponse).ToList()));
        }

        [HttpPost("{id}/participants/{userId}")]
        public async Task<ActionResult<ApiResponse<AddParticipantResponse>>> AddParticipant(Guid workspaceId, Guid id, Guid userId, [FromBody] AddParticipantRequest request)
        {
            var callerId = CallerId();
            var result = await _jobService.AddParticipantAsync(workspaceId, callerId, id, userId, request.Role);
            return Ok(ApiResponse<AddParticipantResponse>.Ok(ToResponse(result)));
        }

        /// <summary>For someone typed by email in the "not found" fallback - always creates an invite, never an instant grant.</summary>
        [HttpPost("{id}/participants/invite")]
        public async Task<ActionResult<ApiResponse<AddParticipantResponse>>> InviteParticipant(Guid workspaceId, Guid id, [FromBody] InviteParticipantRequest request)
        {
            var callerId = CallerId();
            var invitation = await _jobService.InviteParticipantByEmailAsync(
                workspaceId, callerId, id, request.Role, request.Email, request.FirstName, request.LastName, request.Phone, request.Address);
            return Ok(ApiResponse<AddParticipantResponse>.Ok(new AddParticipantResponse { Status = "invited", Invitation = ToResponse(invitation) }));
        }

        [HttpDelete("{id}/participants/{userId}/roles/{role}")]
        public async Task<IActionResult> RemoveParticipant(Guid workspaceId, Guid id, Guid userId, string role)
        {
            var callerId = CallerId();
            await _jobService.RemoveParticipantAsync(workspaceId, callerId, id, userId, role);
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
            PersonId = p.User.PersonId,
            FirstName = p.User.Person.FirstName,
            LastName = p.User.Person.LastName,
            Email = p.User.Person.Email,
            Role = p.Role.Name,
            AssignedAt = p.AssignedAt
        };

        private static JobParticipantResponse ToEffectiveResponse(UserAccess p)
        {
            var response = ToResponse(p);
            response.AccessType = p.ScopeType == SurveyorLedger.Core.Constants.ScopeTypes.Job ? "Direct" : "WorkspaceWide";
            return response;
        }

        private static AddParticipantResponse ToResponse(ParticipantAddResult result) => result.Access != null
            ? new AddParticipantResponse { Status = "added", Participant = ToResponse(result.Access) }
            : new AddParticipantResponse { Status = "invited", Invitation = ToResponse(result.Invitation!) };

        private static InvitationResponse ToResponse(Invitation i) => new()
        {
            InvitationId = i.Id,
            Email = i.Email,
            Role = i.Role.Name,
            ExpiresAt = i.ExpiresAt,
            Status = i.Status
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
