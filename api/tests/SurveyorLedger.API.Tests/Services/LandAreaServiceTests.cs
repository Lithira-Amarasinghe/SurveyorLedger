using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Services;
using Xunit;
using ValidationException = SurveyorLedger.Core.Exceptions.ValidationException;

namespace SurveyorLedger.API.Tests.Services;

public class LandAreaServiceTests : WorkspaceIntegrationTestBase
{
    private ILandService _landService = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ILandService, LandService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-landarea-test-{Guid.NewGuid():N}")
                })
                .Build());
    }

    private LandRequest BaseRequest(AreaDto? area) => new()
    {
        Address = new AddressDto { Street = "123 Main St", City = "Colombo" },
        Area = area
    };

    [Fact]
    public async Task CreateAsync_OnlyPerchesSet_PersistsAndReturnsAllRepresentations()
    {
        _landService = GetService<ILandService>();
        var land = await _landService.CreateAsync(WorkspaceId, AdminId, BaseRequest(new AreaDto { Perches = 40 }));
        var fetched = await _landService.GetByIdAsync(WorkspaceId, AdminId, land.Id);

        Assert.Equal(1011.7141056m, fetched.AreaSquareMeters);
    }

    [Fact]
    public async Task CreateAsync_SquareMetersSet_ConvertsAndPersists()
    {
        _landService = GetService<ILandService>();
        var land = await _landService.CreateAsync(WorkspaceId, AdminId, BaseRequest(new AreaDto { SquareMeters = 5000 }));
        var fetched = await _landService.GetByIdAsync(WorkspaceId, AdminId, land.Id);

        Assert.Equal(5000m, fetched.AreaSquareMeters);
    }

    [Fact]
    public async Task CreateAsync_HectaresSet_ConvertsAndPersists()
    {
        _landService = GetService<ILandService>();
        var land = await _landService.CreateAsync(WorkspaceId, AdminId, BaseRequest(new AreaDto { Hectares = 1 }));
        var fetched = await _landService.GetByIdAsync(WorkspaceId, AdminId, land.Id);

        Assert.Equal(10000m, fetched.AreaSquareMeters);
    }

    [Fact]
    public async Task CreateAsync_BothAcresAndSquareMetersSet_Throws()
    {
        _landService = GetService<ILandService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => _landService.CreateAsync(WorkspaceId, AdminId, BaseRequest(new AreaDto { Acres = 1, SquareMeters = 100 })));
    }

    [Fact]
    public async Task CreateAsync_AreaOmitted_PersistsNull()
    {
        _landService = GetService<ILandService>();
        var land = await _landService.CreateAsync(WorkspaceId, AdminId, BaseRequest(null));
        var fetched = await _landService.GetByIdAsync(WorkspaceId, AdminId, land.Id);

        Assert.Null(fetched.AreaSquareMeters);
    }

    [Fact]
    public void AreaDto_RoodsFour_FailsValidation()
    {
        var dto = new AreaDto { Roods = 4 };
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AreaDto.Roods)));
    }

    [Fact]
    public void AreaDto_PerchesFortyFive_FailsValidation()
    {
        var dto = new AreaDto { Perches = 45 };
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AreaDto.Perches)));
    }
}
