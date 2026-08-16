using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Invitation;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api")]
    public class InvitationController : ControllerBase
    {
        private readonly IInvitationService _invitationService;
        private readonly ApplicationDbContext _context;

        public InvitationController(IInvitationService invitationService, ApplicationDbContext context)
        {
            _invitationService = invitationService;
            _context = context;
        }

        [Authorize]
        [HttpPost("workspace/{workspaceId}/invitations")]
        public async Task<ActionResult<ApiResponse<InvitationResponse>>> CreateInvitation(Guid workspaceId, [FromBody] InvitationRequest request)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var invitation = await _invitationService.CreateInvitationAsync(workspaceId, userId, request);
            invitation = await WithRoleAsync(invitation);

            return Ok(ApiResponse<InvitationResponse>.Ok(ToResponse(invitation)));
        }

        [Authorize]
        [HttpGet("workspace/{workspaceId}/invitations")]
        public async Task<ActionResult<ApiResponse<List<InvitationListItemResponse>>>> ListInvitations(Guid workspaceId)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var invitations = await _invitationService.GetPendingInvitationsAsync(workspaceId, userId);

            var response = invitations.Select(i => new InvitationListItemResponse
            {
                InvitationId = i.Id,
                Email = i.Email,
                Role = i.Role.Name,
                Status = i.Status,
                ExpiresAt = i.ExpiresAt,
                InvitedByName = $"{i.InvitedByUser.Person.FirstName} {i.InvitedByUser.Person.LastName}",
                CreatedAt = i.CreatedAt,
                EmailFailed = i.EmailFailed
            }).ToList();

            return Ok(ApiResponse<List<InvitationListItemResponse>>.Ok(response));
        }

        [Authorize]
        [HttpGet("invitations/mine")]
        public async Task<ActionResult<ApiResponse<List<MyInvitationResponse>>>> ListMyInvitations()
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var invitations = await _invitationService.GetMyInvitationsAsync(userId);

            // Workspace-scope invites: ScopeId is the workspace itself.
            var workspaceScopeIds = invitations.Where(i => i.ScopeType == Constants.ScopeTypes.Workspace).Select(i => i.ScopeId).Distinct().ToList();
            var workspaceNames = await _context.Workspaces
                .Where(w => workspaceScopeIds.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => w.Name);

            // Job-scope invites: ScopeId is a job, resolve Job -> its real Workspace + a label.
            var jobScopeIds = invitations.Where(i => i.ScopeType == Constants.ScopeTypes.Job).Select(i => i.ScopeId).Distinct().ToList();
            var jobs = await _context.Jobs
                .Where(j => jobScopeIds.Contains(j.Id))
                .ToDictionaryAsync(j => j.Id, j => j);
            var jobWorkspaceIds = jobs.Values.Select(j => j.WorkspaceId).Distinct().ToList();
            var jobWorkspaceNames = await _context.Workspaces
                .Where(w => jobWorkspaceIds.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => w.Name);

            var userIds = invitations.Select(i => i.UserId).Distinct().ToList();
            var hasLoginByUser = await _context.UserAccounts
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.HasCompletedSignup);

            var response = invitations.Select(i =>
            {
                string workspaceName;
                string? jobLabel = null;
                if (i.ScopeType == Constants.ScopeTypes.Workspace)
                {
                    workspaceName = workspaceNames.GetValueOrDefault(i.ScopeId, "Unknown workspace");
                }
                else
                {
                    var job = jobs.GetValueOrDefault(i.ScopeId);
                    workspaceName = job != null ? jobWorkspaceNames.GetValueOrDefault(job.WorkspaceId, "Unknown workspace") : "Unknown workspace";
                    jobLabel = job != null ? $"{job.JobNumber} · {job.Title}" : null;
                }

                return new MyInvitationResponse
                {
                    InvitationId = i.Id,
                    WorkspaceName = workspaceName,
                    Role = i.Role.Name,
                    Status = i.Status,
                    ExpiresAt = i.ExpiresAt,
                    CreatedAt = i.CreatedAt,
                    HasLogin = hasLoginByUser.GetValueOrDefault(i.UserId, false),
                    JobLabel = jobLabel
                };
            }).ToList();

            return Ok(ApiResponse<List<MyInvitationResponse>>.Ok(response));
        }

        [Authorize]
        [HttpDelete("workspace/{workspaceId}/invitations/{invitationId}")]
        public async Task<IActionResult> RevokeInvitation(Guid workspaceId, Guid invitationId)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            await _invitationService.RevokeInvitationAsync(workspaceId, invitationId, userId);

            return NoContent();
        }

        [Authorize]
        [HttpPost("workspace/{workspaceId}/invitations/{invitationId}/resend")]
        public async Task<IActionResult> ResendInvitation(Guid workspaceId, Guid invitationId)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            await _invitationService.ResendInvitationAsync(workspaceId, invitationId, userId);

            return NoContent();
        }

        [HttpGet("invitations/{token}")]
        public async Task<ActionResult<ApiResponse<InvitationPreviewResponse>>> GetInvitationByToken(string token)
        {
            var invitation = await _invitationService.GetByTokenAsync(token);
            var (_, workspaceName, jobLabel) = await ResolveScopeAsync(invitation);
            var hasLogin = await _context.UserAccounts.Where(u => u.Id == invitation.UserId).Select(u => u.HasCompletedSignup).FirstOrDefaultAsync();

            var expired = invitation.Status != "Pending" || invitation.ExpiresAt <= DateTime.UtcNow;
            var response = new InvitationPreviewResponse
            {
                InvitationId = invitation.Id,
                Email = invitation.Email,
                WorkspaceName = workspaceName,
                Role = invitation.Role.Name,
                Expired = expired,
                HasLogin = hasLogin,
                JobLabel = jobLabel
            };

            return Ok(ApiResponse<InvitationPreviewResponse>.Ok(response));
        }

        [HttpPost("invitations/{token}/complete")]
        public async Task<ActionResult<ApiResponse<object>>> CompleteInvitation(string token, [FromBody] CompleteInvitationRequest request)
        {
            await _invitationService.CompleteInvitationAsync(token, request);
            return Ok(ApiResponse<object>.Ok(new { message = "Account set up. Please log in to continue." }));
        }

        /// <summary>
        /// Decline by token, no login required - a brand-new invitee has no password yet
        /// and so no way to reach the authenticated decline below.
        /// </summary>
        [HttpPost("invitations/{token}/decline-by-token")]
        public async Task<IActionResult> DeclineInvitationByToken(string token)
        {
            await _invitationService.DeclineByTokenAsync(token);
            return NoContent();
        }

        [Authorize]
        [HttpPost("invitations/{id}/accept")]
        public async Task<ActionResult<ApiResponse<AcceptInvitationResponse>>> AcceptInvitation(Guid id)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var invitation = await _invitationService.AcceptInvitationAsync(id, userId);
            invitation = await WithRoleAsync(invitation);
            var (workspaceId, _, _) = await ResolveScopeAsync(invitation);

            return Ok(ApiResponse<AcceptInvitationResponse>.Ok(new AcceptInvitationResponse
            {
                WorkspaceId = workspaceId,
                Role = invitation.Role.Name,
                JobId = invitation.ScopeType == Constants.ScopeTypes.Job ? invitation.ScopeId : null
            }));
        }

        [Authorize]
        [HttpPost("invitations/{id}/decline")]
        public async Task<IActionResult> DeclineInvitation(Guid id)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            await _invitationService.DeclineInvitationAsync(id, userId);

            return NoContent();
        }

        private async Task<Invitation> WithRoleAsync(Invitation invitation)
        {
            if (invitation.Role == null)
                invitation.Role = await _context.Roles.FirstAsync(r => r.Id == invitation.RoleId);
            return invitation;
        }

        /// <summary>
        /// Resolves the real Workspace for an invitation regardless of scope - a Job-scope
        /// invite's ScopeId is the job, not the workspace, so this walks Job -> WorkspaceId.
        /// </summary>
        private async Task<(Guid workspaceId, string workspaceName, string? jobLabel)> ResolveScopeAsync(Invitation invitation)
        {
            if (invitation.ScopeType == Constants.ScopeTypes.Workspace)
            {
                var ws = await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == invitation.ScopeId);
                return (invitation.ScopeId, ws?.Name ?? "Unknown workspace", null);
            }

            var job = await _context.Jobs.FirstOrDefaultAsync(j => j.Id == invitation.ScopeId);
            var workspace = job == null ? null : await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == job.WorkspaceId);
            return (job?.WorkspaceId ?? Guid.Empty, workspace?.Name ?? "Unknown workspace",
                job == null ? null : $"{job.JobNumber} · {job.Title}");
        }

        private static InvitationResponse ToResponse(Invitation i) => new()
        {
            InvitationId = i.Id,
            Email = i.Email,
            Role = i.Role.Name,
            ExpiresAt = i.ExpiresAt,
            Status = i.Status
        };
    }
}
