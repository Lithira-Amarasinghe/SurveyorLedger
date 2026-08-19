using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;

namespace SurveyorLedger.API.Controllers
{
    /// <summary>
    /// Deliberately separate from LandController: every action here is unauthenticated
    /// by design (the token is the only credential), mirroring DocumentRequestLinkController's
    /// split so the trust boundary is visible at a glance.
    /// </summary>
    [ApiController]
    [Route("api/land-location-links")]
    [EnableRateLimiting("auth")]
    public class LandLocationLinkController : ControllerBase
    {
        private readonly ILandService _landService;

        public LandLocationLinkController(ILandService landService)
        {
            _landService = landService;
        }

        [HttpGet("{token}")]
        public async Task<ActionResult<ApiResponse<LandLocationLinkPreviewResponse>>> Preview(string token)
        {
            var land = await _landService.GetByLocationShareTokenAsync(token);
            var points = await _landService.GetMapPointsForShareTokenAsync(token);
            return Ok(ApiResponse<LandLocationLinkPreviewResponse>.Ok(new LandLocationLinkPreviewResponse
            {
                AddressLine = FormatAddressLine(land),
                Points = points.Select(ToResponse).ToList()
            }));
        }

        [HttpPost("{token}/points")]
        public async Task<ActionResult<ApiResponse<LandMapPointResponse>>> AddPoint(string token, [FromBody] LandMapPointRequest request)
        {
            var point = await _landService.AddMapPointViaShareTokenAsync(token, request);
            return Ok(ApiResponse<LandMapPointResponse>.Ok(ToResponse(point)));
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
