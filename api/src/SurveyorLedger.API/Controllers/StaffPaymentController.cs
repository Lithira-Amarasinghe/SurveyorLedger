using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Models.StaffPayment;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/job/{jobId}/staff-payment")]
    [Authorize]
    public class StaffPaymentController : ControllerBase
    {
        private readonly IStaffPaymentService _staffPaymentService;

        public StaffPaymentController(IStaffPaymentService staffPaymentService)
        {
            _staffPaymentService = staffPaymentService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<StaffPaymentResponse>>>> GetAll(Guid workspaceId, Guid jobId)
        {
            var payments = await _staffPaymentService.GetAllAsync(workspaceId, CallerId(), jobId);
            return Ok(ApiResponse<List<StaffPaymentResponse>>.Ok(payments.Select(ToResponse).ToList()));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<StaffPaymentResponse>>> GetById(Guid workspaceId, Guid jobId, Guid id)
        {
            var payment = await _staffPaymentService.GetByIdAsync(workspaceId, CallerId(), jobId, id);
            return Ok(ApiResponse<StaffPaymentResponse>.Ok(ToResponse(payment)));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<StaffPaymentResponse>>> Create(Guid workspaceId, Guid jobId, [FromBody] StaffPaymentRequest request)
        {
            var payment = await _staffPaymentService.CreateAsync(workspaceId, CallerId(), jobId, request);
            return CreatedAtAction(nameof(GetById), new { workspaceId, jobId, id = payment.Id }, ApiResponse<StaffPaymentResponse>.Ok(ToResponse(payment)));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<StaffPaymentResponse>>> Update(Guid workspaceId, Guid jobId, Guid id, [FromBody] StaffPaymentRequest request)
        {
            var payment = await _staffPaymentService.UpdateAsync(workspaceId, CallerId(), jobId, id, request);
            return Ok(ApiResponse<StaffPaymentResponse>.Ok(ToResponse(payment)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid workspaceId, Guid jobId, Guid id)
        {
            await _staffPaymentService.DeleteAsync(workspaceId, CallerId(), jobId, id);
            return NoContent();
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        private static StaffPaymentResponse ToResponse(StaffPayment p) => new()
        {
            StaffPaymentId = p.Id,
            JobId = p.JobId,
            UserId = p.UserId,
            UserName = $"{p.User.FirstName} {p.User.LastName}",
            Type = p.Type,
            Amount = p.Amount,
            PaidDate = p.PaidDate,
            Notes = p.Notes,
            CreatedAt = p.CreatedAt
        };
    }
}
