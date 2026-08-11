using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class DocumentRequestServiceTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IDocumentRequestService _requestService = null!;
    private Guid _jobAId;
    private Guid _jobBId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IDocumentRequestService, DocumentRequestService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-docreq-test-{Guid.NewGuid():N}")
                })
                .Build());
    }

    private async Task SeedJobsAsync()
    {
        _jobService = GetService<IJobService>();
        _requestService = GetService<IDocumentRequestService>();

        var jobA = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        var jobB = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job B" });
        _jobAId = jobA.Id;
        _jobBId = jobB.Id;

        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobAId, SurveyorId);
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobAId, ClientId);
    }

    private static IFormFile MakeFile(string name = "deed.pdf", string content = "file-bytes") =>
        new FormFile(new MemoryStream(Encoding.UTF8.GetBytes(content)), 0, Encoding.UTF8.GetByteCount(content), "file", name)
            { Headers = new HeaderDictionary(), ContentType = "application/pdf" };

    [Fact]
    public async Task Admin_CanCreateRequest()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);

        Assert.Equal("Legal Deed", request.Title);
        Assert.Equal("Pending", request.Status);
    }

    [Fact]
    public async Task Client_CannotCreateRequest()
    {
        await SeedJobsAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _requestService.CreateAsync(WorkspaceId, ClientId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument));
    }

    [Fact]
    public async Task Client_CanFulfillRequest_OnAssignedJob()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);

        var fulfilled = await _requestService.FulfillAsync(WorkspaceId, ClientId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible);

        Assert.Equal("Fulfilled", fulfilled.Status);
        Assert.NotNull(fulfilled.FulfilledDocumentId);
        Assert.Equal(ClientId, fulfilled.FulfilledBy);
    }

    [Fact]
    public async Task Reopen_ClearsLink_WithoutDeletingDocument()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);
        var fulfilled = await _requestService.FulfillAsync(WorkspaceId, ClientId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible);
        var documentId = fulfilled.FulfilledDocumentId;

        var reopened = await _requestService.ReopenAsync(WorkspaceId, AdminId, _jobAId, request.Id);

        Assert.Equal("Pending", reopened.Status);
        Assert.Null(reopened.FulfilledDocumentId);
        Assert.NotNull(documentId);
    }

    [Fact]
    public async Task Client_CannotReopen()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);
        await _requestService.FulfillAsync(WorkspaceId, ClientId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _requestService.ReopenAsync(WorkspaceId, ClientId, _jobAId, request.Id));
    }

    [Fact]
    public async Task Cancel_SoftDeletes_AndExcludesFromList()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);

        await _requestService.CancelAsync(WorkspaceId, AdminId, _jobAId, request.Id);

        var requests = await _requestService.GetForJobAsync(WorkspaceId, AdminId, _jobAId);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task Client_CanListRequests_OnAssignedJob()
    {
        await SeedJobsAsync();
        await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);

        var requests = await _requestService.GetForJobAsync(WorkspaceId, ClientId, _jobAId);

        Assert.Single(requests);
    }

    [Fact]
    public async Task RequestFromDifferentJob_ThrowsNotFound()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _requestService.FulfillAsync(WorkspaceId, AdminId, _jobBId, request.Id, MakeFile(), DocumentVisibility.ClientVisible));
    }
}
