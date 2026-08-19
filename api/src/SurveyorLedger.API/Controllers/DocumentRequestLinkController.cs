using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.DocumentRequest;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;

namespace SurveyorLedger.API.Controllers
{
    /// <summary>
    /// Deliberately separate from DocumentRequestController: every action here is
    /// unauthenticated by design (the token is the only credential), and keeping that on
    /// its own controller makes the trust boundary visible at a glance rather than mixed
    /// into a controller whose every other action requires [Authorize].
    /// </summary>
    [ApiController]
    [Route("api/document-request-links")]
    [EnableRateLimiting("auth")]
    public class DocumentRequestLinkController : ControllerBase
    {
        private readonly IDocumentRequestService _requestService;
        private readonly ApplicationDbContext _context;

        public DocumentRequestLinkController(IDocumentRequestService requestService, ApplicationDbContext context)
        {
            _requestService = requestService;
            _context = context;
        }

        [HttpGet("{token}")]
        public async Task<ActionResult<ApiResponse<DocumentRequestLinkPreviewResponse>>> Preview(string token)
        {
            var request = await _context.DocumentRequests.FirstOrDefaultAsync(r => r.ShareToken == token && r.IsActive);
            if (request == null)
                throw new NotFoundException("Link not found");

            var expired = request.ShareTokenExpiresAt is null || request.ShareTokenExpiresAt <= DateTime.UtcNow;
            if (expired)
                return Ok(ApiResponse<DocumentRequestLinkPreviewResponse>.Ok(new DocumentRequestLinkPreviewResponse { Expired = true, AlreadyFulfilled = false }));

            var job = await _context.Jobs.FirstAsync(j => j.Id == request.JobId);
            var workspace = await _context.Workspaces.FirstAsync(w => w.Id == job.WorkspaceId);

            return Ok(ApiResponse<DocumentRequestLinkPreviewResponse>.Ok(new DocumentRequestLinkPreviewResponse
            {
                Title = request.Title,
                Description = request.Description,
                Category = request.Category,
                WorkspaceName = workspace.Name,
                JobTitle = job.Title,
                Expired = false,
                AlreadyFulfilled = request.Status == "Fulfilled"
            }));
        }

        [HttpPost("{token}/upload")]
        [RequestSizeLimit(DocumentService.MaxFileSizeBytes)]
        public async Task<IActionResult> Upload(string token, [FromForm] DocumentRequestLinkUploadRequest request)
        {
            await _requestService.UploadViaShareTokenAsync(token, request.Files, request.DisplayFileName);
            return NoContent();
        }
    }
}
