using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Expense;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/job/{jobId}/expense")]
    [Authorize]
    public class ExpenseController : ControllerBase
    {
        private readonly IExpenseService _expenseService;

        public ExpenseController(IExpenseService expenseService)
        {
            _expenseService = expenseService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<ExpenseResponse>>>> GetAll(Guid workspaceId, Guid jobId)
        {
            var expenses = await _expenseService.GetAllAsync(workspaceId, CallerId(), jobId);
            return Ok(ApiResponse<List<ExpenseResponse>>.Ok(expenses.Select(ToResponse).ToList()));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<ExpenseResponse>>> GetById(Guid workspaceId, Guid jobId, Guid id)
        {
            var expense = await _expenseService.GetByIdAsync(workspaceId, CallerId(), jobId, id);
            return Ok(ApiResponse<ExpenseResponse>.Ok(ToResponse(expense)));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<ExpenseResponse>>> Create(Guid workspaceId, Guid jobId, [FromBody] ExpenseRequest request)
        {
            var expense = await _expenseService.CreateAsync(workspaceId, CallerId(), jobId, request);
            return CreatedAtAction(nameof(GetById), new { workspaceId, jobId, id = expense.Id }, ApiResponse<ExpenseResponse>.Ok(ToResponse(expense)));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<ExpenseResponse>>> Update(Guid workspaceId, Guid jobId, Guid id, [FromBody] ExpenseRequest request)
        {
            var expense = await _expenseService.UpdateAsync(workspaceId, CallerId(), jobId, id, request);
            return Ok(ApiResponse<ExpenseResponse>.Ok(ToResponse(expense)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid workspaceId, Guid jobId, Guid id)
        {
            await _expenseService.DeleteAsync(workspaceId, CallerId(), jobId, id);
            return NoContent();
        }

        [HttpPost("{id}/receipt")]
        [RequestSizeLimit(DocumentService.MaxFileSizeBytes)]
        public async Task<ActionResult<ApiResponse<ExpenseResponse>>> UploadReceipt(Guid workspaceId, Guid jobId, Guid id, IFormFile file)
        {
            var expense = await _expenseService.UploadReceiptAsync(workspaceId, CallerId(), jobId, id, file);
            return Ok(ApiResponse<ExpenseResponse>.Ok(ToResponse(expense)));
        }

        [HttpGet("{id}/receipt")]
        public async Task<IActionResult> GetReceipt(Guid workspaceId, Guid jobId, Guid id)
        {
            var (expense, content) = await _expenseService.GetReceiptFileAsync(workspaceId, CallerId(), jobId, id);
            return File(content, "application/octet-stream", Path.GetFileName(expense.ReceiptFilePath!));
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        private static ExpenseResponse ToResponse(Expense e) => new()
        {
            ExpenseId = e.Id,
            JobId = e.JobId,
            Category = e.Category,
            Amount = e.Amount,
            Description = e.Description,
            IncurredDate = e.IncurredDate,
            HasReceipt = e.ReceiptFilePath != null,
            PayeeId = e.PayeeId,
            PayeeName = e.Payee == null ? null : $"{e.Payee.FirstName} {e.Payee.LastName}",
            PayeeType = e.PayeeType,
            RecordedByName = $"{e.RecordedByUser.FirstName} {e.RecordedByUser.LastName}",
            CreatedAt = e.CreatedAt
        };
    }
}
