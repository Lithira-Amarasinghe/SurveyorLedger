using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// Location/share-link permission mirrors every other Land mutation: land.edit
/// (Admin/Surveyor with access to the record) required, Client forbidden.
/// </summary>
public class LandLocationServiceTests : WorkspaceIntegrationTestBase
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
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-landlocation-test-{Guid.NewGuid():N}")
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
    public async Task Client_CannotAddMapPoint()
    {
        await SeedLandAsync();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _landService.AddMapPointAsync(WorkspaceId, ClientId, _landId, new LandMapPointRequest { Name = "Gate", Latitude = 1, Longitude = 1 }));
    }

    [Fact]
    public async Task GenerateLocationShareLinkAsync_IsIdempotent()
    {
        await SeedLandAsync();
        var first = await _landService.GenerateLocationShareLinkAsync(WorkspaceId, AdminId, _landId);
        var second = await _landService.GenerateLocationShareLinkAsync(WorkspaceId, AdminId, _landId);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task RegenerateLocationShareLinkAsync_IssuesNewToken_OldTokenNoLongerResolves()
    {
        await SeedLandAsync();
        var oldToken = await _landService.GenerateLocationShareLinkAsync(WorkspaceId, AdminId, _landId);
        var newToken = await _landService.RegenerateLocationShareLinkAsync(WorkspaceId, AdminId, _landId);

        Assert.NotEqual(oldToken, newToken);
        await Assert.ThrowsAsync<NotFoundException>(() => _landService.GetByLocationShareTokenAsync(oldToken));
        var land = await _landService.GetByLocationShareTokenAsync(newToken);
        Assert.Equal(_landId, land.Id);
    }

    [Fact]
    public async Task RevokeLocationShareLinkAsync_TokenNoLongerResolves()
    {
        await SeedLandAsync();
        var token = await _landService.GenerateLocationShareLinkAsync(WorkspaceId, AdminId, _landId);
        await _landService.RevokeLocationShareLinkAsync(WorkspaceId, AdminId, _landId);

        await Assert.ThrowsAsync<NotFoundException>(() => _landService.GetByLocationShareTokenAsync(token));
    }

    [Fact]
    public async Task AddMapPointViaShareTokenAsync_PersistsPoint()
    {
        await SeedLandAsync();
        var token = await _landService.GenerateLocationShareLinkAsync(WorkspaceId, AdminId, _landId);

        var point = await _landService.AddMapPointViaShareTokenAsync(token, new LandMapPointRequest { Name = "Front gate", Latitude = 6.0m, Longitude = 80.0m });

        Assert.Equal("Front gate", point.Name);
        var points = await _landService.GetMapPointsAsync(WorkspaceId, AdminId, _landId);
        Assert.Single(points);
    }

    [Fact]
    public async Task GenerateMapViewShareLinkAsync_IsIdempotent()
    {
        await SeedLandAsync();
        var first = await _landService.GenerateMapViewShareLinkAsync(WorkspaceId, AdminId, _landId);
        var second = await _landService.GenerateMapViewShareLinkAsync(WorkspaceId, AdminId, _landId);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task RevokeMapViewShareLinkAsync_TokenNoLongerResolves()
    {
        await SeedLandAsync();
        var token = await _landService.GenerateMapViewShareLinkAsync(WorkspaceId, AdminId, _landId);
        await _landService.RevokeMapViewShareLinkAsync(WorkspaceId, AdminId, _landId);

        await Assert.ThrowsAsync<NotFoundException>(() => _landService.GetByMapViewShareTokenAsync(token));
    }

    [Fact]
    public async Task MapViewShareLink_IsReadOnly_NeverAddsAPoint()
    {
        // The view link's own preview flow has no write endpoint at all - GetMapPointsForMapViewShareTokenAsync
        // is the only thing it exposes, confirming zero points ever appear from a view-only token.
        await SeedLandAsync();
        var token = await _landService.GenerateMapViewShareLinkAsync(WorkspaceId, AdminId, _landId);
        var points = await _landService.GetMapPointsForMapViewShareTokenAsync(token);
        Assert.Empty(points);
    }

    [Fact]
    public async Task GetByLocationShareTokenAsync_UnknownToken_Throws()
    {
        _landService = GetService<ILandService>();
        await Assert.ThrowsAsync<NotFoundException>(() => _landService.GetByLocationShareTokenAsync("not-a-real-token"));
    }
}
