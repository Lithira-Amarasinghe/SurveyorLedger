using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Milestone;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/job/{jobId}/milestone")]
    [Authorize]
    public class MilestoneController : ControllerBase
    {
        private readonly IMilestoneService _milestoneService;

        public MilestoneController(IMilestoneService milestoneService)
        {
            _milestoneService = milestoneService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<MilestoneResponse>>>> List(Guid workspaceId, Guid jobId)
        {
            var milestones = await _milestoneService.GetMilestonesAsync(workspaceId, CallerId(), jobId);
            var responses = new List<MilestoneResponse>();
            foreach (var m in milestones)
                responses.Add(await ToResponseAsync(m));
            return Ok(ApiResponse<List<MilestoneResponse>>.Ok(responses));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<MilestoneResponse>>> GetById(Guid workspaceId, Guid jobId, Guid id)
        {
            var milestone = await _milestoneService.GetByIdAsync(workspaceId, CallerId(), jobId, id);
            return Ok(ApiResponse<MilestoneResponse>.Ok(await ToResponseAsync(milestone)));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<MilestoneResponse>>> Create(Guid workspaceId, Guid jobId, [FromBody] MilestoneRequest request)
        {
            var milestone = await _milestoneService.CreateAsync(workspaceId, CallerId(), jobId, request);
            return CreatedAtAction(nameof(GetById), new { workspaceId, jobId, id = milestone.Id }, ApiResponse<MilestoneResponse>.Ok(await ToResponseAsync(milestone)));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<MilestoneResponse>>> Update(Guid workspaceId, Guid jobId, Guid id, [FromBody] MilestoneRequest request)
        {
            var milestone = await _milestoneService.UpdateAsync(workspaceId, CallerId(), jobId, id, request);
            return Ok(ApiResponse<MilestoneResponse>.Ok(await ToResponseAsync(milestone)));
        }

        [HttpPut("{id}/status")]
        public async Task<ActionResult<ApiResponse<MilestoneResponse>>> UpdateStatus(Guid workspaceId, Guid jobId, Guid id, [FromBody] MilestoneStatusRequest request)
        {
            var milestone = await _milestoneService.UpdateStatusAsync(workspaceId, CallerId(), jobId, id, request.Status);
            return Ok(ApiResponse<MilestoneResponse>.Ok(await ToResponseAsync(milestone)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid workspaceId, Guid jobId, Guid id)
        {
            await _milestoneService.DeleteAsync(workspaceId, CallerId(), jobId, id);
            return NoContent();
        }

        [HttpPut("reorder")]
        public async Task<ActionResult<ApiResponse<List<MilestoneResponse>>>> Reorder(Guid workspaceId, Guid jobId, [FromBody] MilestoneReorderRequest request)
        {
            var milestones = await _milestoneService.ReorderAsync(workspaceId, CallerId(), jobId, request.MilestoneIds);
            var responses = new List<MilestoneResponse>();
            foreach (var m in milestones)
                responses.Add(await ToResponseAsync(m));
            return Ok(ApiResponse<List<MilestoneResponse>>.Ok(responses));
        }

        [HttpGet("{id}/payment-requirements")]
        public async Task<ActionResult<ApiResponse<List<PaymentRequirementDto>>>> GetPaymentRequirements(Guid workspaceId, Guid jobId, Guid id)
        {
            var requirements = await _milestoneService.GetPaymentRequirementsAsync(workspaceId, CallerId(), jobId, id);
            return Ok(ApiResponse<List<PaymentRequirementDto>>.Ok(requirements.Select(r => new PaymentRequirementDto { TargetStatus = r.TargetStatus, RequiredState = r.RequiredState }).ToList()));
        }

        [HttpPut("{id}/payment-requirements")]
        public async Task<ActionResult<ApiResponse<List<PaymentRequirementDto>>>> SetPaymentRequirements(Guid workspaceId, Guid jobId, Guid id, [FromBody] SetPaymentRequirementsRequest request)
        {
            var rules = request.Requirements.Select(r => (r.TargetStatus, r.RequiredState)).ToList();
            var requirements = await _milestoneService.SetPaymentRequirementsAsync(workspaceId, CallerId(), jobId, id, rules);
            return Ok(ApiResponse<List<PaymentRequirementDto>>.Ok(requirements.Select(r => new PaymentRequirementDto { TargetStatus = r.TargetStatus, RequiredState = r.RequiredState }).ToList()));
        }

        [HttpGet("{id}/payment-status")]
        public async Task<ActionResult<ApiResponse<MilestonePaymentStatusResponse>>> GetPaymentStatus(Guid workspaceId, Guid jobId, Guid id)
        {
            var status = await _milestoneService.GetPaymentStatusAsync(workspaceId, CallerId(), jobId, id);
            return Ok(ApiResponse<MilestonePaymentStatusResponse>.Ok(new MilestonePaymentStatusResponse
            {
                Amount = status.Amount,
                CommittedAmount = status.CommittedAmount,
                QuotedAmount = status.QuotedAmount,
                InvoicedAmount = status.InvoicedAmount,
                PaidAmount = status.PaidAmount,
                RemainingAmount = status.RemainingAmount,
                LinkedInvoices = status.LinkedInvoices.Select(i => new LinkedInvoiceSummaryDto { InvoiceId = i.InvoiceId, Number = i.Number, Status = i.Status }).ToList(),
                LinkedQuotations = status.LinkedQuotations.Select(q => new LinkedQuotationSummaryDto { QuotationId = q.QuotationId, Number = q.Number, Status = q.Status }).ToList(),
                NextGate = status.NextGate
            }));
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        // Note: sequential await in a loop, never Select(...).ToList() with an async lambda
        // or Task.WhenAll - the injected ApplicationDbContext is scoped per-request and not
        // safe for concurrent operations.
        private async Task<MilestoneResponse> ToResponseAsync(Milestone m)
        {
            var committed = await _milestoneService.GetCommittedAmountAsync(m.JobId, m.Id);
            return new MilestoneResponse
            {
                MilestoneId = m.Id,
                JobId = m.JobId,
                Title = m.Title,
                Description = m.Description,
                DueDate = m.DueDate,
                Amount = m.Amount,
                Status = m.Status,
                SortOrder = m.SortOrder,
                CompletedAt = m.CompletedAt,
                CompletedBy = m.CompletedBy,
                CreatedBy = m.CreatedBy,
                CreatedAt = m.CreatedAt,
                UpdatedAt = m.UpdatedAt,
                CommittedAmount = committed,
                RemainingAmount = m.Amount.HasValue ? m.Amount.Value - committed : null
            };
        }
    }
}
