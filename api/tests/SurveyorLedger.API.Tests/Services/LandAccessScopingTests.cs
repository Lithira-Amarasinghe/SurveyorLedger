using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// Land has no scope grant of its own - it's reached through the jobs it's linked to (see
/// ScopedAccessService.EnsureLandAccessAsync / AccessibleLandIds). These tests are the
/// regression net for the leak that existed before that check: a Client with zero job
/// assignments could list and read every land record in the workspace.
/// </summary>
public class LandAccessScopingTests : WorkspaceIntegrationTestBase
{
    private ILandService _landService = null!;
    private IJobService _jobService = null!;
    private Guid _jobAId;
    private Guid _jobBId;
    private Guid _landOnJobAId;
    private Guid _landOnJobBId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ILandService, LandService>();
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-landaccess-test-{Guid.NewGuid():N}"),
                    ["AppSettings:UiBaseUrl"] = "https://test.local"
                })
                .Build());
    }

    private async Task SeedAsync()
    {
        _landService = GetService<ILandService>();
        _jobService = GetService<IJobService>();

        var jobA = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        var jobB = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job B" });
        _jobAId = jobA.Id;
        _jobBId = jobB.Id;

        var landOnA = await _landService.CreateAsync(WorkspaceId, AdminId, new LandRequest
        {
            Address = new AddressDto { Street = "1 Land A Street" }
        });
        var landOnB = await _landService.CreateAsync(WorkspaceId, AdminId, new LandRequest
        {
            Address = new AddressDto { Street = "2 Land B Street" }
        });
        _landOnJobAId = landOnA.Id;
        _landOnJobBId = landOnB.Id;

        await _jobService.AddLandAsync(WorkspaceId, AdminId, _jobAId, _landOnJobAId);
        await _jobService.AddLandAsync(WorkspaceId, AdminId, _jobBId, _landOnJobBId);

        // Client assigned to Job A only - mirrors the Surveyor-on-Job-A setup in
        // JobAccessScopingTests, but Client additionally lacks land.view_all.
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobAId, ClientId, "Client");
    }

    [Fact]
    public async Task ClientWithNoJobAssignment_SearchReturnsNoLand()
    {
        // Regression case for the leak: before the fix, EnsureAllowedAsync alone let any
        // Client with land.view list every land record in the workspace.
        _landService = GetService<ILandService>();
        _jobService = GetService<IJobService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Unassigned job" });
        var land = await _landService.CreateAsync(WorkspaceId, AdminId, new LandRequest { Address = new AddressDto { Street = "Nobody's land" } });
        await _jobService.AddLandAsync(WorkspaceId, AdminId, job.Id, land.Id);

        var results = await _landService.SearchAsync(WorkspaceId, ClientId, null);
        Assert.Empty(results);
    }

    [Fact]
    public async Task ClientAssignedToJobA_SeesOnlyLandLinkedToJobA()
    {
        await SeedAsync();

        var results = await _landService.SearchAsync(WorkspaceId, ClientId, null);

        var land = Assert.Single(results);
        Assert.Equal(_landOnJobAId, land.Id);
    }

    [Fact]
    public async Task ClientAssignedToJobA_CannotGetLandLinkedToJobB()
    {
        await SeedAsync();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _landService.GetByIdAsync(WorkspaceId, ClientId, _landOnJobBId));
    }

    [Fact]
    public async Task ClientAssignedToJobA_CannotGetDeedsForLandLinkedToJobB()
    {
        await SeedAsync();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _landService.GetDeedsAsync(WorkspaceId, ClientId, _landOnJobBId));
    }

    [Fact]
    public async Task Admin_SeesAllLand_ViaViewAll()
    {
        await SeedAsync();
        var results = await _landService.SearchAsync(WorkspaceId, AdminId, null);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Surveyor_SeesAllLand_ViaViewAll()
    {
        await SeedAsync();
        var results = await _landService.SearchAsync(WorkspaceId, SurveyorId, null);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task UnlinkingLandFromJob_RemovesClientVisibility()
    {
        await SeedAsync();
        await _jobService.RemoveLandAsync(WorkspaceId, AdminId, _jobAId, _landOnJobAId);

        var results = await _landService.SearchAsync(WorkspaceId, ClientId, null);
        Assert.Empty(results);
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _landService.GetByIdAsync(WorkspaceId, ClientId, _landOnJobAId));
    }
}
