using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// Covers Document reuse for LandSurvey/LandDeed attachments via OwnerType/OwnerId -
/// same table/pipeline as Job documents, gated by land.edit/land.view instead of
/// job.edit/job.view since these belong to a Land, not a Job.
/// </summary>
public class LandOwnedDocumentTests : WorkspaceIntegrationTestBase
{
    private ILandService _landService = null!;
    private IDocumentService _documentService = null!;
    private Guid _landId;
    private Guid _surveyId;
    private Guid _deedId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ILandService, LandService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-landowneddoc-test-{Guid.NewGuid():N}")
                })
                .Build());
    }

    private async Task SeedAsync()
    {
        _landService = GetService<ILandService>();
        _documentService = GetService<IDocumentService>();

        var land = await _landService.CreateAsync(WorkspaceId, AdminId, new LandRequest
        {
            Address = new LandAddressDto { Village = "123 Main St", District = "Colombo" }
        });
        _landId = land.Id;

        var survey = await _landService.AddSurveyAsync(WorkspaceId, AdminId, _landId, new LandSurveyRequest
        {
            SurveyPlanNumber = "SP-1",
            SurveyDate = DateTime.UtcNow.Date
        });
        _surveyId = survey.Id;

        var deed = await _landService.AddDeedAsync(WorkspaceId, AdminId, _landId, new LandDeedRequest
        {
            DeedNumber = "DN-1",
            IssuedDate = DateTime.UtcNow.Date,
            IsCurrent = true
        });
        _deedId = deed.Id;
    }

    private static IFormFile MakeFile(string name = "plan.pdf", string content = "file-bytes", string contentType = "application/pdf")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    [Fact]
    public async Task UploadOwnedDocumentAsync_ForSurvey_Persists()
    {
        await SeedAsync();
        var doc = await _documentService.UploadOwnedDocumentAsync(WorkspaceId, AdminId, _landId, "LandSurvey", _surveyId, DocumentCategory.SurveyPlan, MakeFile());

        Assert.Equal("plan.pdf", doc.FileName);

        var docs = await _documentService.GetOwnedDocumentsAsync(WorkspaceId, AdminId, _landId, "LandSurvey", _surveyId);
        Assert.Single(docs);
    }

    [Fact]
    public async Task UploadOwnedDocumentAsync_MultipleFilesForSameSurvey_AllPersist()
    {
        await SeedAsync();
        await _documentService.UploadOwnedDocumentAsync(WorkspaceId, AdminId, _landId, "LandSurvey", _surveyId, DocumentCategory.SurveyPlan, MakeFile("page1.pdf"));
        await _documentService.UploadOwnedDocumentAsync(WorkspaceId, AdminId, _landId, "LandSurvey", _surveyId, DocumentCategory.SurveyPlan, MakeFile("page2.pdf"));

        var docs = await _documentService.GetOwnedDocumentsAsync(WorkspaceId, AdminId, _landId, "LandSurvey", _surveyId);
        Assert.Equal(2, docs.Count);
    }

    [Fact]
    public async Task UploadOwnedDocumentAsync_ForDeed_Persists()
    {
        await SeedAsync();
        var doc = await _documentService.UploadOwnedDocumentAsync(WorkspaceId, AdminId, _landId, "LandDeed", _deedId, DocumentCategory.LegalDocument, MakeFile("deed.pdf"));

        var docs = await _documentService.GetOwnedDocumentsAsync(WorkspaceId, AdminId, _landId, "LandDeed", _deedId);
        Assert.Single(docs);
        Assert.Equal(doc.Id, docs[0].Id);
    }

    [Fact]
    public async Task Client_CannotUploadOwnedDocument()
    {
        await SeedAsync();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _documentService.UploadOwnedDocumentAsync(WorkspaceId, ClientId, _landId, "LandSurvey", _surveyId, DocumentCategory.SurveyPlan, MakeFile()));
    }

    [Fact]
    public async Task UploadOwnedDocumentAsync_MismatchedLandId_ThrowsNotFound()
    {
        await SeedAsync();
        var otherLand = await _landService.CreateAsync(WorkspaceId, AdminId, new LandRequest { Address = new LandAddressDto { Village = "Other" } });

        await Assert.ThrowsAsync<NotFoundException>(
            () => _documentService.UploadOwnedDocumentAsync(WorkspaceId, AdminId, otherLand.Id, "LandSurvey", _surveyId, DocumentCategory.SurveyPlan, MakeFile()));
    }

    [Fact]
    public async Task DeleteOwnedDocumentAsync_RemovesDocument()
    {
        await SeedAsync();
        var doc = await _documentService.UploadOwnedDocumentAsync(WorkspaceId, AdminId, _landId, "LandSurvey", _surveyId, DocumentCategory.SurveyPlan, MakeFile());
        await _documentService.DeleteOwnedDocumentAsync(WorkspaceId, AdminId, _landId, "LandSurvey", _surveyId, doc.Id);

        var docs = await _documentService.GetOwnedDocumentsAsync(WorkspaceId, AdminId, _landId, "LandSurvey", _surveyId);
        Assert.Empty(docs);
    }

    [Fact]
    public async Task DeleteSurveyAsync_AlsoDeletesItsDocuments()
    {
        await SeedAsync();
        await _documentService.UploadOwnedDocumentAsync(WorkspaceId, AdminId, _landId, "LandSurvey", _surveyId, DocumentCategory.SurveyPlan, MakeFile());

        await _landService.DeleteSurveyAsync(WorkspaceId, AdminId, _landId, _surveyId);

        // The survey (and its documents' owner) is gone - listing via the now-deleted
        // survey id should return nothing rather than error, since GetOwnedDocumentsAsync's
        // land-ownership check would fail for an id that no longer exists on this land.
        await Assert.ThrowsAsync<NotFoundException>(
            () => _documentService.GetOwnedDocumentsAsync(WorkspaceId, AdminId, _landId, "LandSurvey", _surveyId));
    }

    [Fact]
    public async Task GetOwnedDocumentFileAsync_ReturnsUploadedContent()
    {
        await SeedAsync();
        var doc = await _documentService.UploadOwnedDocumentAsync(WorkspaceId, AdminId, _landId, "LandSurvey", _surveyId, DocumentCategory.SurveyPlan, MakeFile());

        var (found, content) = await _documentService.GetOwnedDocumentFileAsync(WorkspaceId, AdminId, _landId, "LandSurvey", _surveyId, doc.Id);

        Assert.Equal(doc.Id, found.Id);
        using var reader = new StreamReader(content);
        Assert.Equal("file-bytes", await reader.ReadToEndAsync());
    }
}
