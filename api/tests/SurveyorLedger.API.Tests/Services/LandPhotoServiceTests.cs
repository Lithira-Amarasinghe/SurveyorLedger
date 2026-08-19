using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>Land photos are Documents (OwnerType="LandPhoto") - these tests go through IDocumentService's owned-document methods, same as survey/deed attachments.</summary>
public class LandPhotoServiceTests : WorkspaceIntegrationTestBase
{
    private ILandService _landService = null!;
    private IDocumentService _documentService = null!;
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
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-landphoto-test-{Guid.NewGuid():N}")
                })
                .Build());
    }

    private async Task SeedLandAsync()
    {
        _landService = GetService<ILandService>();
        _documentService = GetService<IDocumentService>();
        var land = await _landService.CreateAsync(WorkspaceId, AdminId, new LandRequest
        {
            Address = new LandAddressDto { Village = "123 Main St", District = "Colombo" }
        });
        _landId = land.Id;
    }

    private static IFormFile MakePhoto(string name = "site.jpg", string contentType = "image/jpeg")
    {
        var bytes = Encoding.UTF8.GetBytes("fake-image-bytes");
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    private Task<Document> UploadPhotoAsync(Guid callerId, IFormFile file) =>
        _documentService.UploadOwnedDocumentAsync(WorkspaceId, callerId, _landId, "LandPhoto", _landId, DocumentCategory.Photo, file);

    private Task<List<Document>> GetPhotosAsync(Guid callerId) =>
        _documentService.GetOwnedDocumentsAsync(WorkspaceId, callerId, _landId, "LandPhoto", _landId);

    [Fact]
    public async Task UploadPhotoAsync_PersistsPhoto()
    {
        await SeedLandAsync();
        var photo = await UploadPhotoAsync(AdminId, MakePhoto());
        Assert.Equal("site.jpg", photo.FileName);

        var photos = await GetPhotosAsync(AdminId);
        Assert.Single(photos);
    }

    [Fact]
    public async Task UploadPhotoAsync_RejectsDisallowedExtension()
    {
        await SeedLandAsync();
        await Assert.ThrowsAsync<ValidationException>(
            () => UploadPhotoAsync(AdminId, MakePhoto("virus.exe", "application/octet-stream")));
    }

    [Fact]
    public async Task Client_CannotUploadPhoto()
    {
        await SeedLandAsync();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => UploadPhotoAsync(ClientId, MakePhoto()));
    }

    [Fact]
    public async Task DeletePhotoAsync_RemovesPhoto()
    {
        await SeedLandAsync();
        var photo = await UploadPhotoAsync(AdminId, MakePhoto());
        await _documentService.DeleteOwnedDocumentAsync(WorkspaceId, AdminId, _landId, "LandPhoto", _landId, photo.Id);

        var photos = await GetPhotosAsync(AdminId);
        Assert.Empty(photos);
    }

    [Fact]
    public async Task GetPhotoFileAsync_ReturnsUploadedContent()
    {
        await SeedLandAsync();
        var photo = await UploadPhotoAsync(AdminId, MakePhoto());
        var (found, content) = await _documentService.GetOwnedDocumentFileAsync(WorkspaceId, AdminId, _landId, "LandPhoto", _landId, photo.Id);

        Assert.Equal(photo.Id, found.Id);
        using var reader = new StreamReader(content);
        Assert.Equal("fake-image-bytes", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task UploadPhotoAsync_SetsUploadedByUser_AsPersonNotUserAccount()
    {
        await SeedLandAsync();
        var photo = await UploadPhotoAsync(AdminId, MakePhoto());

        Assert.IsType<Person>(photo.UploadedByUser);
        Assert.Equal("Admin", photo.UploadedByUser.FirstName);
        Assert.NotEqual(AdminId, photo.UploadedBy); // UploadedBy is the Person.Id, not the caller's UserAccount.Id
    }
}
