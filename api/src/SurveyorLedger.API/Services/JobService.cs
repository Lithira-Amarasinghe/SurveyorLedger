using System.Data;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

/// <summary>
/// Result of AddParticipantAsync - exactly one of the two is set. Access means the grant
/// happened instantly (target already had consent coverage); Invitation means an invite was
/// created instead and nothing is granted until they accept.
/// </summary>
public record ParticipantAddResult(UserAccess? Access, Invitation? Invitation);

public interface IJobService
{
    Task<Job> CreateAsync(Guid workspaceId, Guid callerUserId, JobRequest request);
    Task<List<Job>> GetJobsAsync(Guid workspaceId, Guid callerUserId);
    Task<Job> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId);

    /// <summary>
    /// Cross-workspace single-job fetch for a caller who may not be a workspace member (a
    /// job-only grant) - resolves the job's workspace internally instead of taking it as a
    /// parameter. Same 404-vs-403 order as GetByIdAsync: unknown job -> NotFoundException,
    /// real job with no access -> ForbiddenException (via EnsureJobAccessAsync).
    /// </summary>
    Task<(Job Job, string WorkspaceName)> GetAccessibleJobDetailAsync(Guid callerUserId, Guid jobId);
    Task<Job> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, JobRequest request);
    Task<Job> UpdateStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, string status);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId);

    /// <summary>Assigns an existing account to this job - instant if they already have consent coverage, otherwise creates an invite.</summary>
    Task<ParticipantAddResult> AddParticipantAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid targetUserId, string role);

    /// <summary>Assigns someone by email who wasn't found in a search (may or may not have an account yet) - always creates an invite, same as the workspace invite flow.</summary>
    Task<Invitation> InviteParticipantByEmailAsync(Guid workspaceId, Guid callerUserId, Guid jobId, string role, string email, string? firstName, string? lastName, string? phone, AddressDto? address);

    Task RemoveParticipantAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid targetUserId, string role);
    Task<List<UserAccess>> GetParticipantsAsync(Guid workspaceId, Guid callerUserId, Guid jobId);

    Task AddLandAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid landId);
    Task RemoveLandAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid landId);
    Task<List<Land>> GetLandsAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
}

public class JobService : IJobService
{
    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly IUserAccessGrantService _grantService;
    private readonly IInvitationService _invitationService;
    private readonly ILogger<JobService> _logger;

    public JobService(
        ApplicationDbContext context, IScopedAccessService access, IUserAccessGrantService grantService,
        IInvitationService invitationService, ILogger<JobService> logger)
    {
        _context = context;
        _access = access;
        _grantService = grantService;
        _invitationService = invitationService;
        _logger = logger;
    }

    public async Task<Job> CreateAsync(Guid workspaceId, Guid callerUserId, JobRequest request)
    {
        await _access.EnsureAllowedAsync(callerUserId, "job", "create", workspaceId);
        var createdByPersonId = await _access.ResolvePersonIdAsync(callerUserId);

        var job = new Job
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Title = request.Title.Trim(),
            Description = request.Description,
            Status = "Draft",
            CreatedBy = createdByPersonId,
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
        await _access.EnsureListAllowedAsync(callerUserId, workspaceId);

        if (await _access.HasViewAllAsync(callerUserId, "job", workspaceId))
        {
            return await _context.Jobs
                .Where(j => j.WorkspaceId == workspaceId)
                .OrderByDescending(j => j.CreatedAt)
                .ToListAsync();
        }

        var assignedJobIds = _access.AccessibleJobIds(callerUserId);

        return await _context.Jobs
            .Where(j => j.WorkspaceId == workspaceId && assignedJobIds.Contains(j.Id))
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();
    }

