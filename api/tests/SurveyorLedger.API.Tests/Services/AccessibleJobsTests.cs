using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Data.Configurations;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// ScopedAccessService.GetAccessibleJobsAsync - the cross-workspace "what jobs can this
/// user open" query backing the dashboard's Jobs list. Broadest-level-wins, deduped.
/// </summary>
public class AccessibleJobsTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IScopedAccessService _access = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppSettings:UiBaseUrl"] = "https://test.local" })
            .Build());
    }

    [Fact]
    public async Task Admin_SeesWorkspaceJobs_TaggedWorkspaceLevel()
    {
        _jobService = GetService<IJobService>();
        _access = GetService<IScopedAccessService>();
        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });

        var jobs = await _access.GetAccessibleJobsAsync(AdminId);

        var result = Assert.Single(jobs);
        Assert.Equal(job.Id, result.JobId);
        Assert.Equal(Constants.ScopeTypes.Workspace, result.AccessScopeType);
        Assert.Equal(WorkspaceId, result.WorkspaceId);
    }

    [Fact]
    public async Task GetAccessibleJobsAsync_IncludesOrganizationId()
    {
        _jobService = GetService<IJobService>();
        _access = GetService<IScopedAccessService>();
        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });

        var jobs = await _access.GetAccessibleJobsAsync(AdminId);

        var result = Assert.Single(jobs);
        Assert.Equal(job.Id, result.JobId);
        Assert.NotEqual(Guid.Empty, result.OrganizationId);
    }

    [Fact]
    public async Task PlainMember_WithNoJobViewAll_AndNoDirectGrant_SeesNoJobs()
    {
        // ClientId from the base fixture is a plain workspace Member - has a Workspace-scope
        // UserAccess row, but Member does not carry job.view_all. This is the exact case the
        // spec's "qualifying grant" definition exists to get right: holding a UserAccess row
        // at a level is NOT the same as holding a qualifying grant at that level.
        _jobService = GetService<IJobService>();
        _access = GetService<IScopedAccessService>();
        await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });

        var jobs = await _access.GetAccessibleJobsAsync(ClientId);

        Assert.Empty(jobs);
    }

    [Fact]
    public async Task DirectJobGrant_WithoutWorkspaceMembership_TaggedJobLevel()
    {
        _jobService = GetService<IJobService>();
        _access = GetService<IScopedAccessService>();
        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });

        var jobOnlyUserId = await CreateUserAccountAsync("Job", "Only", "jobonly@test.local");
        await GrantService.GrantAsync(jobOnlyUserId, RoleConfiguration.ClientRoleId, Constants.ScopeTypes.Job, job.Id, AdminId);

        var jobs = await _access.GetAccessibleJobsAsync(jobOnlyUserId);

        var result = Assert.Single(jobs);
        Assert.Equal(job.Id, result.JobId);
        Assert.Equal(Constants.ScopeTypes.Job, result.AccessScopeType);
    }

    [Fact]
    public async Task WorkspaceLevelAndDirectGrant_DedupesToWorkspaceLevel()
    {
        // Admin already sees every job via job.view_all; explicitly adding them as a job
        // participant too must not produce a duplicate row or downgrade the reported level.
        _jobService = GetService<IJobService>();
        _access = GetService<IScopedAccessService>();
        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, job.Id, AdminId, "Surveyor");

        var jobs = await _access.GetAccessibleJobsAsync(AdminId);

        var result = Assert.Single(jobs);
        Assert.Equal(Constants.ScopeTypes.Workspace, result.AccessScopeType);
    }

    [Fact]
    public async Task GetAccessibleJobDetail_JobOnlyUser_ReturnsJobAndWorkspaceName()
    {
        _jobService = GetService<IJobService>();
        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });

        var jobOnlyUserId = await CreateUserAccountAsync("Job", "Only", "jobonly2@test.local");
        await GrantService.GrantAsync(jobOnlyUserId, RoleConfiguration.ClientRoleId, Constants.ScopeTypes.Job, job.Id, AdminId);

        var (result, workspaceName) = await _jobService.GetAccessibleJobDetailAsync(jobOnlyUserId, job.Id);

        Assert.Equal(job.Id, result.Id);
        Assert.Equal("Test Workspace", workspaceName);
    }

    [Fact]
    public async Task GetAccessibleJobDetail_NoAccess_ThrowsForbidden()
    {
        _jobService = GetService<IJobService>();
        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });

        var strangerId = await CreateUserAccountAsync("Stranger", "Person", "stranger@test.local");

        await Assert.ThrowsAsync<SurveyorLedger.Core.Exceptions.ForbiddenException>(
            () => _jobService.GetAccessibleJobDetailAsync(strangerId, job.Id));
    }

    [Fact]
    public async Task GetAccessibleJobDetail_UnknownJobId_ThrowsNotFound()
    {
        _jobService = GetService<IJobService>();

        await Assert.ThrowsAsync<SurveyorLedger.Core.Exceptions.NotFoundException>(
            () => _jobService.GetAccessibleJobDetailAsync(AdminId, Guid.NewGuid()));
    }
}
