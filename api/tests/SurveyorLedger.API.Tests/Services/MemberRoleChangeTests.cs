using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Data.Configurations;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// Regression: changing a member's workspace role must re-role their job-scope grants too.
/// Job grants carry their own copy of the role (AddParticipantAsync copies it at assignment
/// time), and Casbin enforces the job-scope grouping independently - so leaving them behind
/// means a demoted member keeps their old, higher permissions on assigned jobs.
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
    public async Task ChangingWorkspaceRole_AlsoRerolesJobScopeGrants()
    {
        _jobService = GetService<IJobService>();
        _workspaceService = GetService<IWorkspaceService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, job.Id, SurveyorId);

        var grantBefore = await Context.UserAccesses.FirstAsync(ua =>
            ua.UserId == SurveyorId && ua.IsActive &&
            ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == job.Id);
        Assert.Equal(RoleConfiguration.SurveyorRoleId, grantBefore.RoleId);

        await _workspaceService.UpdateMemberRoleAsync(WorkspaceId, SurveyorId, AdminId, Constants.SystemRoles.Client);

        var grantAfter = await Context.UserAccesses.FirstAsync(ua =>
            ua.UserId == SurveyorId && ua.IsActive &&
            ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == job.Id);
        Assert.Equal(RoleConfiguration.ClientRoleId, grantAfter.RoleId);
    }

    [Fact]
    public async Task DemotedMember_LosesElevatedPermissionOnTheirAssignedJob()
    {
        // The point of the fix: Casbin must stop granting the old role at job scope, not
        // just at workspace scope. A Client cannot edit land records; a Surveyor can.
        _jobService = GetService<IJobService>();
        _workspaceService = GetService<IWorkspaceService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, job.Id, SurveyorId);

        Assert.True(await CasbinService.EnforceAsync(SurveyorId.ToString(), "land", "edit", job.Id.ToString()));

        await _workspaceService.UpdateMemberRoleAsync(WorkspaceId, SurveyorId, AdminId, Constants.SystemRoles.Client);

        Assert.False(await CasbinService.EnforceAsync(SurveyorId.ToString(), "land", "edit", job.Id.ToString()));
    }
}
