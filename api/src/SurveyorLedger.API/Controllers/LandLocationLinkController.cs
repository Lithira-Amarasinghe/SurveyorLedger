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
            return Ok(ApiResponse<LandLocationLinkPreviewResponse>.Ok(new LandLocationLinkPreviewResponse
            {
                AddressLine = FormatAddressLine(land),
                Latitude = land.Latitude,
                Longitude = land.Longitude
            }));
        }

        [HttpPut("{token}")]
        public async Task<IActionResult> SetLocation(string token, [FromBody] LandLocationRequest request)
        {
            await _landService.SetLocationViaShareTokenAsync(token, request);
            return NoContent();
        }

        private static string FormatAddressLine(Data.Entities.Land land)
        {
            var parts = new[] { land.Address.Street, land.Address.City }.Where(p => !string.IsNullOrWhiteSpace(p));
            var line = string.Join(", ", parts);
            return string.IsNullOrEmpty(line) ? "Unnamed land record" : line;
        }
    }
}
