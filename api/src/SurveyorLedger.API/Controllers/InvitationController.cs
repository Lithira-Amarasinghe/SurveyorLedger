using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Invitation;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
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
                InvitedByName = $"{i.InvitedByUser.FirstName} {i.InvitedByUser.LastName}",
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

            var workspaceIds = invitations.Select(i => i.ScopeId).Distinct().ToList();
            var workspaceNames = await _context.Workspaces
                .Where(w => workspaceIds.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => w.Name);

            var userIds = invitations.Select(i => i.UserId).Distinct().ToList();
            var hasLoginByUser = await _context.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.PasswordHash != null);

            var response = invitations.Select(i => new MyInvitationResponse
            {
                InvitationId = i.Id,
                WorkspaceName = workspaceNames.GetValueOrDefault(i.ScopeId, "Unknown workspace"),
                Role = i.Role.Name,
                Status = i.Status,
                ExpiresAt = i.ExpiresAt,
                CreatedAt = i.CreatedAt,
                HasLogin = hasLoginByUser.GetValueOrDefault(i.UserId, false)
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
            var workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == invitation.ScopeId);
            var hasLogin = await _context.Users.Where(u => u.Id == invitation.UserId).Select(u => u.PasswordHash != null).FirstOrDefaultAsync();

            var expired = invitation.Status != "Pending" || invitation.ExpiresAt <= DateTime.UtcNow;
            var response = new InvitationPreviewResponse
            {
                InvitationId = invitation.Id,
                Email = invitation.Email,
                WorkspaceName = workspace?.Name ?? "Unknown workspace",
                Role = invitation.Role.Name,
                Expired = expired,
                HasLogin = hasLogin
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

            return Ok(ApiResponse<AcceptInvitationResponse>.Ok(new AcceptInvitationResponse
            {
                WorkspaceId = invitation.ScopeId,
                Role = invitation.Role.Name
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
