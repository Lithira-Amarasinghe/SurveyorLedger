using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.DocumentRequest;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/job/{jobId}/document-request")]
    [Authorize]
    public class DocumentRequestController : ControllerBase
    {
        private readonly IDocumentRequestService _requestService;

        public DocumentRequestController(IDocumentRequestService requestService)
        {
            _requestService = requestService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<DocumentRequestResponse>>>> List(Guid workspaceId, Guid jobId)
        {
            var requests = await _requestService.GetForJobAsync(workspaceId, CallerId(), jobId);
            return Ok(ApiResponse<List<DocumentRequestResponse>>.Ok(requests.Select(ToResponse).ToList()));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<DocumentRequestResponse>>> Create(Guid workspaceId, Guid jobId, [FromBody] DocumentRequestCreateRequest request)
        {
            var created = await _requestService.CreateAsync(workspaceId, CallerId(), jobId, request.Title, request.Description, request.Category, request.TargetRole, request.TargetUserId);
            return Ok(ApiResponse<DocumentRequestResponse>.Ok(ToResponse(created)));
        }

        [HttpPost("{id}/fulfill")]
        [RequestSizeLimit(DocumentService.MaxFileSizeBytes)]
        public async Task<ActionResult<ApiResponse<DocumentRequestResponse>>> Fulfill(Guid workspaceId, Guid jobId, Guid id, [FromForm] DocumentRequestFulfillRequest request)
        {
            var fulfilled = await _requestService.FulfillAsync(workspaceId, CallerId(), jobId, id, request.Files, request.BatchId, request.Visibility, request.DisplayFileName);
            return Ok(ApiResponse<DocumentRequestResponse>.Ok(ToResponse(fulfilled)));
        }

        [HttpPost("{id}/reopen")]
        public async Task<ActionResult<ApiResponse<DocumentRequestResponse>>> Reopen(Guid workspaceId, Guid jobId, Guid id, [FromBody] DocumentRequestReopenRequest? request)
        {
            var reopened = await _requestService.ReopenAsync(workspaceId, CallerId(), jobId, id, request?.Note);
            return Ok(ApiResponse<DocumentRequestResponse>.Ok(ToResponse(reopened)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Cancel(Guid workspaceId, Guid jobId, Guid id)
        {
            await _requestService.CancelAsync(workspaceId, CallerId(), jobId, id);
            return NoContent();
        }

        [HttpPatch("{id}/target")]
        public async Task<ActionResult<ApiResponse<DocumentRequestResponse>>> UpdateTarget(Guid workspaceId, Guid jobId, Guid id, [FromBody] DocumentRequestTargetUpdateRequest request)
        {
            var updated = await _requestService.UpdateTargetAsync(workspaceId, CallerId(), jobId, id, request.TargetRole, request.TargetUserId);
            return Ok(ApiResponse<DocumentRequestResponse>.Ok(ToResponse(updated)));
        }

        [HttpPost("{id}/share-link")]
        public async Task<ActionResult<ApiResponse<DocumentRequestShareLinkResponse>>> GenerateShareLink(Guid workspaceId, Guid jobId, Guid id)
        {
            var updated = await _requestService.GenerateShareLinkAsync(workspaceId, CallerId(), jobId, id);
            return Ok(ApiResponse<DocumentRequestShareLinkResponse>.Ok(new DocumentRequestShareLinkResponse
            {
                Token = updated.ShareToken!,
                ExpiresAt = updated.ShareTokenExpiresAt!.Value
            }));
        }

        [HttpDelete("{id}/share-link")]
        public async Task<IActionResult> RevokeShareLink(Guid workspaceId, Guid jobId, Guid id)
        {
            await _requestService.RevokeShareLinkAsync(workspaceId, CallerId(), jobId, id);
            return NoContent();
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        private static DocumentRequestResponse ToResponse(DocumentRequest r) => new()
        {
            RequestId = r.Id,
            JobId = r.JobId,
            Title = r.Title,
            Description = r.Description,
            Category = r.Category,
            TargetRole = r.TargetRole,
            TargetUserId = r.TargetUserId,
            TargetUserName = r.TargetUser != null ? $"{r.TargetUser.FirstName} {r.TargetUser.LastName}" : null,
            HasActiveShareLink = r.ShareToken != null && r.ShareTokenExpiresAt > DateTime.UtcNow,
            Status = r.Status,
            FulfilledBatchId = r.FulfilledBatchId,
            FulfilledAt = r.FulfilledAt,
            FulfilledBy = r.FulfilledBy,
            RequestedBy = r.RequestedBy,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        };
    }
}
