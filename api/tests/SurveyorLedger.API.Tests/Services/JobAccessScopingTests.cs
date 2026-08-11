using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// Job-scoped UserAccess access model (see JobService): Admin/Manager see every job via
/// job.view_all; Surveyor/Client see only jobs they hold a job-scoped UserAccess row for.
/// </summary>
public class JobAccessScopingTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private Guid _jobAId;
    private Guid _jobBId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
    }

    private async Task SeedJobsAsync()
    {
        _jobService = GetService<IJobService>();

        var jobA = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        var jobB = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job B" });
        _jobAId = jobA.Id;
        _jobBId = jobB.Id;

        // Surveyor assigned to Job A only; Client gets nothing.
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobAId, SurveyorId);
    }

    [Fact]
    public async Task Admin_SeesAllJobs_WithoutExplicitAssignment()
    {
        await SeedJobsAsync();
        var jobs = await _jobService.GetJobsAsync(WorkspaceId, AdminId);
        Assert.Equal(2, jobs.Count);
    }

    [Fact]
    public async Task Surveyor_SeesOnlyAssignedJob()
    {
        await SeedJobsAsync();
        var jobs = await _jobService.GetJobsAsync(WorkspaceId, SurveyorId);
        var job = Assert.Single(jobs);
        Assert.Equal(_jobAId, job.Id);
    }

    [Fact]
    public async Task Client_WithNoAssignment_SeesNoJobs()
    {
        await SeedJobsAsync();
        var jobs = await _jobService.GetJobsAsync(WorkspaceId, ClientId);
        Assert.Empty(jobs);
    }

    [Fact]
    public async Task Surveyor_CanViewAssignedJob()
    {
        await SeedJobsAsync();
        var job = await _jobService.GetByIdAsync(WorkspaceId, SurveyorId, _jobAId);
        Assert.Equal(_jobAId, job.Id);
    }

    [Fact]
    public async Task Surveyor_CannotViewUnassignedJob()
    {
        await SeedJobsAsync();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _jobService.GetByIdAsync(WorkspaceId, SurveyorId, _jobBId));
    }

    [Fact]
    public async Task Surveyor_CannotEditUnassignedJob_EvenThoughRoleGrantsJobEditWorkspaceWide()
    {
        // Regression guard: Surveyor's workspace role grants job.edit workspace-wide in
        // Casbin, but that alone must not be enough to touch a job they aren't assigned to.
        await SeedJobsAsync();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _jobService.UpdateAsync(WorkspaceId, SurveyorId, _jobBId, new JobRequest { Title = "Hijacked" }));
    }

    [Fact]
    public async Task Surveyor_CanEditAssignedJob()
    {
        await SeedJobsAsync();
        var updated = await _jobService.UpdateAsync(WorkspaceId, SurveyorId, _jobAId, new JobRequest { Title = "Updated by surveyor" });
        Assert.Equal("Updated by surveyor", updated.Title);
    }

    [Fact]
    public async Task RemovingParticipant_RevokesJobVisibility()
    {
        await SeedJobsAsync();
        await _jobService.RemoveParticipantAsync(WorkspaceId, AdminId, _jobAId, SurveyorId);

        var jobs = await _jobService.GetJobsAsync(WorkspaceId, SurveyorId);
        Assert.Empty(jobs);
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _jobService.GetByIdAsync(WorkspaceId, SurveyorId, _jobAId));
    }

    [Fact]
    public async Task AddParticipant_RejectsUserWithNoWorkspaceMembership()
    {
        await SeedJobsAsync();
        var outsiderId = Guid.NewGuid();
        await Context.Users.AddAsync(new User
        {
            Id = outsiderId,
            FirstName = "Outsider",
            LastName = "Person",
            EmailVerified = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();

        await Assert.ThrowsAsync<AppException>(
            () => _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobBId, outsiderId));
    }
}
