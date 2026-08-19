using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class LandMapPointServiceTests : WorkspaceIntegrationTestBase
{
    private ILandService _landService = null!;
    private Guid _landId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ILandService, LandService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-mappoint-test-{Guid.NewGuid():N}")
                })
                .Build());
    }

    private async Task SeedLandAsync()
    {
        _landService = GetService<ILandService>();
        var land = await _landService.CreateAsync(WorkspaceId, AdminId, new LandRequest
        {
            Address = new LandAddressDto { Village = "123 Main St", District = "Colombo" }
        });
        _landId = land.Id;
    }

    [Fact]
    public async Task AddMapPointAsync_PersistsAndLists()
    {
        await SeedLandAsync();
        var point = await _landService.AddMapPointAsync(WorkspaceId, AdminId, _landId, new LandMapPointRequest { Name = "North gate", Latitude = 6.9271m, Longitude = 79.8612m });

        Assert.Equal("North gate", point.Name);

        var points = await _landService.GetMapPointsAsync(WorkspaceId, AdminId, _landId);
        Assert.Single(points);
    }

    [Fact]
    public async Task UpdateMapPointAsync_MovesAndRenames()
    {
        await SeedLandAsync();
        var point = await _landService.AddMapPointAsync(WorkspaceId, AdminId, _landId, new LandMapPointRequest { Name = "Gate", Latitude = 1, Longitude = 1 });

        var updated = await _landService.UpdateMapPointAsync(WorkspaceId, AdminId, _landId, point.Id, new LandMapPointRequest { Name = "Main gate", Latitude = 2, Longitude = 2 });

        Assert.Equal("Main gate", updated.Name);
        Assert.Equal(2m, updated.Latitude);
        Assert.Equal(2m, updated.Longitude);
    }

    [Fact]
    public async Task DeleteMapPointAsync_RemovesPoint()
    {
        await SeedLandAsync();
        var point = await _landService.AddMapPointAsync(WorkspaceId, AdminId, _landId, new LandMapPointRequest { Name = "Gate", Latitude = 1, Longitude = 1 });
        await _landService.DeleteMapPointAsync(WorkspaceId, AdminId, _landId, point.Id);

        var points = await _landService.GetMapPointsAsync(WorkspaceId, AdminId, _landId);
        Assert.Empty(points);
    }

    [Fact]
    public async Task Client_CannotAddMapPoint()
    {
        await SeedLandAsync();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _landService.AddMapPointAsync(WorkspaceId, ClientId, _landId, new LandMapPointRequest { Name = "Gate", Latitude = 1, Longitude = 1 }));
    }
}
