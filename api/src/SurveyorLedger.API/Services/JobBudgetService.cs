using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Budget;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IJobBudgetService
{
    Task<JobBudget?> GetAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
    Task<JobBudget> UpsertAsync(Guid workspaceId, Guid callerUserId, Guid jobId, JobBudgetRequest request);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
}

/// <summary>
/// Admin-only, workspace-level permission checks (not job-scoped) - budget visibility
/// doesn't vary by job, only by role, same as ManageMembers. See job-budget design spec.
/// </summary>
public class JobBudgetService : IJobBudgetService
{
    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly ILogger<JobBudgetService> _logger;

    public JobBudgetService(ApplicationDbContext context, IScopedAccessService access, ILogger<JobBudgetService> logger)
    {
        _context = context;
        _access = access;
        _logger = logger;
    }

    public async Task<JobBudget?> GetAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "budget", "view", workspaceId);

        return await _context.JobBudgets.Include(b => b.UpdatedByPerson)
            .FirstOrDefaultAsync(b => b.JobId == jobId);
    }

    public async Task<JobBudget> UpsertAsync(Guid workspaceId, Guid callerUserId, Guid jobId, JobBudgetRequest request)
    {
        await FindJobAsync(workspaceId, jobId);
        if (request.EstimatedFee < 0 || request.EstimatedCost < 0)
            throw new ValidationException("Estimated fee and cost cannot be negative.");

        var existing = await _context.JobBudgets.FirstOrDefaultAsync(b => b.JobId == jobId);
        await _access.EnsureAllowedAsync(callerUserId, "budget", existing == null ? "create" : "edit", workspaceId);
        var callerPersonId = await _access.ResolvePersonIdAsync(callerUserId);

        if (existing == null)
        {
            existing = new JobBudget { JobId = jobId };
            await _context.JobBudgets.AddAsync(existing);
        }

        existing.EstimatedFee = request.EstimatedFee;
        existing.EstimatedCost = request.EstimatedCost;
        existing.UpdatedBy = callerPersonId;
        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Budget set for job {JobId} by {UserId}", jobId, callerUserId);
        return await _context.JobBudgets.Include(b => b.UpdatedByPerson).FirstAsync(b => b.JobId == jobId);
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "budget", "delete", workspaceId);

        var existing = await _context.JobBudgets.FirstOrDefaultAsync(b => b.JobId == jobId)
            ?? throw new NotFoundException("No budget set for this job.");
        _context.JobBudgets.Remove(existing);
        await _context.SaveChangesAsync();
    }

    private async Task<Job> FindJobAsync(Guid workspaceId, Guid jobId)
    {
        return await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Job not found");
    }
}
