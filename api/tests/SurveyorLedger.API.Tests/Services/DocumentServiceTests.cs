using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class DocumentServiceTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IDocumentService _documentService = null!;
    private Guid _jobAId;
    private Guid _jobBId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-doc-test-{Guid.NewGuid():N}")
                })
                .Build());
    }

    private async Task SeedJobsAsync()
    {
        _jobService = GetService<IJobService>();
        _documentService = GetService<IDocumentService>();

        var jobA = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        var jobB = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job B" });
        _jobAId = jobA.Id;
        _jobBId = jobB.Id;

        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobAId, SurveyorId);
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobAId, ClientId);
    }

    private static IFormFile MakeFile(string name = "plan.pdf", string content = "file-bytes", string contentType = "application/pdf")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    [Fact]
    public async Task Surveyor_CanUpload_OnAssignedJob()
    {
        await SeedJobsAsync();
        var doc = await _documentService.UploadAsync(WorkspaceId, SurveyorId, _jobAId,
            MakeFile(), DocumentCategory.SurveyPlan, DocumentVisibility.Internal);

        Assert.Equal("plan.pdf", doc.FileName);
        Assert.Equal(DocumentCategory.SurveyPlan, doc.Category);
    }

    [Fact]
    public async Task Client_CanUpload_OnAssignedJob()
    {
        await SeedJobsAsync();
        var doc = await _documentService.UploadAsync(WorkspaceId, ClientId, _jobAId,
            MakeFile("deed.pdf"), DocumentCategory.LegalDocument, DocumentVisibility.ClientVisible);

        Assert.Equal("deed.pdf", doc.FileName);
    }

    [Fact]
    public async Task Surveyor_CannotUpload_OnUnassignedJob()
    {
        await SeedJobsAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _documentService.UploadAsync(WorkspaceId, SurveyorId, _jobBId,
                MakeFile(), DocumentCategory.Other, DocumentVisibility.Internal));
    }

    [Fact]
    public async Task RejectsDisallowedExtension()
    {
        await SeedJobsAsync();
        await Assert.ThrowsAsync<ValidationException>(() =>
            _documentService.UploadAsync(WorkspaceId, AdminId, _jobAId,
                MakeFile("virus.exe", contentType: "application/octet-stream"), DocumentCategory.Other, DocumentVisibility.Internal));
    }

    [Fact]
    public async Task RejectsOversizedFile()
    {
        await SeedJobsAsync();
        var bytes = new byte[26 * 1024 * 1024];
        var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "file", "big.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _documentService.UploadAsync(WorkspaceId, AdminId, _jobAId,
                file, DocumentCategory.Other, DocumentVisibility.Internal));
    }

    [Fact]
    public async Task Client_DoesNotSee_InternalDocuments()
    {
        await SeedJobsAsync();
        await _documentService.UploadAsync(WorkspaceId, AdminId, _jobAId,
            MakeFile("internal.pdf"), DocumentCategory.Other, DocumentVisibility.Internal);
        await _documentService.UploadAsync(WorkspaceId, AdminId, _jobAId,
            MakeFile("public.pdf"), DocumentCategory.SurveyPlan, DocumentVisibility.ClientVisible);

        var docs = await _documentService.GetDocumentsAsync(WorkspaceId, ClientId, _jobAId);

        var doc = Assert.Single(docs);
        Assert.Equal("public.pdf", doc.FileName);
    }

    [Fact]
    public async Task Surveyor_SeesInternalDocuments()
    {
        await SeedJobsAsync();
        await _documentService.UploadAsync(WorkspaceId, AdminId, _jobAId,
            MakeFile("internal.pdf"), DocumentCategory.Other, DocumentVisibility.Internal);

        var docs = await _documentService.GetDocumentsAsync(WorkspaceId, SurveyorId, _jobAId);

        Assert.Single(docs);
    }

    [Fact]
    public async Task Client_GettingInternalDocumentById_ThrowsNotFound()
    {
        await SeedJobsAsync();
        var doc = await _documentService.UploadAsync(WorkspaceId, AdminId, _jobAId,
            MakeFile("internal.pdf"), DocumentCategory.Other, DocumentVisibility.Internal);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _documentService.GetFileAsync(WorkspaceId, ClientId, _jobAId, doc.Id));
    }

    [Fact]
    public async Task GetFileAsync_ReturnsSavedBytes()
    {
        await SeedJobsAsync();
        var doc = await _documentService.UploadAsync(WorkspaceId, AdminId, _jobAId,
            MakeFile("plan.pdf", "hello-bytes"), DocumentCategory.SurveyPlan, DocumentVisibility.ClientVisible);

        var (found, content) = await _documentService.GetFileAsync(WorkspaceId, AdminId, _jobAId, doc.Id);

        using var reader = new StreamReader(content);
        Assert.Equal("hello-bytes", await reader.ReadToEndAsync());
        Assert.Equal(doc.Id, found.Id);
    }

    [Fact]
    public async Task Client_CannotDelete()
    {
        await SeedJobsAsync();
        var doc = await _documentService.UploadAsync(WorkspaceId, ClientId, _jobAId,
            MakeFile(), DocumentCategory.Other, DocumentVisibility.ClientVisible);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _documentService.DeleteAsync(WorkspaceId, ClientId, _jobAId, doc.Id));
    }

    [Fact]
    public async Task Admin_CanDelete_AndDocumentIsExcludedFromList()
    {
        await SeedJobsAsync();
        var doc = await _documentService.UploadAsync(WorkspaceId, AdminId, _jobAId,
            MakeFile(), DocumentCategory.Other, DocumentVisibility.ClientVisible);

        await _documentService.DeleteAsync(WorkspaceId, AdminId, _jobAId, doc.Id);

        var docs = await _documentService.GetDocumentsAsync(WorkspaceId, AdminId, _jobAId);
        Assert.Empty(docs);
    }

    [Fact]
    public async Task DocumentFromDifferentJob_ThrowsNotFound()
    {
        await SeedJobsAsync();
        var doc = await _documentService.UploadAsync(WorkspaceId, AdminId, _jobAId,
            MakeFile(), DocumentCategory.Other, DocumentVisibility.ClientVisible);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _documentService.GetFileAsync(WorkspaceId, AdminId, _jobBId, doc.Id));
    }
}
