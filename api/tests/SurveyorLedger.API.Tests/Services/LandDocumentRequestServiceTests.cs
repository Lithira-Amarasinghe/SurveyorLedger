using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class LandDocumentRequestServiceTests : WorkspaceIntegrationTestBase
{
    private ILandService _landService = null!;
    private ILandDocumentRequestService _requestService = null!;
    private Guid _landId;

    private IJobService _jobService = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ILandService, LandService>();
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<ILandDocumentRequestService, LandDocumentRequestService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-landdocreq-test-{Guid.NewGuid():N}")
                })
                .Build());
    }

    /// <summary>A Client only reaches land access through a job they're assigned to that's linked to the land - mirrors real production access, not a shortcut.</summary>
    private async Task SeedLandAsync()
    {
        _landService = GetService<ILandService>();
        _jobService = GetService<IJobService>();
        _requestService = GetService<ILandDocumentRequestService>();

        var land = await _landService.CreateAsync(WorkspaceId, AdminId, new LandRequest
        {
            Address = new LandAddressDto { Village = "123 Main St", District = "Colombo" }
        });
        _landId = land.Id;

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job for land" });
        await _jobService.AddLandAsync(WorkspaceId, AdminId, job.Id, _landId);
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, job.Id, ClientId, "Client");
    }

    private static IFormFile MakeFile(string name = "deed.pdf", string content = "file-bytes") =>
        new FormFile(new MemoryStream(Encoding.UTF8.GetBytes(content)), 0, Encoding.UTF8.GetByteCount(content), "file", name)
            { Headers = new HeaderDictionary(), ContentType = "application/pdf" };

    [Fact]
    public async Task Admin_CanCreateRequest()
    {
        await SeedLandAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _landId, "Deed copy", null, DocumentCategory.LegalDocument);

        Assert.Equal("Deed copy", request.Title);
        Assert.Equal("Pending", request.Status);
    }

    [Fact]
    public async Task Client_CannotCreateRequest()
    {
        await SeedLandAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _requestService.CreateAsync(WorkspaceId, ClientId, _landId, "Deed copy", null, DocumentCategory.LegalDocument));
    }

    [Fact]
    public async Task Client_CanFulfillUntargetedRequest()
    {
        await SeedLandAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _landId, "Deed copy", null, DocumentCategory.LegalDocument);

        var fulfilled = await _requestService.FulfillAsync(WorkspaceId, ClientId, _landId, request.Id, new List<IFormFile> { MakeFile() }, Guid.NewGuid());

        Assert.Equal("Fulfilled", fulfilled.Status);
        Assert.NotNull(fulfilled.FulfilledBatchId);
    }

    [Fact]
    public async Task Fulfill_RoleTargetedClient_SucceedsForNonStaffCaller()
    {
        await SeedLandAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _landId, "Deed copy", null, DocumentCategory.LegalDocument, Constants.SystemRoles.Client);

        var fulfilled = await _requestService.FulfillAsync(WorkspaceId, ClientId, _landId, request.Id, new List<IFormFile> { MakeFile() }, Guid.NewGuid());

        Assert.Equal("Fulfilled", fulfilled.Status);
    }

    [Fact]
    public async Task Fulfill_RoleTargetedAdmin_ThrowsForbidden_ForClient()
    {
        await SeedLandAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _landId, "Deed copy", null, DocumentCategory.LegalDocument, Constants.SystemRoles.Admin);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _requestService.FulfillAsync(WorkspaceId, ClientId, _landId, request.Id, new List<IFormFile> { MakeFile() }, Guid.NewGuid()));
    }

    [Fact]
    public async Task Reopen_KeepsPreviousDocumentLink()
    {
        await SeedLandAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _landId, "Deed copy", null, DocumentCategory.LegalDocument);
        var fulfilled = await _requestService.FulfillAsync(WorkspaceId, ClientId, _landId, request.Id, new List<IFormFile> { MakeFile() }, Guid.NewGuid());

        var reopened = await _requestService.ReopenAsync(WorkspaceId, AdminId, _landId, request.Id);

        Assert.Equal("Reopened", reopened.Status);
        Assert.Equal(fulfilled.FulfilledBatchId, reopened.FulfilledBatchId);
    }

    [Fact]
    public async Task RefulfillingReopenedRequest_WithSameBatchId_AccumulatesFiles()
    {
        await SeedLandAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _landId, "Deed copy", null, DocumentCategory.LegalDocument);
        var batchId = Guid.NewGuid();
        var first = await _requestService.FulfillAsync(WorkspaceId, ClientId, _landId, request.Id, new List<IFormFile> { MakeFile("first.pdf") }, batchId);
        await _requestService.ReopenAsync(WorkspaceId, AdminId, _landId, request.Id);

        var second = await _requestService.FulfillAsync(WorkspaceId, ClientId, _landId, request.Id, new List<IFormFile> { MakeFile("second.pdf") }, first.FulfilledBatchId!.Value);

        Assert.Equal(batchId, second.FulfilledBatchId);

        var documentService = GetService<IDocumentService>();
        var remaining = await documentService.GetOwnedDocumentsAsync(WorkspaceId, AdminId, _landId, "Land", _landId);
        Assert.Equal(2, remaining.Count(d => d.UploadBatchId == batchId)); // both first.pdf and second.pdf stay, matching "keep old files, group goes back to pending"
    }

    [Fact]
    public async Task FulfillAsync_WithMultipleFiles_AllShareTheBatchId()
    {
        await SeedLandAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _landId, "Deed copy", null, DocumentCategory.LegalDocument);
        var batchId = Guid.NewGuid();

        var fulfilled = await _requestService.FulfillAsync(WorkspaceId, ClientId, _landId, request.Id, new List<IFormFile> { MakeFile("a.pdf"), MakeFile("b.pdf") }, batchId);

        Assert.Equal(batchId, fulfilled.FulfilledBatchId);
        Assert.Equal("Fulfilled", fulfilled.Status);

        var documentService = GetService<IDocumentService>();
        var docs = await documentService.GetOwnedDocumentsAsync(WorkspaceId, AdminId, _landId, "Land", _landId);
        Assert.Equal(2, docs.Count(d => d.UploadBatchId == batchId));
    }

    [Fact]
    public async Task Cancel_SoftDeletes_AndExcludesFromList()
    {
        await SeedLandAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _landId, "Deed copy", null, DocumentCategory.LegalDocument);

        await _requestService.CancelAsync(WorkspaceId, AdminId, _landId, request.Id);

        var requests = await _requestService.GetForLandAsync(WorkspaceId, AdminId, _landId);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task UpdateTarget_OnFulfilledRequest_ThrowsValidation()
    {
        await SeedLandAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _landId, "Deed copy", null, DocumentCategory.LegalDocument);
        await _requestService.FulfillAsync(WorkspaceId, ClientId, _landId, request.Id, new List<IFormFile> { MakeFile() }, Guid.NewGuid());

        await Assert.ThrowsAsync<ValidationException>(() =>
            _requestService.UpdateTargetAsync(WorkspaceId, AdminId, _landId, request.Id, Constants.SystemRoles.Client));
    }

    [Fact]
    public async Task Admin_CanGenerateAndRevokeShareLink()
    {
        await SeedLandAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _landId, "Deed copy", null, DocumentCategory.LegalDocument);

        var withLink = await _requestService.GenerateShareLinkAsync(WorkspaceId, AdminId, _landId, request.Id);
        Assert.NotNull(withLink.ShareToken);

        await _requestService.RevokeShareLinkAsync(WorkspaceId, AdminId, _landId, request.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => _requestService.GetByShareTokenAsync(withLink.ShareToken!));
    }

    [Fact]
    public async Task UploadViaShareToken_FulfillsRequest_AttributedToRequester()
    {
        await SeedLandAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _landId, "Deed copy", null, DocumentCategory.LegalDocument);
        var withLink = await _requestService.GenerateShareLinkAsync(WorkspaceId, AdminId, _landId, request.Id);

        var fulfilled = await _requestService.UploadViaShareTokenAsync(withLink.ShareToken!, MakeFile());

        Assert.Equal("Fulfilled", fulfilled.Status);
        Assert.Equal(AdminPersonId, fulfilled.FulfilledBy);
    }

    [Fact]
    public async Task UploadViaShareToken_OnAlreadyFulfilledRequest_ThrowsValidation()
    {
        await SeedLandAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _landId, "Deed copy", null, DocumentCategory.LegalDocument);
        var withLink = await _requestService.GenerateShareLinkAsync(WorkspaceId, AdminId, _landId, request.Id);
        await _requestService.UploadViaShareTokenAsync(withLink.ShareToken!, MakeFile());

        await Assert.ThrowsAsync<ValidationException>(() =>
            _requestService.UploadViaShareTokenAsync(withLink.ShareToken!, MakeFile()));
    }
}
