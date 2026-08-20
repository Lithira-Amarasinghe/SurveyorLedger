using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.Milestone;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// Milestone access mirrors JobAccessScopingTests: job.edit (Admin/Surveyor) is needed
/// to mutate, job.view (everyone incl. Client) to read, and unless the caller holds
/// job.view_all (Admin), they must hold a job-scoped UserAccess row for the specific
/// job the milestone belongs to.
/// </summary>
public class MilestoneServiceTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IMilestoneService _milestoneService = null!;
    private Guid _jobAId;
    private Guid _jobBId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IMilestoneService, MilestoneService>();
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
        _milestoneService = GetService<IMilestoneService>();

        var jobA = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        var jobB = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job B" });
        _jobAId = jobA.Id;
        _jobBId = jobB.Id;

        // Surveyor and Client both assigned to Job A only; neither is assigned to Job B.
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobAId, SurveyorId, "Surveyor");
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobAId, ClientId, "Client");
    }

    [Fact]
    public async Task Admin_CanCreateMilestone_OnAnyJob_WithoutExplicitAssignment()
    {
        await SeedJobsAsync();
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobBId, new MilestoneRequest { Title = "Site Visit" });
        Assert.Equal("Site Visit", milestone.Title);
        Assert.Equal("Pending", milestone.Status);
    }

    [Fact]
    public async Task Surveyor_CanCreateMilestone_OnAssignedJob()
    {
        await SeedJobsAsync();
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, SurveyorId, _jobAId, new MilestoneRequest { Title = "Survey Complete" });
        Assert.Equal("Survey Complete", milestone.Title);
    }

    [Fact]
    public async Task Surveyor_CannotCreateMilestone_OnUnassignedJob()
    {
        // Regression guard: Surveyor's role grants job.edit workspace-wide in Casbin,
        // but that alone must not be enough to add a milestone to a job they aren't
        // assigned to.
        await SeedJobsAsync();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _milestoneService.CreateAsync(WorkspaceId, SurveyorId, _jobBId, new MilestoneRequest { Title = "Hijacked" }));
    }

    [Fact]
    public async Task Client_CannotCreateMilestone_EvenOnAssignedJob()
    {
        await SeedJobsAsync();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _milestoneService.CreateAsync(WorkspaceId, ClientId, _jobAId, new MilestoneRequest { Title = "Not allowed" }));
    }

    [Fact]
    public async Task Client_CanViewMilestones_OnAssignedJob()
    {
        await SeedJobsAsync();
        await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "Site Visit" });

        var milestones = await _milestoneService.GetMilestonesAsync(WorkspaceId, ClientId, _jobAId);
        var milestone = Assert.Single(milestones);
        Assert.Equal("Site Visit", milestone.Title);
    }

    [Fact]
    public async Task Client_CannotViewMilestones_OnUnassignedJob()
    {
        await SeedJobsAsync();
        await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobBId, new MilestoneRequest { Title = "Site Visit" });

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _milestoneService.GetMilestonesAsync(WorkspaceId, ClientId, _jobBId));
    }

    [Fact]
    public async Task Admin_CanViewMilestones_OnAnyJob_WithoutExplicitAssignment()
    {
        await SeedJobsAsync();
        await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobBId, new MilestoneRequest { Title = "Site Visit" });

        var milestones = await _milestoneService.GetMilestonesAsync(WorkspaceId, AdminId, _jobBId);
        Assert.Single(milestones);
    }

    [Fact]
    public async Task CompletingMilestone_StampsCompletedAtAndCompletedBy()
    {
        await SeedJobsAsync();
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "Deed Verified" });

        var completed = await _milestoneService.UpdateStatusAsync(WorkspaceId, SurveyorId, _jobAId, milestone.Id, "Completed");

        Assert.Equal("Completed", completed.Status);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(SurveyorPersonId, completed.CompletedBy);
    }

    [Fact]
    public async Task ReopeningMilestone_ClearsCompletionMetadata()
    {
        await SeedJobsAsync();
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "Deed Verified" });
        await _milestoneService.UpdateStatusAsync(WorkspaceId, AdminId, _jobAId, milestone.Id, "Completed");

        var reopened = await _milestoneService.UpdateStatusAsync(WorkspaceId, AdminId, _jobAId, milestone.Id, "InProgress");

        Assert.Equal("InProgress", reopened.Status);
        Assert.Null(reopened.CompletedAt);
        Assert.Null(reopened.CompletedBy);
    }

    [Fact]
    public async Task InvalidStatus_ThrowsValidationException()
    {
        await SeedJobsAsync();
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "Deed Verified" });

        await Assert.ThrowsAsync<ValidationException>(
            () => _milestoneService.UpdateStatusAsync(WorkspaceId, AdminId, _jobAId, milestone.Id, "Bogus"));
    }

    [Fact]
    public async Task DeletedMilestone_IsSoftDeleted_AndExcludedFromList()
    {
        await SeedJobsAsync();
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "Site Visit" });

        await _milestoneService.DeleteAsync(WorkspaceId, AdminId, _jobAId, milestone.Id);

        var milestones = await _milestoneService.GetMilestonesAsync(WorkspaceId, AdminId, _jobAId);
        Assert.Empty(milestones);
    }

    [Fact]
    public async Task MilestoneFromDifferentJob_Returns404()
    {
        await SeedJobsAsync();
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "Site Visit" });

        await Assert.ThrowsAsync<NotFoundException>(
            () => _milestoneService.GetByIdAsync(WorkspaceId, AdminId, _jobBId, milestone.Id));
    }

    [Fact]
    public async Task JobFromDifferentWorkspace_Returns404()
    {
        await SeedJobsAsync();
        await Assert.ThrowsAsync<NotFoundException>(
            () => _milestoneService.GetMilestonesAsync(Guid.NewGuid(), AdminId, _jobAId));
    }

    [Fact]
    public async Task Reorder_PersistsNewSortOrder()
    {
        await SeedJobsAsync();
        var first = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "First" });
        var second = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "Second" });

        await _milestoneService.ReorderAsync(WorkspaceId, AdminId, _jobAId, new List<Guid> { second.Id, first.Id });

        var milestones = await _milestoneService.GetMilestonesAsync(WorkspaceId, AdminId, _jobAId);
        Assert.Equal(new[] { "Second", "First" }, milestones.Select(m => m.Title));
    }

    [Fact]
    public async Task Reorder_RejectsListNotMatchingCurrentMilestones()
    {
        await SeedJobsAsync();
        var first = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "First" });
        await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "Second" });

        await Assert.ThrowsAsync<ValidationException>(
            () => _milestoneService.ReorderAsync(WorkspaceId, AdminId, _jobAId, new List<Guid> { first.Id }));
    }

    [Fact]
    public async Task Surveyor_CannotReorder_OnUnassignedJob()
    {
        await SeedJobsAsync();
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobBId, new MilestoneRequest { Title = "Site Visit" });

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _milestoneService.ReorderAsync(WorkspaceId, SurveyorId, _jobBId, new List<Guid> { milestone.Id }));
    }

    [Fact]
    public async Task CreateAsync_SetsCreatedByUser_AsPersonNotUserAccount()
    {
        await SeedJobsAsync();
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest
        {
            Title = "Site visit"
        });

        var loaded = await Context.Milestones.Include(m => m.CreatedByUser).FirstAsync(m => m.Id == milestone.Id);
        Assert.IsType<SurveyorLedger.Data.Entities.Person>(loaded.CreatedByUser);
        Assert.Equal("Admin", loaded.CreatedByUser.FirstName);
        Assert.NotEqual(AdminId, loaded.CreatedBy); // CreatedBy is the Person.Id, not the caller's UserAccount.Id
    }

    [Fact]
    public async Task Amount_IsPersisted_AndDefaultsToNull()
    {
        await SeedJobsAsync();
        var withAmount = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "Deed Verified", Amount = 25000m });
        var withoutAmount = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "Site Visit" });

        Assert.Equal(25000m, withAmount.Amount);
        Assert.Null(withoutAmount.Amount);
    }
}
