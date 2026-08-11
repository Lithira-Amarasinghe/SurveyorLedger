using System.Data;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IJobService
{
    Task<Job> CreateAsync(Guid workspaceId, Guid callerUserId, JobRequest request);
    Task<List<Job>> GetJobsAsync(Guid workspaceId, Guid callerUserId);
    Task<Job> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
    Task<Job> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, JobRequest request);
    Task<Job> UpdateStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, string status);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId);

    Task<UserAccess> AddParticipantAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid targetUserId);
    Task RemoveParticipantAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid targetUserId);
    Task<List<UserAccess>> GetParticipantsAsync(Guid workspaceId, Guid callerUserId, Guid jobId);

    Task AddLandAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid landId);
    Task RemoveLandAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid landId);
    Task<List<Land>> GetLandsAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
}

public class JobService : IJobService
{
    private readonly ApplicationDbContext _context;
    private readonly ICasbinService _casbinService;
    private readonly IUserAccessGrantService _grantService;
    private readonly ILogger<JobService> _logger;

    public JobService(ApplicationDbContext context, ICasbinService casbinService, IUserAccessGrantService grantService, ILogger<JobService> logger)
    {
        _context = context;
        _casbinService = casbinService;
        _grantService = grantService;
        _logger = logger;
    }

    public async Task<Job> CreateAsync(Guid workspaceId, Guid callerUserId, JobRequest request)
    {
        await EnsureAllowedAsync(callerUserId, "create", workspaceId);

        var job = new Job
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Title = request.Title.Trim(),
            Description = request.Description,
            Status = "Draft",
            CreatedBy = callerUserId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Serializable so two concurrent creates in the same workspace can't both read the
        // same COUNT and generate the same JobNumber - the second commit would otherwise
        // race the first past the read before hitting the unique index on insert. Must run
        // through the execution strategy (EnableRetryOnFailure), which owns retry boundaries
        // and won't accept a bare BeginTransactionAsync.
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            job.JobNumber = await NextJobNumberAsync(workspaceId);
            await _context.Jobs.AddAsync(job);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        _logger.LogInformation("Job {JobId} ({JobNumber}) created in workspace {WorkspaceId} by {UserId}", job.Id, job.JobNumber, workspaceId, callerUserId);
        return job;
    }

    /// <summary>
    /// Access model: job-level access is granted via a UserAccess row scoped to the job
    /// (ScopeType = Job, ScopeId = jobId) rather than a separate participants table - see
    /// AddParticipantAsync. A caller holding job.view_all at the workspace scope (Admin,
    /// Manager) sees every job; everyone else sees only jobs they hold a job-scoped
    /// UserAccess row for.
    /// </summary>
    public async Task<List<Job>> GetJobsAsync(Guid workspaceId, Guid callerUserId)
    {
        await EnsureAllowedAsync(callerUserId, "view", workspaceId);

        if (await HasFullJobAccessAsync(callerUserId, workspaceId))
        {
            return await _context.Jobs
                .Where(j => j.WorkspaceId == workspaceId)
                .OrderByDescending(j => j.CreatedAt)
                .ToListAsync();
        }

        var assignedJobIds = _context.UserAccesses
            .Where(ua => ua.UserId == callerUserId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Job)
            .Select(ua => ua.ScopeId);

        return await _context.Jobs
            .Where(j => j.WorkspaceId == workspaceId && assignedJobIds.Contains(j.Id))
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();
    }

