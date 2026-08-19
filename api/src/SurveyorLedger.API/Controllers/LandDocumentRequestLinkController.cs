using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.LandDocumentRequest;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;

namespace SurveyorLedger.API.Controllers
{
    /// <summary>
    /// Deliberately separate from LandDocumentRequestController: every action here is
    /// unauthenticated by design (the token is the only credential), mirroring
    /// DocumentRequestLinkController's split.
    /// </summary>
    [ApiController]
    [Route("api/land-document-request-links")]
    [EnableRateLimiting("auth")]
    public class LandDocumentRequestLinkController : ControllerBase
    {
        private readonly ILandDocumentRequestService _requestService;
        private readonly ApplicationDbContext _context;

        public LandDocumentRequestLinkController(ILandDocumentRequestService requestService, ApplicationDbContext context)
        {
            _requestService = requestService;
            _context = context;
        }

        [HttpGet("{token}")]
        public async Task<ActionResult<ApiResponse<LandDocumentRequestLinkPreviewResponse>>> Preview(string token)
        {
            var request = await _context.LandDocumentRequests.FirstOrDefaultAsync(r => r.ShareToken == token && r.IsActive);
            if (request == null)
                throw new NotFoundException("Link not found");

            var expired = request.ShareTokenExpiresAt is null || request.ShareTokenExpiresAt <= DateTime.UtcNow;
            if (expired)
                return Ok(ApiResponse<LandDocumentRequestLinkPreviewResponse>.Ok(new LandDocumentRequestLinkPreviewResponse { Expired = true, AlreadyFulfilled = false }));

            var land = await _context.Lands.FirstAsync(l => l.Id == request.LandId);
            var workspace = await _context.Workspaces.FirstAsync(w => w.Id == land.WorkspaceId);

            var addressParts = new[] { land.Address.Village, land.Address.DivisionalSecretariat, land.Address.District }.Where(p => !string.IsNullOrWhiteSpace(p));
            var addressLine = string.Join(", ", addressParts);

            return Ok(ApiResponse<LandDocumentRequestLinkPreviewResponse>.Ok(new LandDocumentRequestLinkPreviewResponse
            {
                Title = request.Title,
                Description = request.Description,
                Category = request.Category,
                WorkspaceName = workspace.Name,
                LandAddressLine = string.IsNullOrEmpty(addressLine) ? "Unnamed land record" : addressLine,
                Expired = false,
                AlreadyFulfilled = request.Status == "Fulfilled"
            }));
        }

        [HttpPost("{token}/upload")]
        [RequestSizeLimit(DocumentService.MaxFileSizeBytes)]
        public async Task<IActionResult> Upload(string token, [FromForm] LandDocumentRequestLinkUploadRequest request)
        {
            await _requestService.UploadViaShareTokenAsync(token, request.Files, request.DisplayFileName);
            return NoContent();
        }
    }
}
