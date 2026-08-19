using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;

namespace SurveyorLedger.API.Controllers
{
    /// <summary>
    /// Independent of LandLocationLinkController's add-a-point link: this one is purely
    /// read-only, for sharing "here's where everything is" with someone who should never
    /// be able to add, move, or delete a point - e.g. a client wanting to navigate to the
    /// site, not a surveyor pinning a new corner.
    /// </summary>
    [ApiController]
    [Route("api/land-map-view-links")]
    [EnableRateLimiting("auth")]
    public class LandMapViewLinkController : ControllerBase
    {
        private readonly ILandService _landService;

        public LandMapViewLinkController(ILandService landService)
        {
            _landService = landService;
        }

        [HttpGet("{token}")]
        public async Task<ActionResult<ApiResponse<LandMapViewLinkPreviewResponse>>> Preview(string token)
        {
            var land = await _landService.GetByMapViewShareTokenAsync(token);
            var points = await _landService.GetMapPointsForMapViewShareTokenAsync(token);
            return Ok(ApiResponse<LandMapViewLinkPreviewResponse>.Ok(new LandMapViewLinkPreviewResponse
            {
                AddressLine = FormatAddressLine(land),
                Points = points.Select(ToResponse).ToList()
            }));
        }

        private static LandMapPointResponse ToResponse(Data.Entities.LandMapPoint p) => new()
        {
            Id = p.Id,
            LandId = p.LandId,
            Name = p.Name,
            Latitude = p.Latitude,
            Longitude = p.Longitude,
            CreatedAt = p.CreatedAt
        };

        private static string FormatAddressLine(Data.Entities.Land land)
        {
            var parts = new[] { land.Address.Village, land.Address.DivisionalSecretariat, land.Address.District }.Where(p => !string.IsNullOrWhiteSpace(p));
            var line = string.Join(", ", parts);
            return string.IsNullOrEmpty(line) ? "Unnamed land record" : line;
        }
    }
}
