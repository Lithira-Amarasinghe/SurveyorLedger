using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Client;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/client")]
    [Authorize]
    public class ClientController : ControllerBase
    {
        private readonly IClientService _clientService;
        private readonly ILogger<ClientController> _logger;

        public ClientController(IClientService clientService, ILogger<ClientController> logger)
        {
            _clientService = clientService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<ClientResponse>>> Create(Guid workspaceId, [FromBody] ClientRequest request)
        {
            var callerId = CallerId();
            var user = await _clientService.CreateAsync(workspaceId, callerId, request);
            return Ok(ApiResponse<ClientResponse>.Ok(ToResponse(user)));
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<ClientResponse>>>> Search(Guid workspaceId, [FromQuery] string? query)
        {
            var callerId = CallerId();
            var clients = await _clientService.SearchAsync(workspaceId, callerId, query);
            return Ok(ApiResponse<List<ClientResponse>>.Ok(clients.Select(ToResponse).ToList()));
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        private static ClientResponse ToResponse(User u) => new()
        {
            UserId = u.Id,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Phone = u.Phone,
            Email = u.Email,
            HasLogin = u.PasswordHash != null,
            CreatedAt = u.CreatedAt
        };
    }
}
