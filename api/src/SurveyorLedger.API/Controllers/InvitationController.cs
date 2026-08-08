using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Invitation;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api")]
    public class InvitationController : ControllerBase
    {
        private readonly IInvitationService _invitationService;

        public InvitationController(IInvitationService invitationService)
        {
            _invitationService = invitationService;
        }

        [Authorize]
        [HttpPost("workspace/{workspaceId}/invitations")]
        public async Task<ActionResult<ApiResponse<InvitationResponse>>> CreateInvitation(Guid workspaceId, [FromBody] InvitationRequest request)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var invitation = await _invitationService.CreateInvitationAsync(workspaceId, userId, request);

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
                Role = i.Role,
                ExpiresAt = i.ExpiresAt,
                InvitedByName = $"{i.InvitedByUser.FirstName} {i.InvitedByUser.LastName}",
                CreatedAt = i.CreatedAt,
                EmailFailed = i.EmailFailed
            }).ToList();

            return Ok(ApiResponse<List<InvitationListItemResponse>>.Ok(response));
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

            var expired = invitation.Status != "Pending" || invitation.ExpiresAt <= DateTime.UtcNow;
            var response = new InvitationPreviewResponse
            {
                Email = invitation.Email,
                WorkspaceName = invitation.Workspace.Name,
                Role = invitation.Role,
                Expired = expired
            };

            return Ok(ApiResponse<InvitationPreviewResponse>.Ok(response));
        }

        [HttpPost("invitations/{token}/register")]
        public async Task<ActionResult<ApiResponse<object>>> RegisterFromInvitation(string token, [FromBody] RegisterFromInvitationRequest request)
        {
            await _invitationService.RegisterFromInvitationAsync(token, request);
            return Ok(ApiResponse<object>.Ok(new { message = "Account created. Please log in to continue." }));
        }

        [Authorize]
        [HttpPost("invitations/{token}/accept")]
        public async Task<ActionResult<ApiResponse<AcceptInvitationResponse>>> AcceptInvitation(string token)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var email = User.FindFirst(ClaimTypes.Email)?.Value!;

            var (workspaceId, role) = await _invitationService.AcceptInvitationAsync(token, userId, email);

            return Ok(ApiResponse<AcceptInvitationResponse>.Ok(new AcceptInvitationResponse
            {
                WorkspaceId = workspaceId,
                Role = role
            }));
        }

        private static InvitationResponse ToResponse(Invitation i) => new()
        {
            InvitationId = i.Id,
            Email = i.Email,
            Role = i.Role,
            ExpiresAt = i.ExpiresAt,
            Status = i.Status
        };
    }
}
