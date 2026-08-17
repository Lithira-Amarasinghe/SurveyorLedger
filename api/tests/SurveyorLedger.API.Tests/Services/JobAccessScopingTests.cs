using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
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
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AppSettings:UiBaseUrl"] = "https://test.local" })
                .Build());
    }

    private async Task SeedJobsAsync()
    {
        _jobService = GetService<IJobService>();

        var jobA = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        var jobB = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job B" });
        _jobAId = jobA.Id;
        _jobBId = jobB.Id;

        // Surveyor assigned to Job A only; Client gets nothing.
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobAId, SurveyorId, "Surveyor");
    }

    [Fact]
    public async Task Surveyor_CannotCreateJob()
    {
        _jobService = GetService<IJobService>();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _jobService.CreateAsync(WorkspaceId, SurveyorId, new JobRequest { Title = "Unauthorized job" }));
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
        await _jobService.RemoveParticipantAsync(WorkspaceId, AdminId, _jobAId, SurveyorId, "Surveyor");

        var jobs = await _jobService.GetJobsAsync(WorkspaceId, SurveyorId);
        Assert.Empty(jobs);
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _jobService.GetByIdAsync(WorkspaceId, SurveyorId, _jobAId));
    }

    [Fact]
    public async Task AddParticipant_WithNoWorkspaceMembership_CreatesWorkspaceInviteWithJobDescendant()
    {
        // No consent coverage (not a workspace member, no existing job grant) means
        // AddParticipantAsync falls back to an invite rather than an instant grant or a
        // rejection. Surveyor chains to WorkspaceMember, so the invite itself is created at
        // the highest level that will actually be granted (Workspace) - the specific job
        // assignment rides along as the invitation's descendant grant.
        await SeedJobsAsync();
        var outsiderId = await CreateUserAccountAsync("Outsider", "Person", "outsider@test.local");

        var result = await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobBId, outsiderId, "Surveyor");
        Assert.Null(result.Access);
        Assert.NotNull(result.Invitation);
        Assert.Equal(Constants.ScopeTypes.Workspace, result.Invitation!.ScopeType);
        Assert.Equal(WorkspaceId, result.Invitation.ScopeId);
        Assert.Equal(Constants.ScopeTypes.Job, result.Invitation.DescendantScopeType);
        Assert.Equal(_jobBId, result.Invitation.DescendantScopeId);
    }

    [Fact]
    public async Task AddParticipant_ClientRole_NoAncestor_StillInvitesAtJobScope()
    {
        // Client (SingleScope policy) has no ancestor to chain to - Job stays the only level
        // that matters, no descendant needed. Regression guard: the new ancestor-lookup logic
        // must not change behavior for roles that were never meant to chain.
        await SeedJobsAsync();
        var outsiderId = await CreateUserAccountAsync("Outsider", "Client", "outsiderclient@test.local");

        var result = await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobBId, outsiderId, "Client");
        Assert.Null(result.Access);
        Assert.NotNull(result.Invitation);
        Assert.Equal(Constants.ScopeTypes.Job, result.Invitation!.ScopeType);
        Assert.Null(result.Invitation.DescendantScopeType);
    }

    [Fact]
    public async Task Surveyor_CannotAddParticipant_EvenOnTheirOwnAssignedJob()
    {
        // job.edit lets a Surveyor update the job itself, but managing who's assigned to it
        // is a separate permission (job.manage_participants) that only Admin holds - staffed
        // work access must not imply people-management access.
        await SeedJobsAsync();
        var outsiderId = await CreateUserAccountAsync("Outsider", "Person", "outsider2@test.local");

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _jobService.AddParticipantAsync(WorkspaceId, SurveyorId, _jobAId, outsiderId, "Client"));
    }

    [Fact]
    public async Task Surveyor_CannotRemoveParticipant_EvenOnTheirOwnAssignedJob()
    {
        await SeedJobsAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _jobService.RemoveParticipantAsync(WorkspaceId, SurveyorId, _jobAId, SurveyorId, "Surveyor"));
    }

    [Fact]
    public async Task Admin_CanAddParticipant()
    {
        // Confirms the permission was actually granted to Admin, not just withheld from
        // Surveyor - a mis-seeded RolePermission row would fail this even though the two
        // tests above would still pass. ClientId already holds workspace-level access (see
        // WorkspaceIntegrationTestBase seed), so this hits the instant-grant path rather
        // than falling back to an invite.
        await SeedJobsAsync();

        var result = await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobBId, ClientId, "Client");
        Assert.NotNull(result.Access);
    }
}
