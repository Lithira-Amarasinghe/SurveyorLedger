using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Data.Configurations;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// Workspace role and job role are independent facts now - a job-scope grant (Surveyor or
/// Client, picked explicitly by Admin at assignment time) is no longer derived from the
/// workspace role, so changing someone's workspace role must NOT touch their existing
/// job-scope grants.
/// </summary>
public class MemberRoleChangeTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IWorkspaceService _workspaceService = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IWorkspaceService, WorkspaceService>();
    }

    [Fact]
    public async Task ChangingWorkspaceRole_DoesNotTouchExistingJobScopeGrants()
    {
        _jobService = GetService<IJobService>();
        _workspaceService = GetService<IWorkspaceService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, job.Id, SurveyorId, "Surveyor");

        var grantBefore = await Context.UserAccesses.FirstAsync(ua =>
            ua.UserId == SurveyorId && ua.IsActive &&
            ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == job.Id);
        Assert.Equal(RoleConfiguration.SurveyorRoleId, grantBefore.RoleId);

        await _workspaceService.UpdateMemberRoleAsync(WorkspaceId, SurveyorId, AdminId, Constants.SystemRoles.Member);

        var grantAfter = await Context.UserAccesses.FirstAsync(ua =>
            ua.UserId == SurveyorId && ua.IsActive &&
            ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == job.Id);
        Assert.Equal(RoleConfiguration.SurveyorRoleId, grantAfter.RoleId);
    }

    [Fact]
    public async Task DemotedAtWorkspace_KeepsTheirJobScopePermissionOnAssignedJob()
    {
        // The job grant is its own independent fact - demoting someone to Member at
        // workspace level must not strip a Surveyor job-scope grant they were explicitly
        // assigned. Casbin enforces the job-scope grouping on its own, unaffected by the
        // workspace-scope change.
        _jobService = GetService<IJobService>();
        _workspaceService = GetService<IWorkspaceService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, job.Id, SurveyorId, "Surveyor");

        Assert.True(await CasbinService.EnforceAsync(SurveyorId.ToString(), "land", "edit", job.Id.ToString()));

        await _workspaceService.UpdateMemberRoleAsync(WorkspaceId, SurveyorId, AdminId, Constants.SystemRoles.Member);

        Assert.True(await CasbinService.EnforceAsync(SurveyorId.ToString(), "land", "edit", job.Id.ToString()));
    }
}
