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

    Task<JobParticipant> AddParticipantAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid targetUserId, string participantType);
    Task RemoveParticipantAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid targetUserId);
    Task<List<JobParticipant>> GetParticipantsAsync(Guid workspaceId, Guid callerUserId, Guid jobId);

    Task AddLandAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid landId);
    Task RemoveLandAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid landId);
    Task<List<Land>> GetLandsAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
}

public class JobService : IJobService
{
    private readonly ApplicationDbContext _context;
    private readonly ICasbinService _casbinService;
    private readonly ILogger<JobService> _logger;

    public JobService(ApplicationDbContext context, ICasbinService casbinService, ILogger<JobService> logger)
    {
        _context = context;
        _casbinService = casbinService;
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
    /// Access model: a caller with no workspace role (a client, who is never granted
    /// workspace-level UserAccess) only sees jobs they're a JobParticipant on. Everyone
    /// with a workspace role sees the full workspace job list. This is an explicit
    /// service-level check, not a Casbin policy - Casbin is workspace-scoped and has no
    /// concept of "this specific job," so object-level scoping has to happen here.
    /// </summary>
    public async Task<List<Job>> GetJobsAsync(Guid workspaceId, Guid callerUserId)
    {
        var hasWorkspaceRole = await _context.UserAccesses.AnyAsync(ua =>
            ua.UserId == callerUserId && ua.IsActive &&
            ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId);

        if (!hasWorkspaceRole)
        {
            return await _context.Jobs
                .Where(j => j.WorkspaceId == workspaceId &&
                    j.Participants.Any(p => p.UserId == callerUserId && p.IsActive))
                .OrderByDescending(j => j.CreatedAt)
                .ToListAsync();
        }

        await EnsureAllowedAsync(callerUserId, "view", workspaceId);
        return await _context.Jobs
            .Where(j => j.WorkspaceId == workspaceId)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();
    }

    public async Task<Job> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        var job = await FindJobAsync(workspaceId, jobId);
        await EnsureCanViewJobAsync(callerUserId, workspaceId, job);
        return job;
    }

    public async Task<Job> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, JobRequest request)
    {
        await EnsureAllowedAsync(callerUserId, "edit", workspaceId);
        var job = await FindJobAsync(workspaceId, jobId);

        job.Title = request.Title.Trim();
        job.Description = request.Description;
        job.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return job;
    }

    public async Task<Job> UpdateStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, string status)
    {
        await EnsureAllowedAsync(callerUserId, "edit", workspaceId);
        var job = await FindJobAsync(workspaceId, jobId);

        job.Status = status;
        job.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return job;
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await EnsureAllowedAsync(callerUserId, "delete", workspaceId);
        var job = await FindJobAsync(workspaceId, jobId);

        job.IsActive = false;
        job.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<JobParticipant> AddParticipantAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid targetUserId, string participantType)
    {
        await EnsureAllowedAsync(callerUserId, "edit", workspaceId);
        await FindJobAsync(workspaceId, jobId);

        var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == targetUserId && u.IsActive)
            ?? throw new NotFoundException("User not found");

        var existing = await _context.JobParticipants
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.JobId == jobId && p.UserId == targetUserId && p.ParticipantType == participantType);

        if (existing != null)
        {
            if (existing.IsActive)
                return existing;

            // Re-adding someone previously removed reactivates their row instead of
            // creating a duplicate - the unique index on (JobId, UserId, ParticipantType)
            // would reject a second insert anyway.
            existing.IsActive = true;
            existing.AddedBy = callerUserId;
            existing.AddedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return existing;
        }

        var participant = new JobParticipant
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            UserId = targetUserId,
            ParticipantType = participantType,
            IsActive = true,
            AddedBy = callerUserId,
            AddedAt = DateTime.UtcNow
        };

        await _context.JobParticipants.AddAsync(participant);
        await _context.SaveChangesAsync();
        participant.User = targetUser;
        return participant;
    }

    public async Task RemoveParticipantAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid targetUserId)
    {
        await EnsureAllowedAsync(callerUserId, "edit", workspaceId);
        await FindJobAsync(workspaceId, jobId);

        var participants = await _context.JobParticipants
            .Where(p => p.JobId == jobId && p.UserId == targetUserId && p.IsActive)
            .ToListAsync();

        foreach (var p in participants)
            p.IsActive = false;

        await _context.SaveChangesAsync();
    }

    public async Task<List<JobParticipant>> GetParticipantsAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        var job = await FindJobAsync(workspaceId, jobId);
        await EnsureCanViewJobAsync(callerUserId, workspaceId, job);

        return await _context.JobParticipants
            .Include(p => p.User)
            .Where(p => p.JobId == jobId && p.IsActive)
            .OrderBy(p => p.AddedAt)
            .ToListAsync();
    }

    public async Task AddLandAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid landId)
    {
        await EnsureAllowedAsync(callerUserId, "edit", workspaceId);
        await FindJobAsync(workspaceId, jobId);

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
        await EnsureAllowedAsync(callerUserId, "edit", workspaceId);
        await FindJobAsync(workspaceId, jobId);

        // Soft delete, matching JobParticipant - keeps the "was this land ever linked to
        // this job" history instead of losing it, since RemoveLandAsync used to hard-delete.
        var link = await _context.JobLands.FirstOrDefaultAsync(jl => jl.JobId == jobId && jl.LandId == landId && jl.IsActive);
        if (link == null)
            return;

        link.IsActive = false;
        await _context.SaveChangesAsync();
    }

    public async Task<List<Land>> GetLandsAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        var job = await FindJobAsync(workspaceId, jobId);
        await EnsureCanViewJobAsync(callerUserId, workspaceId, job);

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

    /// <summary>
    /// A caller with a workspace role can view any job in the workspace (subject to
    /// Casbin's job.view policy); a caller without one (a client) can only view jobs
    /// they're an active participant on.
    /// </summary>
    private async Task EnsureCanViewJobAsync(Guid callerUserId, Guid workspaceId, Job job)
    {
        var hasWorkspaceRole = await _context.UserAccesses.AnyAsync(ua =>
            ua.UserId == callerUserId && ua.IsActive &&
            ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId);

        if (hasWorkspaceRole)
        {
            await EnsureAllowedAsync(callerUserId, "view", workspaceId);
            return;
        }

        var isParticipant = await _context.JobParticipants.AnyAsync(p =>
            p.JobId == job.Id && p.UserId == callerUserId && p.IsActive);
        if (!isParticipant)
            throw new ForbiddenException("You do not have permission to view this job.");
    }

    private async Task EnsureAllowedAsync(Guid callerUserId, string action, Guid workspaceId)
    {
        var allowed = await _casbinService.EnforceAsync(callerUserId.ToString(), "job", action, workspaceId.ToString());
        if (!allowed)
            throw new ForbiddenException($"You do not have permission to {action} jobs in this workspace.");
    }
}
