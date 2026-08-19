using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.LandDocumentRequest;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/land/{landId}/document-request")]
    [Authorize]
    public class LandDocumentRequestController : ControllerBase
    {
        private readonly ILandDocumentRequestService _requestService;

        public LandDocumentRequestController(ILandDocumentRequestService requestService)
        {
            _requestService = requestService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<LandDocumentRequestResponse>>>> List(Guid workspaceId, Guid landId)
        {
            var requests = await _requestService.GetForLandAsync(workspaceId, CallerId(), landId);
            return Ok(ApiResponse<List<LandDocumentRequestResponse>>.Ok(requests.Select(ToResponse).ToList()));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<LandDocumentRequestResponse>>> Create(Guid workspaceId, Guid landId, [FromBody] LandDocumentRequestCreateRequest request)
        {
            var created = await _requestService.CreateAsync(workspaceId, CallerId(), landId, request.Title, request.Description, request.Category, request.TargetRole);
            return Ok(ApiResponse<LandDocumentRequestResponse>.Ok(ToResponse(created)));
        }

        [HttpPost("{id}/fulfill")]
        [RequestSizeLimit(DocumentService.MaxFileSizeBytes)]
        public async Task<ActionResult<ApiResponse<LandDocumentRequestResponse>>> Fulfill(Guid workspaceId, Guid landId, Guid id, [FromForm] LandDocumentRequestFulfillRequest request)
        {
            var fulfilled = await _requestService.FulfillAsync(workspaceId, CallerId(), landId, id, request.File, request.DisplayFileName);
            return Ok(ApiResponse<LandDocumentRequestResponse>.Ok(ToResponse(fulfilled)));
        }

        [HttpPost("{id}/reopen")]
        public async Task<ActionResult<ApiResponse<LandDocumentRequestResponse>>> Reopen(Guid workspaceId, Guid landId, Guid id, [FromBody] LandDocumentRequestReopenRequest? request)
        {
            var reopened = await _requestService.ReopenAsync(workspaceId, CallerId(), landId, id, request?.Note);
            return Ok(ApiResponse<LandDocumentRequestResponse>.Ok(ToResponse(reopened)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Cancel(Guid workspaceId, Guid landId, Guid id)
        {
            await _requestService.CancelAsync(workspaceId, CallerId(), landId, id);
            return NoContent();
        }

        [HttpPatch("{id}/target")]
        public async Task<ActionResult<ApiResponse<LandDocumentRequestResponse>>> UpdateTarget(Guid workspaceId, Guid landId, Guid id, [FromBody] LandDocumentRequestTargetUpdateRequest request)
        {
            var updated = await _requestService.UpdateTargetAsync(workspaceId, CallerId(), landId, id, request.TargetRole);
            return Ok(ApiResponse<LandDocumentRequestResponse>.Ok(ToResponse(updated)));
        }

        [HttpPost("{id}/share-link")]
        public async Task<ActionResult<ApiResponse<LandDocumentRequestShareLinkResponse>>> GenerateShareLink(Guid workspaceId, Guid landId, Guid id)
        {
            var updated = await _requestService.GenerateShareLinkAsync(workspaceId, CallerId(), landId, id);
            return Ok(ApiResponse<LandDocumentRequestShareLinkResponse>.Ok(new LandDocumentRequestShareLinkResponse
            {
                Token = updated.ShareToken!,
                ExpiresAt = updated.ShareTokenExpiresAt!.Value
            }));
        }

        [HttpDelete("{id}/share-link")]
        public async Task<IActionResult> RevokeShareLink(Guid workspaceId, Guid landId, Guid id)
        {
            await _requestService.RevokeShareLinkAsync(workspaceId, CallerId(), landId, id);
            return NoContent();
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        private static LandDocumentRequestResponse ToResponse(LandDocumentRequest r) => new()
        {
            RequestId = r.Id,
            LandId = r.LandId,
            Title = r.Title,
            Description = r.Description,
            Category = r.Category,
            TargetRole = r.TargetRole,
            HasActiveShareLink = r.ShareToken != null && r.ShareTokenExpiresAt > DateTime.UtcNow,
            Status = r.Status,
            FulfilledDocumentId = r.FulfilledDocumentId,
            FulfilledAt = r.FulfilledAt,
            FulfilledBy = r.FulfilledBy,
            RequestedBy = r.RequestedBy,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        };
    }
}
