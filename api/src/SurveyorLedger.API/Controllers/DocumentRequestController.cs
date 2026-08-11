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
            var created = await _requestService.CreateAsync(workspaceId, CallerId(), jobId, request.Title, request.Description, request.Category);
            return Ok(ApiResponse<DocumentRequestResponse>.Ok(ToResponse(created)));
        }

        [HttpPost("{id}/fulfill")]
        [RequestSizeLimit(DocumentService.MaxFileSizeBytes)]
        public async Task<ActionResult<ApiResponse<DocumentRequestResponse>>> Fulfill(Guid workspaceId, Guid jobId, Guid id, [FromForm] DocumentRequestFulfillRequest request)
        {
            var fulfilled = await _requestService.FulfillAsync(workspaceId, CallerId(), jobId, id, request.File, request.Visibility);
            return Ok(ApiResponse<DocumentRequestResponse>.Ok(ToResponse(fulfilled)));
        }

        [HttpPost("{id}/reopen")]
        public async Task<ActionResult<ApiResponse<DocumentRequestResponse>>> Reopen(Guid workspaceId, Guid jobId, Guid id)
        {
            var reopened = await _requestService.ReopenAsync(workspaceId, CallerId(), jobId, id);
            return Ok(ApiResponse<DocumentRequestResponse>.Ok(ToResponse(reopened)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Cancel(Guid workspaceId, Guid jobId, Guid id)
        {
            await _requestService.CancelAsync(workspaceId, CallerId(), jobId, id);
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
