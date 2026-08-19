using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Document;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/job/{jobId}/document")]
    [Authorize]
    public class DocumentController : ControllerBase
    {
        private readonly IDocumentService _documentService;

        public DocumentController(IDocumentService documentService)
        {
            _documentService = documentService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<DocumentResponse>>>> List(Guid workspaceId, Guid jobId)
        {
            var documents = await _documentService.GetDocumentsAsync(workspaceId, CallerId(), jobId);
            return Ok(ApiResponse<List<DocumentResponse>>.Ok(documents.Select(ToResponse).ToList()));
        }

        [HttpPost]
        [RequestSizeLimit(DocumentService.MaxFileSizeBytes)]
        public async Task<ActionResult<ApiResponse<DocumentResponse>>> Upload(Guid workspaceId, Guid jobId, [FromForm] DocumentUploadRequest request)
        {
            var document = await _documentService.UploadAsync(workspaceId, CallerId(), jobId, request.File, request.Category, request.Visibility, request.DisplayFileName, request.BatchId);
            return Ok(ApiResponse<DocumentResponse>.Ok(ToResponse(document)));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(Guid workspaceId, Guid jobId, Guid id, [FromQuery] bool download = false)
        {
            var (document, content) = await _documentService.GetFileAsync(workspaceId, CallerId(), jobId, id);
            Response.Headers.ContentDisposition = download
                ? $"attachment; filename=\"{document.FileName}\""
                : $"inline; filename=\"{document.FileName}\"";
            return File(content, document.ContentType);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid workspaceId, Guid jobId, Guid id)
        {
            await _documentService.DeleteAsync(workspaceId, CallerId(), jobId, id);
            return NoContent();
        }

        [HttpPatch("{id}/visibility")]
        public async Task<ActionResult<ApiResponse<DocumentResponse>>> UpdateVisibility(Guid workspaceId, Guid jobId, Guid id, [FromBody] DocumentVisibilityUpdateRequest request)
        {
            var document = await _documentService.UpdateVisibilityAsync(workspaceId, CallerId(), jobId, id, request.Visibility);
            return Ok(ApiResponse<DocumentResponse>.Ok(ToResponse(document)));
        }

        [HttpPatch("{id}")]
        public async Task<ActionResult<ApiResponse<DocumentResponse>>> Rename(Guid workspaceId, Guid jobId, Guid id, [FromBody] RenameDocumentRequest request)
        {
            var document = await _documentService.RenameAsync(workspaceId, CallerId(), jobId, id, request.FileName);
            return Ok(ApiResponse<DocumentResponse>.Ok(ToResponse(document)));
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        private static DocumentResponse ToResponse(Document d) => new()
        {
            DocumentId = d.Id,
            JobId = d.JobId!.Value,
            FileName = d.FileName,
            ContentType = d.ContentType,
            FileSizeBytes = d.FileSizeBytes,
            Category = d.Category,
            Visibility = d.Visibility,
            UploadedBy = d.UploadedBy,
            UploadedByName = $"{d.UploadedByUser.FirstName} {d.UploadedByUser.LastName}",
            CreatedAt = d.CreatedAt,
            UpdatedAt = d.UpdatedAt,
            UploadBatchId = d.UploadBatchId
        };
    }
}
