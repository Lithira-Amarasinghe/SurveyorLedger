using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Budget;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/job/{jobId}/budget")]
    [Authorize]
    public class JobBudgetController : ControllerBase
    {
        private readonly IJobBudgetService _budgetService;

        public JobBudgetController(IJobBudgetService budgetService)
        {
            _budgetService = budgetService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<JobBudgetResponse?>>> Get(Guid workspaceId, Guid jobId)
        {
            var budget = await _budgetService.GetAsync(workspaceId, CallerId(), jobId);
            return Ok(ApiResponse<JobBudgetResponse?>.Ok(budget == null ? null : ToResponse(budget)));
        }

        [HttpPut]
        public async Task<ActionResult<ApiResponse<JobBudgetResponse>>> Upsert(Guid workspaceId, Guid jobId, [FromBody] JobBudgetRequest request)
        {
            var budget = await _budgetService.UpsertAsync(workspaceId, CallerId(), jobId, request);
            return Ok(ApiResponse<JobBudgetResponse>.Ok(ToResponse(budget)));
        }

        [HttpDelete]
        public async Task<IActionResult> Delete(Guid workspaceId, Guid jobId)
        {
            await _budgetService.DeleteAsync(workspaceId, CallerId(), jobId);
            return NoContent();
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        private static JobBudgetResponse ToResponse(JobBudget b) => new()
        {
            JobId = b.JobId,
            EstimatedFee = b.EstimatedFee,
            EstimatedCost = b.EstimatedCost,
            ExpectedProfit = b.EstimatedFee - b.EstimatedCost,
            UpdatedByName = $"{b.UpdatedByPerson.FirstName} {b.UpdatedByPerson.LastName}",
            UpdatedAt = b.UpdatedAt
        };
    }
}
