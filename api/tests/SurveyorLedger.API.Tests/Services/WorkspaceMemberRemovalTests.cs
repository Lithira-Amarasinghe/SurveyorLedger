using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// Regression: removing a workspace member must also revoke their job-scope UserAccess
/// rows for that workspace's jobs. Workspace-scope and job-scope grants are separate
/// UserAccess rows - removing one alone would leave the removed person still showing as
/// having access to jobs they were assigned to, despite no longer being a member at all.
/// </summary>
public class WorkspaceMemberRemovalTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IWorkspaceService _workspaceService = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IWorkspaceService, WorkspaceService>();
    }

    [Fact]
    public async Task RemoveMember_AlsoRevokesTheirJobAssignments()
    {
        _jobService = GetService<IJobService>();
        _workspaceService = GetService<IWorkspaceService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, job.Id, SurveyorId);

        var jobAccessBefore = await Context.UserAccesses.AnyAsync(ua =>
            ua.UserId == SurveyorId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == job.Id);
        Assert.True(jobAccessBefore);

        await _workspaceService.RemoveMemberAsync(WorkspaceId, SurveyorId, AdminId);

        var workspaceAccessAfter = await Context.UserAccesses.AnyAsync(ua =>
            ua.UserId == SurveyorId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == WorkspaceId);
        Assert.False(workspaceAccessAfter);

        var jobAccessAfter = await Context.UserAccesses.AnyAsync(ua =>
            ua.UserId == SurveyorId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == job.Id);
        Assert.False(jobAccessAfter);
    }
}