    public async Task<Job> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        var job = await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");
        return job;
    }

    public async Task<Job> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, JobRequest request)
    {
        var job = await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        job.Title = request.Title.Trim();
        job.Description = request.Description;
        job.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return job;
    }

    public async Task<Job> UpdateStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, string status)
    {
        var job = await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        job.Status = status;
        job.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return job;
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        var job = await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "delete");

        job.IsActive = false;
        job.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Grants job-scoped access: a UserAccess row at ScopeType=Job, ScopeId=jobId, using the
    /// target's existing workspace role (no separate job-level role - see plan discussion).
    /// The target must already be a workspace member; job assignment doesn't create members.
    /// </summary>
    public async Task<UserAccess> AddParticipantAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid targetUserId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var workspaceAccess = await _context.UserAccesses
            .FirstOrDefaultAsync(ua => ua.UserId == targetUserId && ua.IsActive &&
                ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId)
            ?? throw new AppException(Constants.ErrorCodes.UserNotFound,
                "This person isn't a member of the workspace yet - add them as a member before assigning them to a job.", 400);

        return await _grantService.GrantAsync(targetUserId, workspaceAccess.RoleId, Constants.ScopeTypes.Job, jobId, callerUserId);
    }

    public async Task RemoveParticipantAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid targetUserId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        await _grantService.RevokeAsync(targetUserId, Constants.ScopeTypes.Job, jobId);
    }

    public async Task<List<UserAccess>> GetParticipantsAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

        return await _context.UserAccesses
            .Include(ua => ua.User)
            .Include(ua => ua.Role)
            .Where(ua => ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == jobId && ua.IsActive)
            .OrderBy(ua => ua.AssignedAt)
            .ToListAsync();
    }

    public async Task AddLandAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid landId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var land = await _context.Lands.FirstOrDefaultAsync(l => l.Id == landId && l.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Land not found");

        var existing = await _context.JobLands.FirstOrDefaultAsync(jl => jl.JobId == jobId && jl.LandId == landId);
        if (existing != null)
        {
            if (!existing.IsActive)
            {
                existing.IsActive = true;
                await _context.SaveChangesAsync();
            }
            return;
        }

        await _context.JobLands.AddAsync(new JobLand
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            LandId = land.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    public async Task RemoveLandAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid landId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        // Soft delete, matching the job-access model - keeps the "was this land ever linked
        // to this job" history instead of losing it.
        var link = await _context.JobLands.FirstOrDefaultAsync(jl => jl.JobId == jobId && jl.LandId == landId && jl.IsActive);
        if (link == null)
            return;

        link.IsActive = false;
        await _context.SaveChangesAsync();
    }

    public async Task<List<Land>> GetLandsAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

        return await _context.JobLands
            .Where(jl => jl.JobId == jobId && jl.IsActive)
            .Select(jl => jl.Land)
            .ToListAsync();
    }

    /// <summary>
    /// Job numbers are workspace-scoped and sequential (JOB-0001, JOB-0002, ...).
    /// Reads the current max under the caller's transaction - fine at this scale;
    /// revisit with a dedicated counter if concurrent job creation becomes frequent.
    /// </summary>
    private async Task<string> NextJobNumberAsync(Guid workspaceId)
    {
        var count = await _context.Jobs.IgnoreQueryFilters().CountAsync(j => j.WorkspaceId == workspaceId);
        return $"JOB-{count + 1:D4}";
    }

    private async Task<Job> FindJobAsync(Guid workspaceId, Guid jobId)
    {
        return await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Job not found");
    }

    private Task<bool> HasFullJobAccessAsync(Guid callerUserId, Guid workspaceId) =>
        _casbinService.EnforceAsync(callerUserId.ToString(), "job", "view_all", workspaceId.ToString());

    private Task<bool> IsAssignedToJobAsync(Guid callerUserId, Guid jobId) =>
        _context.UserAccesses.AnyAsync(ua =>
            ua.UserId == callerUserId && ua.IsActive &&
            ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == jobId);

    /// <summary>
    /// Two-part check for any per-job action: the caller's workspace role must grant the
    /// action at all (Casbin, workspace-scoped), and unless the role also grants
    /// job.view_all (full workspace visibility - Admin), the caller must hold an
    /// explicit job-scoped UserAccess row for this specific job. Applies uniformly to
    /// view, edit, and delete so a role with workspace-wide job.edit still can't touch a
    /// job it isn't assigned to and can't see.
    /// </summary>
    private async Task EnsureJobAccessAsync(Guid callerUserId, Guid workspaceId, Guid jobId, string action)
    {
        await EnsureAllowedAsync(callerUserId, action, workspaceId);
        if (await HasFullJobAccessAsync(callerUserId, workspaceId))
            return;
        if (!await IsAssignedToJobAsync(callerUserId, jobId))
            throw new ForbiddenException($"You do not have permission to {action} this job.");
    }

    private async Task EnsureAllowedAsync(Guid callerUserId, string action, Guid workspaceId)
    {
        var allowed = await _casbinService.EnforceAsync(callerUserId.ToString(), "job", action, workspaceId.ToString());
        if (!allowed)
            throw new ForbiddenException($"You do not have permission to {action} jobs in this workspace.");
    }
}
