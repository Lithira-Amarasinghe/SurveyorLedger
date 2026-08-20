using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/quotations")]
    [Authorize]
    public class QuotationsController : ControllerBase
    {
        private readonly IQuotationService _quotationService;
        private readonly IInvoiceService _invoiceService;
        private readonly IConfiguration _config;

        public QuotationsController(IQuotationService quotationService, IInvoiceService invoiceService, IConfiguration config)
        {
            _quotationService = quotationService;
            _invoiceService = invoiceService;
            _config = config;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<QuotationResponse>>>> Search(Guid workspaceId, [FromQuery] Guid? clientId, [FromQuery] Guid? jobId)
        {
            var quotations = await _quotationService.SearchAsync(workspaceId, CallerId(), clientId, jobId);
            return Ok(ApiResponse<List<QuotationResponse>>.Ok(quotations.Select(ToResponse).ToList()));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<QuotationResponse>>> Create(Guid workspaceId, [FromBody] QuotationRequest request)
        {
            var quotation = await _quotationService.CreateAsync(workspaceId, CallerId(), request);
            return CreatedAtAction(nameof(GetById), new { workspaceId, id = quotation.Id }, ApiResponse<QuotationResponse>.Ok(ToResponse(quotation)));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<QuotationResponse>>> GetById(Guid workspaceId, Guid id)
        {
            var quotation = await _quotationService.GetByIdAsync(workspaceId, CallerId(), id);
            return Ok(ApiResponse<QuotationResponse>.Ok(ToResponse(quotation)));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<QuotationResponse>>> Update(Guid workspaceId, Guid id, [FromBody] QuotationRequest request)
        {
            var quotation = await _quotationService.UpdateAsync(workspaceId, CallerId(), id, request);
            return Ok(ApiResponse<QuotationResponse>.Ok(ToResponse(quotation)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid workspaceId, Guid id)
        {
            await _quotationService.DeleteAsync(workspaceId, CallerId(), id);
            return NoContent();
        }

        [HttpPost("{id}/convert-to-invoice")]
        public async Task<ActionResult<ApiResponse<InvoiceResponse>>> ConvertToInvoice(Guid workspaceId, Guid id, [FromBody] ConvertQuotationRequest request)
        {
            var invoice = await _quotationService.ConvertToInvoiceAsync(workspaceId, CallerId(), id, request);
            return Ok(ApiResponse<InvoiceResponse>.Ok(InvoicesController.ToResponse(invoice, _invoiceService)));
        }

        [HttpPost("{id}/send")]
        public async Task<IActionResult> Send(Guid workspaceId, Guid id, [FromBody] SendQuotationRequest request)
        {
            var appBaseUrl = _config["AppSettings:UiBaseUrl"] ?? throw new InvalidOperationException("AppSettings:UiBaseUrl not configured");
            await _quotationService.SendAsync(workspaceId, CallerId(), id, request.RecipientPersonIds, appBaseUrl);
            return NoContent();
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        internal static QuotationResponse ToResponse(Quotation q)
        {
            var subtotal = q.LineItems.Sum(li => li.Quantity * li.UnitPrice);
            var tax = subtotal * q.TaxRatePercent / 100m;
            return new QuotationResponse
            {
                QuotationId = q.Id,
                ClientId = q.ClientId,
                JobId = q.JobId,
                Number = q.Number,
                LineItems = q.LineItems.Select(li => new LineItemDto { Description = li.Description, Quantity = li.Quantity, UnitPrice = li.UnitPrice, MilestoneId = li.MilestoneId }).ToList(),
                TaxRatePercent = q.TaxRatePercent,
                Subtotal = subtotal,
                Total = subtotal + tax,
                Status = q.Status,
                ValidUntil = q.ValidUntil,
                RevisionNumber = q.RevisionNumber,
                CreatedAt = q.CreatedAt,
                UpdatedAt = q.UpdatedAt
            };
        }
    }
}
