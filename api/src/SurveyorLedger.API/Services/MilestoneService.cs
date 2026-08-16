using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Milestone;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IMilestoneService
{
    Task<List<Milestone>> GetMilestonesAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
    Task<Milestone> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId);
    Task<Milestone> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, MilestoneRequest request);
    Task<Milestone> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, MilestoneRequest request);
    Task<Milestone> UpdateStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, string status);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId);
    Task<List<Milestone>> ReorderAsync(Guid workspaceId, Guid callerUserId, Guid jobId, List<Guid> orderedMilestoneIds);
}

/// <summary>
/// Milestones are a job sub-resource: every action reuses JobService's job.view /
/// job.edit Casbin permissions and the same job-assignment scoping rule (unless the
/// caller holds job.view_all, they must hold a job-scoped UserAccess row for this
/// specific job). This is intentionally duplicated from JobService rather than
/// extracted to a shared base - see the design spec's reasoning: only two call sites
/// exist, and a shared abstraction for two users isn't justified yet.
/// </summary>
public class MilestoneService : IMilestoneService
{
    private static readonly HashSet<string> ValidStatuses = new() { "Pending", "InProgress", "Completed" };

    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly ILogger<MilestoneService> _logger;

    public MilestoneService(ApplicationDbContext context, IScopedAccessService access, ILogger<MilestoneService> logger)
    {
        _context = context;
        _access = access;
        _logger = logger;
    }

    public async Task<List<Milestone>> GetMilestonesAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

        return await _context.Milestones
            .Where(m => m.JobId == jobId)
            .OrderBy(m => m.SortOrder)
            .ToListAsync();
    }

    public async Task<Milestone> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");
        return await FindMilestoneAsync(jobId, milestoneId);
    }

    public async Task<Milestone> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, MilestoneRequest request)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var nextSortOrder = await _context.Milestones
            .Where(m => m.JobId == jobId)
            .Select(m => (int?)m.SortOrder)
            .MaxAsync() ?? -1;

        var callerPersonId = await _access.ResolvePersonIdAsync(callerUserId);

        var milestone = new Milestone
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            Title = request.Title.Trim(),
            Description = request.Description,
            DueDate = request.DueDate,
            Status = "Pending",
            SortOrder = nextSortOrder + 1,
            CreatedBy = callerPersonId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Milestones.AddAsync(milestone);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Milestone {MilestoneId} created for job {JobId} by {UserId}", milestone.Id, jobId, callerUserId);
        return milestone;
    }

    public async Task<Milestone> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, MilestoneRequest request)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var milestone = await FindMilestoneAsync(jobId, milestoneId);
        milestone.Title = request.Title.Trim();
        milestone.Description = request.Description;
        milestone.DueDate = request.DueDate;
        milestone.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return milestone;
    }

    public async Task<Milestone> UpdateStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, string status)
    {
        if (!ValidStatuses.Contains(status))
            throw new ValidationException($"Status must be one of: {string.Join(", ", ValidStatuses)}.");

        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var milestone = await FindMilestoneAsync(jobId, milestoneId);
        milestone.Status = status;
        milestone.UpdatedAt = DateTime.UtcNow;

        if (status == "Completed")
        {
            milestone.CompletedAt = DateTime.UtcNow;
            milestone.CompletedBy = await _access.ResolvePersonIdAsync(callerUserId);
        }
        else
        {
            // Reopening a milestone clears stale completion metadata rather than
            // leaving a CompletedAt/CompletedBy that no longer matches its status.
            milestone.CompletedAt = null;
            milestone.CompletedBy = null;
        }

        await _context.SaveChangesAsync();
        return milestone;
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var milestone = await FindMilestoneAsync(jobId, milestoneId);
        milestone.IsActive = false;
        milestone.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Full-list reorder: caller submits every milestone id in the desired order, and each
    /// gets SortOrder = its index. Requires the submitted set to exactly match the job's
    /// current active milestones - a partial or stale list (e.g. a milestone someone else
    /// just deleted) is rejected rather than silently reordering a subset, which would
    /// leave SortOrder gaps or duplicates.
    /// </summary>
    public async Task<List<Milestone>> ReorderAsync(Guid workspaceId, Guid callerUserId, Guid jobId, List<Guid> orderedMilestoneIds)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var milestones = await _context.Milestones.Where(m => m.JobId == jobId).ToListAsync();
        var byId = milestones.ToDictionary(m => m.Id);

        if (orderedMilestoneIds.Count != milestones.Count || orderedMilestoneIds.Distinct().Count() != orderedMilestoneIds.Count
            || !orderedMilestoneIds.All(byId.ContainsKey))
            throw new ValidationException("The reorder list must contain exactly this job's current milestones, each once.");

        for (var i = 0; i < orderedMilestoneIds.Count; i++)
        {
            var milestone = byId[orderedMilestoneIds[i]];
            milestone.SortOrder = i;
            milestone.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return milestones.OrderBy(m => m.SortOrder).ToList();
    }

    private async Task<Job> FindJobAsync(Guid workspaceId, Guid jobId)
    {
        return await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Job not found");
    }

    private async Task<Milestone> FindMilestoneAsync(Guid jobId, Guid milestoneId)
    {
        return await _context.Milestones.FirstOrDefaultAsync(m => m.Id == milestoneId && m.JobId == jobId)
            ?? throw new NotFoundException("Milestone not found");
    }

}
