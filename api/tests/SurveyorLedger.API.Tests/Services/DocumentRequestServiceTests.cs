using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
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
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-docreq-test-{Guid.NewGuid():N}"),
                    ["AppSettings:UiBaseUrl"] = "https://test.local"
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

        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobAId, SurveyorId, "Surveyor");
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobAId, ClientId, "Client");
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
    public async Task Reopen_KeepsPreviousDocumentLink_SetsStatusReopened()
    {
        // No versioning: the previous document and its "via request" link stay visible
        // (FulfilledDocumentId not cleared) until a replacement is actually uploaded.
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);
        var fulfilled = await _requestService.FulfillAsync(WorkspaceId, ClientId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible);

        var reopened = await _requestService.ReopenAsync(WorkspaceId, AdminId, _jobAId, request.Id);

        Assert.Equal("Reopened", reopened.Status);
        Assert.Equal(fulfilled.FulfilledDocumentId, reopened.FulfilledDocumentId);
    }

    [Fact]
    public async Task Reopen_CanUpdateNote()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);
        await _requestService.FulfillAsync(WorkspaceId, ClientId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible);

        var reopened = await _requestService.ReopenAsync(WorkspaceId, AdminId, _jobAId, request.Id, "Scan as PDF, both sides.");

        Assert.Equal("Scan as PDF, both sides.", reopened.Description);
    }

    [Fact]
    public async Task RefulfillingReopenedRequest_DeletesPreviousDocument()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);
        var firstFulfill = await _requestService.FulfillAsync(WorkspaceId, ClientId, _jobAId, request.Id, MakeFile("first.pdf"), DocumentVisibility.ClientVisible);
        var firstDocumentId = firstFulfill.FulfilledDocumentId!.Value;
        await _requestService.ReopenAsync(WorkspaceId, AdminId, _jobAId, request.Id);

        var secondFulfill = await _requestService.FulfillAsync(WorkspaceId, ClientId, _jobAId, request.Id, MakeFile("second.pdf"), DocumentVisibility.ClientVisible);

        Assert.Equal("Fulfilled", secondFulfill.Status);
        Assert.NotEqual(firstDocumentId, secondFulfill.FulfilledDocumentId);

        var documentService = GetService<IDocumentService>();
        var remainingDocs = await documentService.GetDocumentsAsync(WorkspaceId, AdminId, _jobAId);
        Assert.DoesNotContain(remainingDocs, d => d.Id == firstDocumentId);
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

    [Fact]
    public async Task Create_WithBothTargetRoleAndTargetUserId_ThrowsValidation()
    {
        await SeedJobsAsync();
        await Assert.ThrowsAsync<ValidationException>(() =>
            _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument, Constants.SystemRoles.Client, ClientId));
    }

    [Fact]
    public async Task Create_TargetingNonParticipant_ThrowsValidation()
    {
        await SeedJobsAsync();
        // SurveyorId/ClientId are assigned to Job A only; nobody is assigned to Job B, so
        // targeting AdminId (who has full access but no job-scoped UserAccess row for Job A) works
        // as the non-participant case here since Admin never gets an explicit job assignment.
        await Assert.ThrowsAsync<ValidationException>(() =>
            _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument, null, AdminId));
    }

    [Fact]
    public async Task Fulfill_RoleTargeted_WrongRole_ThrowsForbidden_EvenForAdmin()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument, Constants.SystemRoles.Client, null);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _requestService.FulfillAsync(WorkspaceId, AdminId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible));
    }

    [Fact]
    public async Task Fulfill_RoleTargeted_CorrectRole_Succeeds()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument, Constants.SystemRoles.Client, null);

        var fulfilled = await _requestService.FulfillAsync(WorkspaceId, ClientId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible);

        Assert.Equal("Fulfilled", fulfilled.Status);
    }

    [Fact]
    public async Task Fulfill_PersonTargeted_WrongPerson_ThrowsForbidden_EvenForSurveyor()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument, null, ClientId);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _requestService.FulfillAsync(WorkspaceId, SurveyorId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible));
    }

    [Fact]
    public async Task Fulfill_PersonTargeted_CorrectPerson_Succeeds()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument, null, ClientId);

        var fulfilled = await _requestService.FulfillAsync(WorkspaceId, ClientId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible);

        Assert.Equal("Fulfilled", fulfilled.Status);
    }

    [Fact]
    public async Task Fulfill_OpenRequest_StaffCanStillFulfillOnBehalf()
    {
        // Regression guard: targeting must not change open-request behavior.
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument, null, null);

        var fulfilled = await _requestService.FulfillAsync(WorkspaceId, AdminId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible);

        Assert.Equal("Fulfilled", fulfilled.Status);
    }

    [Fact]
    public async Task UpdateTarget_ChangesFromOpenToRoleTargeted()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument, null, null);

        var updated = await _requestService.UpdateTargetAsync(WorkspaceId, AdminId, _jobAId, request.Id, Constants.SystemRoles.Client, null);

        Assert.Equal(Constants.SystemRoles.Client, updated.TargetRole);
        Assert.Null(updated.TargetUserId);
    }

    [Fact]
    public async Task UpdateTarget_OnFulfilledRequest_ThrowsValidation()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument, null, null);
        await _requestService.FulfillAsync(WorkspaceId, AdminId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _requestService.UpdateTargetAsync(WorkspaceId, AdminId, _jobAId, request.Id, Constants.SystemRoles.Client, null));
    }

    [Fact]
    public async Task UpdateTarget_WithBothRoleAndPerson_ThrowsValidation()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument, null, null);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _requestService.UpdateTargetAsync(WorkspaceId, AdminId, _jobAId, request.Id, Constants.SystemRoles.Client, ClientId));
    }

    [Fact]
    public async Task UpdateTarget_ByClient_ThrowsForbidden()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument, null, null);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _requestService.UpdateTargetAsync(WorkspaceId, ClientId, _jobAId, request.Id, Constants.SystemRoles.Client, null));
    }

    [Fact]
    public async Task Admin_CanGenerateShareLink()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);

        var withLink = await _requestService.GenerateShareLinkAsync(WorkspaceId, AdminId, _jobAId, request.Id);

        Assert.NotNull(withLink.ShareToken);
        Assert.True(withLink.ShareTokenExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task Client_CannotGenerateShareLink()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _requestService.GenerateShareLinkAsync(WorkspaceId, ClientId, _jobAId, request.Id));
    }

    [Fact]
    public async Task RegeneratingShareLink_InvalidatesOldToken()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);
        var first = await _requestService.GenerateShareLinkAsync(WorkspaceId, AdminId, _jobAId, request.Id);
        var oldToken = first.ShareToken!;

        var second = await _requestService.GenerateShareLinkAsync(WorkspaceId, AdminId, _jobAId, request.Id);

        Assert.NotEqual(oldToken, second.ShareToken);
        await Assert.ThrowsAsync<NotFoundException>(() => _requestService.GetByShareTokenAsync(oldToken));
    }

    [Fact]
    public async Task RevokeShareLink_ClearsToken()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);
        var withLink = await _requestService.GenerateShareLinkAsync(WorkspaceId, AdminId, _jobAId, request.Id);

        await _requestService.RevokeShareLinkAsync(WorkspaceId, AdminId, _jobAId, request.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => _requestService.GetByShareTokenAsync(withLink.ShareToken!));
    }

    [Fact]
    public async Task GetByShareToken_UnknownToken_ThrowsNotFound()
    {
        await SeedJobsAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => _requestService.GetByShareTokenAsync("does-not-exist"));
    }

    [Fact]
    public async Task GetByShareToken_ExpiredToken_ThrowsNotFound()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);
        var withLink = await _requestService.GenerateShareLinkAsync(WorkspaceId, AdminId, _jobAId, request.Id);

        var context = GetService<ApplicationDbContext>();
        var tracked = await context.DocumentRequests.FirstAsync(r => r.Id == request.Id);
        tracked.ShareTokenExpiresAt = DateTime.UtcNow.AddDays(-1);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => _requestService.GetByShareTokenAsync(withLink.ShareToken!));
    }

    [Fact]
    public async Task UploadViaShareToken_FulfillsRequest_AttributedToRequester()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);
        var withLink = await _requestService.GenerateShareLinkAsync(WorkspaceId, AdminId, _jobAId, request.Id);

        var fulfilled = await _requestService.UploadViaShareTokenAsync(withLink.ShareToken!, MakeFile());

        Assert.Equal("Fulfilled", fulfilled.Status);
        Assert.Equal(AdminId, fulfilled.FulfilledBy); // RequestedBy in this seed is Admin
    }

    [Fact]
    public async Task UploadViaShareToken_OnAlreadyFulfilledRequest_ThrowsValidation()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);
        var withLink = await _requestService.GenerateShareLinkAsync(WorkspaceId, AdminId, _jobAId, request.Id);
        await _requestService.UploadViaShareTokenAsync(withLink.ShareToken!, MakeFile());

        await Assert.ThrowsAsync<ValidationException>(() =>
            _requestService.UploadViaShareTokenAsync(withLink.ShareToken!, MakeFile()));
    }

    [Fact]
    public async Task UploadViaShareToken_AlwaysUsesClientVisibleRegardlessOfCallerChoice()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);
        var withLink = await _requestService.GenerateShareLinkAsync(WorkspaceId, AdminId, _jobAId, request.Id);

        var fulfilled = await _requestService.UploadViaShareTokenAsync(withLink.ShareToken!, MakeFile());

        var documentService = GetService<IDocumentService>();
        var docs = await documentService.GetDocumentsAsync(WorkspaceId, ClientId, _jobAId);
        var uploaded = Assert.Single(docs, d => d.Id == fulfilled.FulfilledDocumentId);
        Assert.Equal(DocumentVisibility.ClientVisible, uploaded.Visibility);
    }
}