    public async Task<Job> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        var job = await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");
        return job;
    }

    public async Task<(Job Job, string WorkspaceName)> GetAccessibleJobDetailAsync(Guid callerUserId, Guid jobId)
    {
        var job = await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId)
            ?? throw new NotFoundException("Job not found");

        await _access.EnsureJobAccessAsync(callerUserId, job.WorkspaceId, jobId, "view");

        var workspaceName = await _context.Workspaces
            .Where(w => w.Id == job.WorkspaceId)
            .Select(w => w.Name)
            .FirstOrDefaultAsync() ?? "Unknown workspace";

        return (job, workspaceName);
    }

    public async Task<Job> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, JobRequest request)
    {
        var job = await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        job.Title = request.Title.Trim();
        job.Description = request.Description;
        job.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return job;
    }

    public async Task<Job> UpdateStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, string status)
    {
        var job = await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        job.Status = status;
        job.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return job;
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        var job = await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "delete");

        job.IsActive = false;
        job.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Grants job-scoped access: a UserAccess row at ScopeType=Job, ScopeId=jobId, using the
    /// role Admin picks for this specific job (Surveyor or Client) - independent of the
    /// target's workspace role. Instant if the target already has consent coverage for this
    /// job (workspace member, or already on this job under another role); otherwise an
    /// invite is created instead and nothing is granted until they accept - same rule as
    /// ScopedAccessService.HasConsentCoverageAsync everywhere else.
    /// </summary>
    public async Task<ParticipantAddResult> AddParticipantAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid targetUserId, string role)
    {
        var job = await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "manage_participants");

        var jobRole = await ResolveJobRoleAsync(role);

        // targetUserId is normally a Person.Id (picked via system-wide person search, same
        // as ClientService/Land ownership) - a person may not have a UserAccount yet. Also
        // accept a UserAccount.Id directly for callers that already have the account id.
        var targetPerson = await _context.People.FirstOrDefaultAsync(p => p.Id == targetUserId && p.IsActive)
            ?? await _context.UserAccounts.Where(a => a.Id == targetUserId && a.IsActive)
                .Select(a => a.Person).FirstOrDefaultAsync()
            ?? throw new NotFoundException("Person not found");

        var targetAccount = await _context.UserAccounts.FirstOrDefaultAsync(a => a.PersonId == targetPerson.Id && a.IsActive);
        if (targetAccount != null && await _access.HasConsentCoverageAsync(targetAccount.Id, Constants.ScopeTypes.Job, jobId))
        {
            var access = await _grantService.GrantAsync(targetAccount.Id, jobRole.Id, Constants.ScopeTypes.Job, jobId, callerUserId);
            return new ParticipantAddResult(access, null);
        }

        var invitation = await _invitationService.CreateScopedInvitationAsync(
            Constants.ScopeTypes.Job, jobId, jobRole.Id, JobDisplayName(job), callerUserId,
            targetPerson.Email!, targetPerson.FirstName, targetPerson.LastName, targetPerson.Phone, null);
        return new ParticipantAddResult(null, invitation);
    }

    /// <summary>Same invite as above, but for someone found by typing an email rather than picking an existing account - mirrors WorkspaceService's invite-by-email flow, scoped to this job instead of the workspace.</summary>
    public async Task<Invitation> InviteParticipantByEmailAsync(Guid workspaceId, Guid callerUserId, Guid jobId, string role, string email, string? firstName, string? lastName, string? phone, AddressDto? address)
    {
        var job = await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "manage_participants");

        var jobRole = await ResolveJobRoleAsync(role);

        return await _invitationService.CreateScopedInvitationAsync(
            Constants.ScopeTypes.Job, jobId, jobRole.Id, JobDisplayName(job), callerUserId,
            email, firstName, lastName, phone, address);
    }

    private async Task<Role> ResolveJobRoleAsync(string role) =>
        await _context.Roles
            .Where(r => r.Name == role && r.IsSystem)
            .Where(r => r.RoleScopes.Any(rs => rs.ScopeType == Constants.ScopeTypes.Job))
            .FirstOrDefaultAsync()
            ?? throw new AppException(Constants.ErrorCodes.ValidationFailed, $"'{role}' is not a valid job role.", 400);

    private static string JobDisplayName(Job job) => $"{job.JobNumber} · {job.Title}";

    public async Task RemoveParticipantAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid targetUserId, string role)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "manage_participants");

        var jobRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == role && r.IsSystem)
            ?? throw new AppException(Constants.ErrorCodes.ValidationFailed, "Unknown role.", 400);

        await _grantService.RevokeAsync(targetUserId, Constants.ScopeTypes.Job, jobId, jobRole.Id);
    }

    public async Task<List<UserAccess>> GetParticipantsAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

        return await _context.UserAccesses
            .Include(ua => ua.User).ThenInclude(a => a.Person)
            .Include(ua => ua.Role)
            .Where(ua => ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == jobId && ua.IsActive)
            .OrderBy(ua => ua.AssignedAt)
            .ToListAsync();
    }

    public async Task AddLandAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid landId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

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
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

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
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

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

}
