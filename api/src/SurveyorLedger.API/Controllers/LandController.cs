using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Land;
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
        private readonly ILogger<LandController> _logger;

        public LandController(ILandService landService, ILogger<LandController> logger)
        {
            _landService = landService;
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

        [HttpPut("{id}/location")]
        public async Task<ActionResult<ApiResponse<LandResponse>>> SetLocation(Guid workspaceId, Guid id, [FromBody] LandLocationRequest request)
        {
            var callerId = CallerId();
            var land = await _landService.SetLocationAsync(workspaceId, callerId, id, request);
            return Ok(ApiResponse<LandResponse>.Ok(ToResponse(land)));
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

        [HttpGet("{id}/photos")]
        public async Task<ActionResult<ApiResponse<List<LandPhotoResponse>>>> GetPhotos(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var photos = await _landService.GetPhotosAsync(workspaceId, callerId, id);
            return Ok(ApiResponse<List<LandPhotoResponse>>.Ok(photos.Select(ToResponse).ToList()));
        }

        [HttpPost("{id}/photos")]
        [RequestSizeLimit(DocumentService.MaxFileSizeBytes)]
        public async Task<ActionResult<ApiResponse<LandPhotoResponse>>> UploadPhoto(Guid workspaceId, Guid id, IFormFile file)
        {
            var callerId = CallerId();
            var photo = await _landService.UploadPhotoAsync(workspaceId, callerId, id, file);
            return Ok(ApiResponse<LandPhotoResponse>.Ok(ToResponse(photo)));
        }

        [HttpGet("{id}/photos/{photoId}")]
        public async Task<IActionResult> GetPhotoFile(Guid workspaceId, Guid id, Guid photoId)
        {
            var callerId = CallerId();
            var (photo, content) = await _landService.GetPhotoFileAsync(workspaceId, callerId, id, photoId);
            return File(content, photo.ContentType, photo.FileName);
        }

        [HttpDelete("{id}/photos/{photoId}")]
        public async Task<IActionResult> DeletePhoto(Guid workspaceId, Guid id, Guid photoId)
        {
            var callerId = CallerId();
            await _landService.DeletePhotoAsync(workspaceId, callerId, id, photoId);
            return NoContent();
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        private static LandResponse ToResponse(Land l) => new()
        {
            LandId = l.Id,
            Address = new AddressDto
            {
                Street = l.Address.Street,
                City = l.Address.City,
                District = l.Address.District,
                PostalCode = l.Address.PostalCode,
                Country = l.Address.Country
            },
            Area = ToAreaDto(l.AreaSquareMeters),
            GpsCoordinates = l.GpsCoordinates,
            Notes = l.Notes,
            CreatedAt = l.CreatedAt,
            UpdatedAt = l.UpdatedAt,
            OwnerId = l.OwnerId,
            OwnerName = l.Owner != null ? $"{l.Owner.FirstName} {l.Owner.LastName}" : l.OwnerName,
            OwnerPhone = l.Owner != null ? l.Owner.Phone : l.OwnerPhone,
            OwnerEmail = l.Owner != null ? l.Owner.Email : l.OwnerEmail,
            Latitude = l.Latitude,
            Longitude = l.Longitude,
            HasActiveLocationShareLink = l.LocationShareToken != null
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

        private static LandPhotoResponse ToResponse(LandPhoto p) => new()
        {
            PhotoId = p.Id,
            FileName = p.FileName,
            ContentType = p.ContentType,
            FileSizeBytes = p.FileSizeBytes,
            UploadedByName = $"{p.UploadedByUser.FirstName} {p.UploadedByUser.LastName}",
            CreatedAt = p.CreatedAt
        };
    }
}
