using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class LandPhotoServiceTests : WorkspaceIntegrationTestBase
{
    private ILandService _landService = null!;
    private Guid _landId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ILandService, LandService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-landphoto-test-{Guid.NewGuid():N}")
                })
                .Build());
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

    private static IFormFile MakePhoto(string name = "site.jpg", string contentType = "image/jpeg")
    {
        var bytes = Encoding.UTF8.GetBytes("fake-image-bytes");
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    [Fact]
    public async Task UploadPhotoAsync_PersistsPhoto()
    {
        await SeedLandAsync();
        var photo = await _landService.UploadPhotoAsync(WorkspaceId, AdminId, _landId, MakePhoto());
        Assert.Equal("site.jpg", photo.FileName);

        var photos = await _landService.GetPhotosAsync(WorkspaceId, AdminId, _landId);
        Assert.Single(photos);
    }

    [Fact]
    public async Task UploadPhotoAsync_RejectsDisallowedExtension()
    {
        await SeedLandAsync();
        await Assert.ThrowsAsync<ValidationException>(
            () => _landService.UploadPhotoAsync(WorkspaceId, AdminId, _landId, MakePhoto("plan.pdf", "application/pdf")));
    }

    [Fact]
    public async Task Client_CannotUploadPhoto()
    {
        await SeedLandAsync();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _landService.UploadPhotoAsync(WorkspaceId, ClientId, _landId, MakePhoto()));
    }

    [Fact]
    public async Task DeletePhotoAsync_RemovesPhoto()
    {
        await SeedLandAsync();
        var photo = await _landService.UploadPhotoAsync(WorkspaceId, AdminId, _landId, MakePhoto());
        await _landService.DeletePhotoAsync(WorkspaceId, AdminId, _landId, photo.Id);

        var photos = await _landService.GetPhotosAsync(WorkspaceId, AdminId, _landId);
        Assert.Empty(photos);
    }

    [Fact]
    public async Task GetPhotoFileAsync_ReturnsUploadedContent()
    {
        await SeedLandAsync();
        var photo = await _landService.UploadPhotoAsync(WorkspaceId, AdminId, _landId, MakePhoto());
        var (found, content) = await _landService.GetPhotoFileAsync(WorkspaceId, AdminId, _landId, photo.Id);

        Assert.Equal(photo.Id, found.Id);
        using var reader = new StreamReader(content);
        Assert.Equal("fake-image-bytes", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task UploadPhotoAsync_SetsUploadedByUser_AsPersonNotUserAccount()
    {
        await SeedLandAsync();
        var photo = await _landService.UploadPhotoAsync(WorkspaceId, AdminId, _landId, MakePhoto());

        Assert.IsType<Person>(photo.UploadedByUser);
        Assert.Equal("Admin", photo.UploadedByUser.FirstName);
        Assert.NotEqual(AdminId, photo.UploadedBy); // UploadedBy is the Person.Id, not the caller's UserAccount.Id
    }
}
