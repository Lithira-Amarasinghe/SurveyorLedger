using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;

namespace SurveyorLedger.API.Controllers
{
    /// <summary>
    /// Top-level (not nested under /workspace/{id}) - both routes here are user-scoped,
    /// not workspace-scoped, so they don't fit the nested pattern JobController uses.
    /// </summary>
    [ApiController]
    [Route("api/jobs")]
    [Authorize]
    public class JobsController : ControllerBase
    {
        private readonly IScopedAccessService _access;
        private readonly IJobService _jobService;

        public JobsController(IScopedAccessService access, IJobService jobService)
        {
            _access = access;
            _jobService = jobService;
        }

        [HttpGet("mine")]
        public async Task<ActionResult<ApiResponse<List<AccessibleJobResponse>>>> GetMine()
        {
            var userId = CallerId();
            var jobs = await _access.GetAccessibleJobsAsync(userId);

            var response = jobs.Select(j => new AccessibleJobResponse
            {
                JobId = j.JobId,
                JobNumber = j.JobNumber,
                Title = j.Title,
                Status = j.Status,
                WorkspaceId = j.WorkspaceId,
                WorkspaceName = j.WorkspaceName,
                OrganizationId = j.OrganizationId,
                AccessScopeType = j.AccessScopeType
            }).ToList();

            return Ok(ApiResponse<List<AccessibleJobResponse>>.Ok(response));
        }

        [HttpGet("{jobId}")]
        public async Task<ActionResult<ApiResponse<JobWithWorkspaceResponse>>> GetById(Guid jobId)
        {
            var userId = CallerId();
            var (job, workspaceName) = await _jobService.GetAccessibleJobDetailAsync(userId, jobId);

            return Ok(ApiResponse<JobWithWorkspaceResponse>.Ok(new JobWithWorkspaceResponse
            {
                JobId = job.Id,
                JobNumber = job.JobNumber,
                Title = job.Title,
                Description = job.Description,
                Status = job.Status,
                CreatedBy = job.CreatedBy,
                CreatedAt = job.CreatedAt,
                UpdatedAt = job.UpdatedAt,
                WorkspaceId = job.WorkspaceId,
                WorkspaceName = workspaceName
            }));
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
    }
}
