using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Report;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/reports")]
    [Authorize]
    public class ReportController : ControllerBase
    {
        private readonly IReportService _reportService;

        public ReportController(IReportService reportService)
        {
            _reportService = reportService;
        }

        [HttpGet("summary")]
        public async Task<ActionResult<ApiResponse<FinancialSummaryResponse>>> GetSummary(Guid workspaceId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var summary = await _reportService.GetFinancialSummaryAsync(workspaceId, CallerId(), from, to);
            return Ok(ApiResponse<FinancialSummaryResponse>.Ok(summary));
        }

        [HttpGet("payments")]
        public async Task<ActionResult<ApiResponse<PagedResult<PaymentHistoryRow>>>> GetPayments(
            Guid workspaceId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            var result = await _reportService.GetPaymentHistoryAsync(workspaceId, CallerId(), from, to, page, pageSize);
            return Ok(ApiResponse<PagedResult<PaymentHistoryRow>>.Ok(result));
        }

        [HttpGet("expenses")]
        public async Task<ActionResult<ApiResponse<PagedResult<ExpenseHistoryRow>>>> GetExpenses(
            Guid workspaceId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            var result = await _reportService.GetExpenseHistoryAsync(workspaceId, CallerId(), from, to, page, pageSize);
            return Ok(ApiResponse<PagedResult<ExpenseHistoryRow>>.Ok(result));
        }

        [HttpGet("outstanding-invoices")]
        public async Task<ActionResult<ApiResponse<List<OutstandingInvoiceRow>>>> GetOutstandingInvoices(Guid workspaceId)
        {
            var result = await _reportService.GetOutstandingInvoicesAsync(workspaceId, CallerId());
            return Ok(ApiResponse<List<OutstandingInvoiceRow>>.Ok(result));
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
    }
}
