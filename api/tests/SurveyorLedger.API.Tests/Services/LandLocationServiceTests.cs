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
    }

    private async Task SeedLandAsync()
    {
        _landService = GetService<ILandService>();
        var land = await _landService.CreateAsync(WorkspaceId, AdminId, new LandRequest
        {
            Address = new AddressDto { Street = "123 Main St", City = "Colombo" }
        });
        _landId = land.Id;
    }

    [Fact]
    public async Task SetLocationAsync_PersistsLatLng()
    {
        await SeedLandAsync();
        var updated = await _landService.SetLocationAsync(WorkspaceId, AdminId, _landId, new LandLocationRequest { Latitude = 6.9271m, Longitude = 79.8612m });
        Assert.Equal(6.9271m, updated.Latitude);
        Assert.Equal(79.8612m, updated.Longitude);
    }

    [Fact]
    public async Task Client_CannotSetLocation()
    {
        await SeedLandAsync();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _landService.SetLocationAsync(WorkspaceId, ClientId, _landId, new LandLocationRequest { Latitude = 1, Longitude = 1 }));
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
    public async Task SetLocationViaShareTokenAsync_UpdatesSameLandRow()
    {
        await SeedLandAsync();
        var token = await _landService.GenerateLocationShareLinkAsync(WorkspaceId, AdminId, _landId);

        var updated = await _landService.SetLocationViaShareTokenAsync(token, new LandLocationRequest { Latitude = 6.0m, Longitude = 80.0m });

        Assert.Equal(_landId, updated.Id);
        var land = await _landService.GetByIdAsync(WorkspaceId, AdminId, _landId);
        Assert.Equal(6.0m, land.Latitude);
        Assert.Equal(80.0m, land.Longitude);
    }

    [Fact]
    public async Task GetByLocationShareTokenAsync_UnknownToken_Throws()
    {
        _landService = GetService<ILandService>();
        await Assert.ThrowsAsync<NotFoundException>(() => _landService.GetByLocationShareTokenAsync("not-a-real-token"));
    }
}
