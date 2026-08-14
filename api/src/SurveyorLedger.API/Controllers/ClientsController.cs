using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/clients")]
    [Authorize]
    public class ClientsController : ControllerBase
    {
        private readonly IClientService _clientService;

        public ClientsController(IClientService clientService)
        {
            _clientService = clientService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<ClientResponse>>>> Search(Guid workspaceId, [FromQuery] string? query)
        {
            var clients = await _clientService.SearchAsync(workspaceId, CallerId(), query);
            return Ok(ApiResponse<List<ClientResponse>>.Ok(clients.Select(ToResponse).ToList()));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<ClientResponse>>> Create(Guid workspaceId, [FromBody] ClientRequest request)
        {
            var client = await _clientService.CreateAsync(workspaceId, CallerId(), request);
            return CreatedAtAction(nameof(GetById), new { workspaceId, id = client.Id }, ApiResponse<ClientResponse>.Ok(ToResponse(client)));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<ClientResponse>>> GetById(Guid workspaceId, Guid id)
        {
            var client = await _clientService.GetByIdAsync(workspaceId, CallerId(), id);
            return Ok(ApiResponse<ClientResponse>.Ok(ToResponse(client)));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<ClientResponse>>> Update(Guid workspaceId, Guid id, [FromBody] ClientRequest request)
        {
            var client = await _clientService.UpdateAsync(workspaceId, CallerId(), id, request);
            return Ok(ApiResponse<ClientResponse>.Ok(ToResponse(client)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid workspaceId, Guid id)
        {
            await _clientService.DeleteAsync(workspaceId, CallerId(), id);
            return NoContent();
        }

        [HttpGet("{id}/balance")]
        public async Task<ActionResult<ApiResponse<ClientBalanceResponse>>> GetBalance(Guid workspaceId, Guid id)
        {
            var balance = await _clientService.GetBalanceAsync(workspaceId, CallerId(), id);
            return Ok(ApiResponse<ClientBalanceResponse>.Ok(new ClientBalanceResponse { ClientId = id, OutstandingBalance = balance }));
        }

        [HttpGet("{id}/payments")]
        public async Task<ActionResult<ApiResponse<List<PaymentResponse>>>> GetPayments(Guid workspaceId, Guid id)
        {
            var payments = await _clientService.GetPaymentHistoryAsync(workspaceId, CallerId(), id);
            return Ok(ApiResponse<List<PaymentResponse>>.Ok(payments.Select(ToPaymentResponse).ToList()));
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        internal static ClientResponse ToResponse(Client c) => new()
        {
            ClientId = c.Id,
            Name = c.Name,
            Phone = c.Phone,
            Email = c.Email,
            Address = new AddressDto
            {
                Street = c.Address.Street,
                City = c.Address.City,
                District = c.Address.District,
                PostalCode = c.Address.PostalCode,
                Country = c.Address.Country
            },
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        };

        private static PaymentResponse ToPaymentResponse(Payment p) => new()
        {
            PaymentId = p.Id,
            InvoiceId = p.InvoiceId,
            Amount = p.Amount,
            Method = p.Method,
            ReceivedAt = p.ReceivedAt,
            ReferenceNumber = p.ReferenceNumber,
            HasProofFile = p.ProofFilePath != null,
            ReceiptNumber = p.ReceiptNumber,
            CreatedAt = p.CreatedAt
        };
    }
}
