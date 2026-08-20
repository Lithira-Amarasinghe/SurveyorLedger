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
    [Route("api/workspace/{workspaceId}/invoices")]
    [Authorize]
    public class InvoicesController : ControllerBase
    {
        private readonly IInvoiceService _invoiceService;
        private readonly IConfiguration _config;

        public InvoicesController(IInvoiceService invoiceService, IConfiguration config)
        {
            _invoiceService = invoiceService;
            _config = config;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<InvoiceResponse>>>> Search(Guid workspaceId, [FromQuery] Guid? clientId, [FromQuery] Guid? jobId)
        {
            var invoices = await _invoiceService.SearchAsync(workspaceId, CallerId(), clientId, jobId);
            return Ok(ApiResponse<List<InvoiceResponse>>.Ok(invoices.Select(i => ToResponse(i, _invoiceService)).ToList()));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<InvoiceResponse>>> Create(Guid workspaceId, [FromBody] InvoiceRequest request)
        {
            var invoice = await _invoiceService.CreateAsync(workspaceId, CallerId(), request);
            return CreatedAtAction(nameof(GetById), new { workspaceId, id = invoice.Id }, ApiResponse<InvoiceResponse>.Ok(ToResponse(invoice, _invoiceService)));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<InvoiceResponse>>> GetById(Guid workspaceId, Guid id)
        {
            var invoice = await _invoiceService.GetByIdAsync(workspaceId, CallerId(), id);
            return Ok(ApiResponse<InvoiceResponse>.Ok(ToResponse(invoice, _invoiceService)));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<InvoiceResponse>>> Update(Guid workspaceId, Guid id, [FromBody] InvoiceRequest request)
        {
            var invoice = await _invoiceService.UpdateAsync(workspaceId, CallerId(), id, request);
            return Ok(ApiResponse<InvoiceResponse>.Ok(ToResponse(invoice, _invoiceService)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid workspaceId, Guid id)
        {
            await _invoiceService.DeleteAsync(workspaceId, CallerId(), id);
            return NoContent();
        }

        [HttpPost("{id}/payments")]
        public async Task<ActionResult<ApiResponse<PaymentResponse>>> RecordPayment(Guid workspaceId, Guid id, [FromForm] PaymentRequest request, IFormFile? proofFile)
        {
            var payment = await _invoiceService.RecordPaymentAsync(workspaceId, CallerId(), id, request, proofFile);
            return Ok(ApiResponse<PaymentResponse>.Ok(ToResponse(payment)));
        }

        [HttpGet("{id}/payments")]
        public async Task<ActionResult<ApiResponse<List<PaymentResponse>>>> GetPayments(Guid workspaceId, Guid id)
        {
            var payments = await _invoiceService.GetPaymentsAsync(workspaceId, CallerId(), id);
            return Ok(ApiResponse<List<PaymentResponse>>.Ok(payments.Select(ToResponse).ToList()));
        }

        [HttpPost("{id}/send")]
        public async Task<IActionResult> Send(Guid workspaceId, Guid id, [FromBody] SendInvoiceRequest request)
        {
            var appBaseUrl = _config["AppSettings:UiBaseUrl"] ?? throw new InvalidOperationException("AppSettings:UiBaseUrl not configured");
            await _invoiceService.SendAsync(workspaceId, CallerId(), id, request.RecipientPersonIds, appBaseUrl);
            return NoContent();
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        internal static InvoiceResponse ToResponse(Invoice i, IInvoiceService invoiceService)
        {
            var (total, amountPaid, balance, isOverdue, daysOverdue) = invoiceService.ComputeInvoiceTotals(i);
            var subtotal = i.LineItems.Sum(li => li.Quantity * li.UnitPrice);
            return new InvoiceResponse
            {
                InvoiceId = i.Id,
                ClientId = i.ClientId,
                JobId = i.JobId,
                QuotationId = i.QuotationId,
                Number = i.Number,
                LineItems = i.LineItems.Select(li => new LineItemDto { Description = li.Description, Quantity = li.Quantity, UnitPrice = li.UnitPrice, MilestoneId = li.MilestoneId }).ToList(),
                TaxRatePercent = i.TaxRatePercent,
                DiscountAmount = i.DiscountAmount,
                Subtotal = subtotal,
                Total = total,
                AmountPaid = amountPaid,
                Balance = balance,
                Status = i.Status,
                DueDate = i.DueDate,
                IsOverdue = isOverdue,
                DaysOverdue = daysOverdue,
                CreatedAt = i.CreatedAt,
                UpdatedAt = i.UpdatedAt,
                Installments = invoiceService.ComputeInstallmentStatuses(i)
                    .Select(x => new InstallmentResponse { Amount = x.Installment.Amount, DueDate = x.Installment.DueDate, Status = x.Status })
                    .ToList()
            };
        }

        internal static PaymentResponse ToResponse(Payment p) => new()
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
