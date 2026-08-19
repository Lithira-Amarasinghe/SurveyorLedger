using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Models.Document;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/land")]
    [Authorize]
    public class LandController : ControllerBase
    {
        private readonly ILandService _landService;
        private readonly IDocumentService _documentService;
        private readonly ILogger<LandController> _logger;

        public LandController(ILandService landService, IDocumentService documentService, ILogger<LandController> logger)
        {
            _landService = landService;
            _documentService = documentService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<LandResponse>>>> Search(Guid workspaceId, [FromQuery] string? query)
        {
            var callerId = CallerId();
            var lands = await _landService.SearchAsync(workspaceId, callerId, query);
            return Ok(ApiResponse<List<LandResponse>>.Ok(lands.Select(ToResponse).ToList()));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<LandResponse>>> Create(Guid workspaceId, [FromBody] LandRequest request)
        {
            var callerId = CallerId();
            var land = await _landService.CreateAsync(workspaceId, callerId, request);
            return CreatedAtAction(nameof(GetById), new { workspaceId, id = land.Id }, ApiResponse<LandResponse>.Ok(ToResponse(land)));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<LandResponse>>> GetById(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var land = await _landService.GetByIdAsync(workspaceId, callerId, id);
            return Ok(ApiResponse<LandResponse>.Ok(ToResponse(land)));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<LandResponse>>> Update(Guid workspaceId, Guid id, [FromBody] LandRequest request)
        {
            var callerId = CallerId();
            var land = await _landService.UpdateAsync(workspaceId, callerId, id, request);
            return Ok(ApiResponse<LandResponse>.Ok(ToResponse(land)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            await _landService.DeleteAsync(workspaceId, callerId, id);
            return NoContent();
        }

        [HttpPost("{id}/location-share-link")]
        public async Task<ActionResult<ApiResponse<LandLocationShareLinkResponse>>> GenerateLocationShareLink(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var token = await _landService.GenerateLocationShareLinkAsync(workspaceId, callerId, id);
            return Ok(ApiResponse<LandLocationShareLinkResponse>.Ok(new LandLocationShareLinkResponse { Token = token }));
        }

        [HttpPost("{id}/location-share-link/regenerate")]
        public async Task<ActionResult<ApiResponse<LandLocationShareLinkResponse>>> RegenerateLocationShareLink(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var token = await _landService.RegenerateLocationShareLinkAsync(workspaceId, callerId, id);
            return Ok(ApiResponse<LandLocationShareLinkResponse>.Ok(new LandLocationShareLinkResponse { Token = token }));
        }

        [HttpDelete("{id}/location-share-link")]
        public async Task<IActionResult> RevokeLocationShareLink(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            await _landService.RevokeLocationShareLinkAsync(workspaceId, callerId, id);
            return NoContent();
        }

        [HttpPost("{id}/map-view-share-link")]
        public async Task<ActionResult<ApiResponse<LandLocationShareLinkResponse>>> GenerateMapViewShareLink(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var token = await _landService.GenerateMapViewShareLinkAsync(workspaceId, callerId, id);
            return Ok(ApiResponse<LandLocationShareLinkResponse>.Ok(new LandLocationShareLinkResponse { Token = token }));
        }

        [HttpPost("{id}/map-view-share-link/regenerate")]
        public async Task<ActionResult<ApiResponse<LandLocationShareLinkResponse>>> RegenerateMapViewShareLink(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var token = await _landService.RegenerateMapViewShareLinkAsync(workspaceId, callerId, id);
            return Ok(ApiResponse<LandLocationShareLinkResponse>.Ok(new LandLocationShareLinkResponse { Token = token }));
        }

        [HttpDelete("{id}/map-view-share-link")]
        public async Task<IActionResult> RevokeMapViewShareLink(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            await _landService.RevokeMapViewShareLinkAsync(workspaceId, callerId, id);
            return NoContent();
        }

        [HttpGet("{id}/surveys")]
        public async Task<ActionResult<ApiResponse<List<LandSurveyResponse>>>> GetSurveys(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var surveys = await _landService.GetSurveysAsync(workspaceId, callerId, id);
            return Ok(ApiResponse<List<LandSurveyResponse>>.Ok(surveys.Select(ToResponse).ToList()));
        }

        [HttpPost("{id}/surveys")]
        public async Task<ActionResult<ApiResponse<LandSurveyResponse>>> AddSurvey(Guid workspaceId, Guid id, [FromBody] LandSurveyRequest request)
        {
            var callerId = CallerId();
            var survey = await _landService.AddSurveyAsync(workspaceId, callerId, id, request);
            return Ok(ApiResponse<LandSurveyResponse>.Ok(ToResponse(survey)));
        }

        [HttpPut("{id}/surveys/{surveyId}")]
        public async Task<ActionResult<ApiResponse<LandSurveyResponse>>> UpdateSurvey(Guid workspaceId, Guid id, Guid surveyId, [FromBody] LandSurveyRequest request)
        {
            var callerId = CallerId();
            var survey = await _landService.UpdateSurveyAsync(workspaceId, callerId, id, surveyId, request);
            return Ok(ApiResponse<LandSurveyResponse>.Ok(ToResponse(survey)));
        }

        [HttpDelete("{id}/surveys/{surveyId}")]
        public async Task<IActionResult> DeleteSurvey(Guid workspaceId, Guid id, Guid surveyId)
        {
            var callerId = CallerId();
            await _landService.DeleteSurveyAsync(workspaceId, callerId, id, surveyId);
            return NoContent();
        }

        [HttpGet("{id}/documents")]
        public async Task<ActionResult<ApiResponse<List<OwnedDocumentResponse>>>> GetDocuments(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var documents = await _documentService.GetOwnedDocumentsAsync(workspaceId, callerId, id, "Land", id);
            return Ok(ApiResponse<List<OwnedDocumentResponse>>.Ok(documents.Select(ToOwnedDocumentResponse).ToList()));
        }

        [HttpPost("{id}/documents")]
        [RequestSizeLimit(DocumentService.MaxFileSizeBytes)]
        public async Task<ActionResult<ApiResponse<OwnedDocumentResponse>>> UploadDocument(Guid workspaceId, Guid id, IFormFile file, [FromQuery] DocumentCategory category = DocumentCategory.Other)
        {
            var callerId = CallerId();
            var document = await _documentService.UploadOwnedDocumentAsync(workspaceId, callerId, id, "Land", id, category, file);
            return Ok(ApiResponse<OwnedDocumentResponse>.Ok(ToOwnedDocumentResponse(document)));
        }

        [HttpGet("{id}/documents/{documentId}")]
        public async Task<IActionResult> GetDocumentFile(Guid workspaceId, Guid id, Guid documentId)
        {
            var callerId = CallerId();
            var (document, content) = await _documentService.GetOwnedDocumentFileAsync(workspaceId, callerId, id, "Land", id, documentId);
            return File(content, document.ContentType, document.FileName);
        }

        [HttpDelete("{id}/documents/{documentId}")]
        public async Task<IActionResult> DeleteDocument(Guid workspaceId, Guid id, Guid documentId)
        {
            var callerId = CallerId();
            await _documentService.DeleteOwnedDocumentAsync(workspaceId, callerId, id, "Land", id, documentId);
            return NoContent();
        }

        [HttpPatch("{id}/documents/{documentId}")]
        public async Task<ActionResult<ApiResponse<OwnedDocumentResponse>>> RenameDocument(Guid workspaceId, Guid id, Guid documentId, [FromBody] RenameDocumentRequest request)
        {
            var callerId = CallerId();
            var document = await _documentService.RenameOwnedDocumentAsync(workspaceId, callerId, id, "Land", id, documentId, request.FileName);
            return Ok(ApiResponse<OwnedDocumentResponse>.Ok(ToOwnedDocumentResponse(document)));
        }

        [HttpGet("{id}/surveys/{surveyId}/documents")]
        public async Task<ActionResult<ApiResponse<List<OwnedDocumentResponse>>>> GetSurveyDocuments(Guid workspaceId, Guid id, Guid surveyId)
        {
            var callerId = CallerId();
            var documents = await _documentService.GetOwnedDocumentsAsync(workspaceId, callerId, id, "LandSurvey", surveyId);
            return Ok(ApiResponse<List<OwnedDocumentResponse>>.Ok(documents.Select(ToOwnedDocumentResponse).ToList()));
        }

        [HttpPost("{id}/surveys/{surveyId}/documents")]
        [RequestSizeLimit(DocumentService.MaxFileSizeBytes)]
        public async Task<ActionResult<ApiResponse<OwnedDocumentResponse>>> UploadSurveyDocument(Guid workspaceId, Guid id, Guid surveyId, IFormFile file)
        {
            var callerId = CallerId();
            var document = await _documentService.UploadOwnedDocumentAsync(workspaceId, callerId, id, "LandSurvey", surveyId, DocumentCategory.SurveyPlan, file);
            return Ok(ApiResponse<OwnedDocumentResponse>.Ok(ToOwnedDocumentResponse(document)));
        }

        [HttpGet("{id}/surveys/{surveyId}/documents/{documentId}")]
        public async Task<IActionResult> GetSurveyDocumentFile(Guid workspaceId, Guid id, Guid surveyId, Guid documentId)
        {
            var callerId = CallerId();
            var (document, content) = await _documentService.GetOwnedDocumentFileAsync(workspaceId, callerId, id, "LandSurvey", surveyId, documentId);
            return File(content, document.ContentType, document.FileName);
        }

        [HttpDelete("{id}/surveys/{surveyId}/documents/{documentId}")]
        public async Task<IActionResult> DeleteSurveyDocument(Guid workspaceId, Guid id, Guid surveyId, Guid documentId)
        {
            var callerId = CallerId();
            await _documentService.DeleteOwnedDocumentAsync(workspaceId, callerId, id, "LandSurvey", surveyId, documentId);
            return NoContent();
        }

        [HttpPatch("{id}/surveys/{surveyId}/documents/{documentId}")]
        public async Task<ActionResult<ApiResponse<OwnedDocumentResponse>>> RenameSurveyDocument(Guid workspaceId, Guid id, Guid surveyId, Guid documentId, [FromBody] RenameDocumentRequest request)
        {
            var callerId = CallerId();
            var document = await _documentService.RenameOwnedDocumentAsync(workspaceId, callerId, id, "LandSurvey", surveyId, documentId, request.FileName);
            return Ok(ApiResponse<OwnedDocumentResponse>.Ok(ToOwnedDocumentResponse(document)));
        }

        [HttpGet("{id}/deeds")]
        public async Task<ActionResult<ApiResponse<List<LandDeedResponse>>>> GetDeeds(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var deeds = await _landService.GetDeedsAsync(workspaceId, callerId, id);
            return Ok(ApiResponse<List<LandDeedResponse>>.Ok(deeds.Select(ToResponse).ToList()));
        }

        [HttpPost("{id}/deeds")]
        public async Task<ActionResult<ApiResponse<LandDeedResponse>>> AddDeed(Guid workspaceId, Guid id, [FromBody] LandDeedRequest request)
        {
            var callerId = CallerId();
            var deed = await _landService.AddDeedAsync(workspaceId, callerId, id, request);
            return Ok(ApiResponse<LandDeedResponse>.Ok(ToResponse(deed)));
        }

        [HttpPut("{id}/deeds/{deedId}")]
        public async Task<ActionResult<ApiResponse<LandDeedResponse>>> UpdateDeed(Guid workspaceId, Guid id, Guid deedId, [FromBody] LandDeedRequest request)
        {
            var callerId = CallerId();
            var deed = await _landService.UpdateDeedAsync(workspaceId, callerId, id, deedId, request);
            return Ok(ApiResponse<LandDeedResponse>.Ok(ToResponse(deed)));
        }

        [HttpDelete("{id}/deeds/{deedId}")]
        public async Task<IActionResult> DeleteDeed(Guid workspaceId, Guid id, Guid deedId)
        {
            var callerId = CallerId();
            await _landService.DeleteDeedAsync(workspaceId, callerId, id, deedId);
            return NoContent();
        }

        [HttpGet("{id}/deeds/{deedId}/documents")]
        public async Task<ActionResult<ApiResponse<List<OwnedDocumentResponse>>>> GetDeedDocuments(Guid workspaceId, Guid id, Guid deedId)
        {
            var callerId = CallerId();
            var documents = await _documentService.GetOwnedDocumentsAsync(workspaceId, callerId, id, "LandDeed", deedId);
            return Ok(ApiResponse<List<OwnedDocumentResponse>>.Ok(documents.Select(ToOwnedDocumentResponse).ToList()));
        }

        [HttpPost("{id}/deeds/{deedId}/documents")]
        [RequestSizeLimit(DocumentService.MaxFileSizeBytes)]
        public async Task<ActionResult<ApiResponse<OwnedDocumentResponse>>> UploadDeedDocument(Guid workspaceId, Guid id, Guid deedId, IFormFile file)
        {
            var callerId = CallerId();
            var document = await _documentService.UploadOwnedDocumentAsync(workspaceId, callerId, id, "LandDeed", deedId, DocumentCategory.LegalDocument, file);
            return Ok(ApiResponse<OwnedDocumentResponse>.Ok(ToOwnedDocumentResponse(document)));
        }

        [HttpGet("{id}/deeds/{deedId}/documents/{documentId}")]
        public async Task<IActionResult> GetDeedDocumentFile(Guid workspaceId, Guid id, Guid deedId, Guid documentId)
        {
            var callerId = CallerId();
            var (document, content) = await _documentService.GetOwnedDocumentFileAsync(workspaceId, callerId, id, "LandDeed", deedId, documentId);
            return File(content, document.ContentType, document.FileName);
        }

        [HttpDelete("{id}/deeds/{deedId}/documents/{documentId}")]
        public async Task<IActionResult> DeleteDeedDocument(Guid workspaceId, Guid id, Guid deedId, Guid documentId)
        {
            var callerId = CallerId();
            await _documentService.DeleteOwnedDocumentAsync(workspaceId, callerId, id, "LandDeed", deedId, documentId);
            return NoContent();
        }

        [HttpPatch("{id}/deeds/{deedId}/documents/{documentId}")]
        public async Task<ActionResult<ApiResponse<OwnedDocumentResponse>>> RenameDeedDocument(Guid workspaceId, Guid id, Guid deedId, Guid documentId, [FromBody] RenameDocumentRequest request)
        {
            var callerId = CallerId();
            var document = await _documentService.RenameOwnedDocumentAsync(workspaceId, callerId, id, "LandDeed", deedId, documentId, request.FileName);
            return Ok(ApiResponse<OwnedDocumentResponse>.Ok(ToOwnedDocumentResponse(document)));
        }

        [HttpGet("{id}/boundaries")]
        public async Task<ActionResult<ApiResponse<List<LandBoundaryResponse>>>> GetBoundaries(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var boundaries = await _landService.GetBoundariesAsync(workspaceId, callerId, id);
            return Ok(ApiResponse<List<LandBoundaryResponse>>.Ok(boundaries.Select(ToResponse).ToList()));
        }

        [HttpPost("{id}/boundaries")]
        public async Task<ActionResult<ApiResponse<LandBoundaryResponse>>> AddBoundary(Guid workspaceId, Guid id, [FromBody] LandBoundaryRequest request)
        {
            var callerId = CallerId();
            var boundary = await _landService.AddBoundaryAsync(workspaceId, callerId, id, request);
            return Ok(ApiResponse<LandBoundaryResponse>.Ok(ToResponse(boundary)));
        }

        [HttpPut("{id}/boundaries/{boundaryId}")]
        public async Task<ActionResult<ApiResponse<LandBoundaryResponse>>> UpdateBoundary(Guid workspaceId, Guid id, Guid boundaryId, [FromBody] LandBoundaryRequest request)
        {
            var callerId = CallerId();
            var boundary = await _landService.UpdateBoundaryAsync(workspaceId, callerId, id, boundaryId, request);
            return Ok(ApiResponse<LandBoundaryResponse>.Ok(ToResponse(boundary)));
        }

        [HttpDelete("{id}/boundaries/{boundaryId}")]
        public async Task<IActionResult> DeleteBoundary(Guid workspaceId, Guid id, Guid boundaryId)
        {
            var callerId = CallerId();
            await _landService.DeleteBoundaryAsync(workspaceId, callerId, id, boundaryId);
            return NoContent();
        }

        [HttpGet("{id}/map-points")]
        public async Task<ActionResult<ApiResponse<List<LandMapPointResponse>>>> GetMapPoints(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var points = await _landService.GetMapPointsAsync(workspaceId, callerId, id);
            return Ok(ApiResponse<List<LandMapPointResponse>>.Ok(points.Select(ToResponse).ToList()));
        }

        [HttpPost("{id}/map-points")]
        public async Task<ActionResult<ApiResponse<LandMapPointResponse>>> AddMapPoint(Guid workspaceId, Guid id, [FromBody] LandMapPointRequest request)
        {
            var callerId = CallerId();
            var point = await _landService.AddMapPointAsync(workspaceId, callerId, id, request);
            return Ok(ApiResponse<LandMapPointResponse>.Ok(ToResponse(point)));
        }

        [HttpPut("{id}/map-points/{pointId}")]
        public async Task<ActionResult<ApiResponse<LandMapPointResponse>>> UpdateMapPoint(Guid workspaceId, Guid id, Guid pointId, [FromBody] LandMapPointRequest request)
        {
            var callerId = CallerId();
            var point = await _landService.UpdateMapPointAsync(workspaceId, callerId, id, pointId, request);
            return Ok(ApiResponse<LandMapPointResponse>.Ok(ToResponse(point)));
        }

        [HttpDelete("{id}/map-points/{pointId}")]
        public async Task<IActionResult> DeleteMapPoint(Guid workspaceId, Guid id, Guid pointId)
        {
            var callerId = CallerId();
            await _landService.DeleteMapPointAsync(workspaceId, callerId, id, pointId);
            return NoContent();
        }

        // Photos are Documents (OwnerType="LandPhoto", OwnerId=landId) - same generic infra
        // surveys/deeds/general land docs use. Route shape and LandPhotoResponse are
        // unchanged for the frontend; only the storage moved.
        [HttpGet("{id}/photos")]
        public async Task<ActionResult<ApiResponse<List<LandPhotoResponse>>>> GetPhotos(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var photos = await _documentService.GetOwnedDocumentsAsync(workspaceId, callerId, id, "LandPhoto", id);
            return Ok(ApiResponse<List<LandPhotoResponse>>.Ok(photos.Select(ToPhotoResponse).ToList()));
        }

        [HttpPost("{id}/photos")]
        [RequestSizeLimit(DocumentService.MaxFileSizeBytes)]
        public async Task<ActionResult<ApiResponse<LandPhotoResponse>>> UploadPhoto(Guid workspaceId, Guid id, IFormFile file)
        {
            var callerId = CallerId();
            var photo = await _documentService.UploadOwnedDocumentAsync(workspaceId, callerId, id, "LandPhoto", id, DocumentCategory.Photo, file);
            return Ok(ApiResponse<LandPhotoResponse>.Ok(ToPhotoResponse(photo)));
        }

        [HttpGet("{id}/photos/{photoId}")]
        public async Task<IActionResult> GetPhotoFile(Guid workspaceId, Guid id, Guid photoId)
        {
            var callerId = CallerId();
            var (photo, content) = await _documentService.GetOwnedDocumentFileAsync(workspaceId, callerId, id, "LandPhoto", id, photoId);
            return File(content, photo.ContentType, photo.FileName);
        }

        [HttpPatch("{id}/photos/{photoId}")]
        public async Task<ActionResult<ApiResponse<LandPhotoResponse>>> RenamePhoto(Guid workspaceId, Guid id, Guid photoId, [FromBody] RenameDocumentRequest request)
        {
            var callerId = CallerId();
            var photo = await _documentService.RenameOwnedDocumentAsync(workspaceId, callerId, id, "LandPhoto", id, photoId, request.FileName);
            return Ok(ApiResponse<LandPhotoResponse>.Ok(ToPhotoResponse(photo)));
        }

        [HttpDelete("{id}/photos/{photoId}")]
        public async Task<IActionResult> DeletePhoto(Guid workspaceId, Guid id, Guid photoId)
        {
            var callerId = CallerId();
            await _documentService.DeleteOwnedDocumentAsync(workspaceId, callerId, id, "LandPhoto", id, photoId);
            return NoContent();
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        private static LandResponse ToResponse(Land l) => new()
        {
            LandId = l.Id,
            Address = new LandAddressDto
            {
                Village = l.Address.Village,
                GramaNiladhariDivision = l.Address.GramaNiladhariDivision,
                DivisionalSecretariat = l.Address.DivisionalSecretariat,
                PradeshiyaSabha = l.Address.PradeshiyaSabha,
                Korale = l.Address.Korale,
                Hatpattu = l.Address.Hatpattu,
                District = l.Address.District,
                Province = l.Address.Province
            },
            Area = ToAreaDto(l.AreaSquareMeters),
            Notes = l.Notes,
            CreatedAt = l.CreatedAt,
            UpdatedAt = l.UpdatedAt,
            OwnerId = l.OwnerId,
            OwnerName = l.Owner != null ? $"{l.Owner.FirstName} {l.Owner.LastName}" : l.OwnerName,
            OwnerPhone = l.Owner != null ? l.Owner.Phone : l.OwnerPhone,
            OwnerEmail = l.Owner != null ? l.Owner.Email : l.OwnerEmail,
            HasActiveLocationShareLink = l.LocationShareToken != null,
            HasActiveMapViewShareLink = l.MapViewShareToken != null
        };

        private static LandSurveyResponse ToResponse(LandSurvey s) => new()
        {
            Id = s.Id,
            LandId = s.LandId,
            SurveyPlanNumber = s.SurveyPlanNumber,
            SurveyDate = s.SurveyDate,
            SurveyedByName = s.SurveyedByName,
            Notes = s.Notes,
            CreatedAt = s.CreatedAt
        };

        private static LandDeedResponse ToResponse(LandDeed d) => new()
        {
            Id = d.Id,
            LandId = d.LandId,
            DeedNumber = d.DeedNumber,
            IssuedDate = d.IssuedDate,
            IsCurrent = d.IsCurrent,
            Notes = d.Notes,
            CreatedAt = d.CreatedAt
        };

        private static OwnedDocumentResponse ToOwnedDocumentResponse(Document d) => new()
        {
            DocumentId = d.Id,
            FileName = d.FileName,
            ContentType = d.ContentType,
            FileSizeBytes = d.FileSizeBytes,
            UploadedBy = d.UploadedBy,
            UploadedByName = $"{d.UploadedByUser.FirstName} {d.UploadedByUser.LastName}",
            CreatedAt = d.CreatedAt
        };

        private static AreaDto ToAreaDto(decimal? squareMeters)
        {
            if (squareMeters is null)
                return new AreaDto();

            var (acres, roods, perches) = AreaConversion.ToAcresRoodsPerches(squareMeters.Value);
            return new AreaDto
            {
                Acres = acres,
                Roods = roods,
                Perches = perches,
                SquareMeters = squareMeters.Value,
                Hectares = squareMeters.Value / AreaConversion.SquareMetersPerHectare
            };
        }

        private static LandBoundaryResponse ToResponse(LandBoundary b) => new()
        {
            Id = b.Id,
            LandId = b.LandId,
            Label = b.Label,
            Description = b.Description,
            CreatedAt = b.CreatedAt
        };

        private static LandMapPointResponse ToResponse(LandMapPoint p) => new()
        {
            Id = p.Id,
            LandId = p.LandId,
            Name = p.Name,
            Latitude = p.Latitude,
            Longitude = p.Longitude,
            CreatedAt = p.CreatedAt
        };

        private static LandPhotoResponse ToPhotoResponse(Document d) => new()
        {
            PhotoId = d.Id,
            FileName = d.FileName,
            ContentType = d.ContentType,
            FileSizeBytes = d.FileSizeBytes,
            UploadedByName = $"{d.UploadedByUser.FirstName} {d.UploadedByUser.LastName}",
            CreatedAt = d.CreatedAt
        };
    }
}
