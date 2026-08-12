# Shareable Upload Link for Document Requests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admin/Surveyor can generate a link for a document request that lets someone upload the file without an account; the link can be revoked or regenerated if it leaks.

**Architecture:** Two nullable columns + a filtered unique index on the existing `DocumentRequest` table — no new entity. Mirrors `Invitation`'s existing unauthenticated-token pattern exactly (public routes with no `[Authorize]`, `Guid.NewGuid("N")` token). The "upload → replace-deletes-previous → link → mark Fulfilled" sequence already in `DocumentRequestService.FulfillAsync` is extracted into one shared private method both the authenticated and token-based upload paths call — one implementation of that invariant, not two.

**Tech Stack:** .NET 9/EF Core 9, Angular 21 standalone/signals — same stack as every other feature this session. Reuses the existing `"auth"` per-IP rate-limit policy from `Program.cs` for the new public endpoints.

## Global Constraints

- Public routes carry no `[Authorize]` and must not require a workspace/job id from the caller — they only know the token.
- Anonymous uploads: `Visibility` is always `ClientVisible` (hardcoded, never caller-supplied), `Category` is inherited from the request, `UploadedBy` is attributed to the request's `RequestedBy`.
- Link stays usable while `Pending`/`Reopened`; rejected once `Fulfilled`.
- Generate/revoke are Admin/Surveyor only (`job.edit`, via `IScopedAccessService.EnsureJobAccessAsync`), same gate as create/reopen/cancel/edit-target.
- The raw `ShareToken` never appears in the normal authenticated `DocumentRequestResponse` — only a boolean `HasActiveShareLink`, and the actual token only in the generate endpoint's own response.
- Migrations generated via `dotnet ef migrations add`, never hand-edited.
- Do not run `git commit` for any step — commit only when the user explicitly says to.
- Spec: `docs/superpowers/specs/2026-08-11-document-request-share-link-design.md`.

---

### Task 1: Entity, EF config, migration

**Files:**
- Modify: `api/src/SurveyorLedger.Data/Entities/DocumentRequest.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/DocumentRequestConfiguration.cs`
- Migration: generated under `api/src/SurveyorLedger.Data/Migrations/`

**Interfaces:**
- Produces: `DocumentRequest.ShareToken` (`string?`), `DocumentRequest.ShareTokenExpiresAt` (`DateTime?`). Task 2 reads/writes these directly.

- [ ] **Step 1: Add the fields**

In `api/src/SurveyorLedger.Data/Entities/DocumentRequest.cs`, add after `TargetUserId`:

```csharp
public string? ShareToken { get; set; }
public DateTime? ShareTokenExpiresAt { get; set; }
```

- [ ] **Step 2: Add the EF configuration**

In `api/src/SurveyorLedger.Data/Configurations/DocumentRequestConfiguration.cs`, add inside `Configure`, after the existing `CK_DocumentRequests_TargetExclusive` block:

```csharp
builder.Property(x => x.ShareToken).HasMaxLength(64);
builder.HasIndex(x => x.ShareToken).IsUnique().HasFilter("[ShareToken] IS NOT NULL");
```

Filtered unique index — only enforced when non-null, so multiple requests with no active link (`NULL`) don't collide on uniqueness.

- [ ] **Step 3: Generate and apply the migration**

```bash
cd api
dotnet ef migrations add AddDocumentRequestShareLink --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```

Expected: two new columns, one filtered unique index, no other schema change. Run the `migration-check` skill checklist.

- [ ] **Step 4: Build**

```bash
cd api && dotnet build SurveyorLedger.sln
```

Expected: succeeds.

---

### Task 2: `DocumentRequestService` — generate/revoke/token-lookup/token-upload, shared fulfillment core

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/DocumentRequestService.cs`

**Interfaces:**
- Consumes: `IScopedAccessService.EnsureJobAccessAsync` (existing), `IDocumentService.UploadAsync` (existing).
- Produces: `IDocumentRequestService` gains:
  - `Task<DocumentRequest> GenerateShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId)`
  - `Task RevokeShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId)`
  - `Task<DocumentRequest> GetByShareTokenAsync(string token)`
  - `Task<DocumentRequest> UploadViaShareTokenAsync(string token, IFormFile file, string? displayFileName = null)`

  Task 3's `DocumentRequestController`/new `DocumentRequestLinkController` consume these exact signatures.

- [ ] **Step 1: Add the four methods to the interface**

In `api/src/SurveyorLedger.API/Services/DocumentRequestService.cs`, add to `IDocumentRequestService`:

```csharp
Task<DocumentRequest> GenerateShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId);
Task RevokeShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId);
Task<DocumentRequest> GetByShareTokenAsync(string token);
Task<DocumentRequest> UploadViaShareTokenAsync(string token, IFormFile file, string? displayFileName = null);
```

- [ ] **Step 2: Extract the shared fulfillment core from `FulfillAsync`**

Replace the current `FulfillAsync` body (the block from `// Reopening keeps...` through `return request;`) with a call to a new private method, and add that method. `FulfillAsync` becomes:

```csharp
public async Task<DocumentRequest> FulfillAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId, IFormFile file, DocumentVisibility visibility, string? displayFileName = null)
{
    await FindJobAsync(workspaceId, jobId);
    await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

    var request = await FindRequestAsync(jobId, requestId);

    if (request.TargetUserId.HasValue && request.TargetUserId != callerUserId)
        throw new ForbiddenException("This request is for a specific person.");

    if (request.TargetRole != null)
    {
        var callerRole = await _access.GetEffectiveJobRoleAsync(callerUserId, workspaceId, jobId);
        if (callerRole != request.TargetRole)
            throw new ForbiddenException($"This request is for the {request.TargetRole} role.");
    }

    return await LinkFulfilledDocumentAsync(workspaceId, jobId, request, file, visibility, callerUserId, displayFileName);
}
```

Add the extracted core as a new private method (this is the exact body `FulfillAsync` had before, unchanged, just parameterized on `attributedUserId` instead of always using `callerUserId`):

```csharp
/// <summary>
/// Shared by FulfillAsync (authenticated) and UploadViaShareTokenAsync (anonymous, via
/// link) - one implementation of "upload, replace-deletes-previous, link, mark Fulfilled"
/// regardless of which path got here. attributedUserId is the caller for FulfillAsync,
/// or the request's RequestedBy for an anonymous link upload (no real caller to attribute to).
/// </summary>
private async Task<DocumentRequest> LinkFulfilledDocumentAsync(Guid workspaceId, Guid jobId, DocumentRequest request, IFormFile file, DocumentVisibility visibility, Guid attributedUserId, string? displayFileName)
{
    // Reopening keeps the previous FulfilledDocumentId as a reference (not cleared) so
    // the old file and the "via request" link stay visible until a replacement lands.
    // No versioning support: once a replacement is uploaded, the old document is
    // superseded and soft-deleted here rather than kept alongside it.
    var previousDocumentId = request.FulfilledDocumentId;

    var document = await _documentService.UploadAsync(workspaceId, attributedUserId, jobId, file, request.Category, visibility, displayFileName);

    request.FulfilledDocumentId = document.Id;
    request.FulfilledAt = DateTime.UtcNow;
    request.FulfilledBy = attributedUserId;
    request.Status = "Fulfilled";
    request.UpdatedAt = DateTime.UtcNow;

    if (previousDocumentId.HasValue)
    {
        // Not IDocumentService.DeleteAsync: that requires job.edit, but a Client (or an
        // anonymous link uploader) fulfilling their own request must be able to trigger
        // this - the access check already done by the caller is what actually authorizes it.
        var previousDocument = await _context.Documents.FindAsync(previousDocumentId.Value);
        if (previousDocument != null)
        {
            previousDocument.IsActive = false;
            previousDocument.UpdatedAt = DateTime.UtcNow;
        }
    }

    await _context.SaveChangesAsync();
    return request;
}
```

- [ ] **Step 3: Add `GenerateShareLinkAsync` and `RevokeShareLinkAsync`**

Add after `UpdateTargetAsync`:

```csharp
public async Task<DocumentRequest> GenerateShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId)
{
    await FindJobAsync(workspaceId, jobId);
    await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

    var request = await FindRequestAsync(jobId, requestId);
    // Overwriting an existing token is deliberate - the old link stops resolving
    // immediately, so "generate again" doubles as instant revoke-and-reissue.
    request.ShareToken = Guid.NewGuid().ToString("N");
    request.ShareTokenExpiresAt = DateTime.UtcNow.AddDays(7);
    request.UpdatedAt = DateTime.UtcNow;

    await _context.SaveChangesAsync();
    return request;
}

public async Task RevokeShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId)
{
    await FindJobAsync(workspaceId, jobId);
    await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

    var request = await FindRequestAsync(jobId, requestId);
    request.ShareToken = null;
    request.ShareTokenExpiresAt = null;
    request.UpdatedAt = DateTime.UtcNow;

    await _context.SaveChangesAsync();
}
```

- [ ] **Step 4: Add `GetByShareTokenAsync` and `UploadViaShareTokenAsync`**

Add after `RevokeShareLinkAsync`. These have no `workspaceId`/`callerUserId` - the token itself is the only credential:

```csharp
public async Task<DocumentRequest> GetByShareTokenAsync(string token)
{
    var request = await _context.DocumentRequests
        .FirstOrDefaultAsync(r => r.ShareToken == token && r.IsActive)
        ?? throw new NotFoundException("Link not found");

    if (request.ShareTokenExpiresAt is null || request.ShareTokenExpiresAt <= DateTime.UtcNow)
        throw new NotFoundException("Link not found");

    return request;
}

public async Task<DocumentRequest> UploadViaShareTokenAsync(string token, IFormFile file, string? displayFileName = null)
{
    var request = await GetByShareTokenAsync(token);
    var job = await _context.Jobs.FirstAsync(j => j.Id == request.JobId);

    if (request.Status == "Fulfilled")
        throw new ValidationException("This document has already been provided.");

    return await LinkFulfilledDocumentAsync(job.WorkspaceId, job.Id, request, file, DocumentVisibility.ClientVisible, request.RequestedBy, displayFileName);
}
```

`GetByShareTokenAsync` deliberately throws the same `NotFoundException` for "doesn't exist" and "expired" - a public endpoint shouldn't distinguish those for an attacker probing tokens, same "don't reveal existence" reasoning already used elsewhere in this codebase. The controller layer (Task 3) computes a friendlier `Expired`/`AlreadyFulfilled` flag for the preview response by checking the row directly before this throws, not by relying on this method's error message.

- [ ] **Step 5: Build**

```bash
cd api && dotnet build SurveyorLedger.sln
```

Expected: succeeds.

---

### Task 3: API surface — `DocumentRequestController` additions, new `DocumentRequestLinkController`, DI/rate-limit wiring

**Files:**
- Modify: `api/src/SurveyorLedger.API/Controllers/DocumentRequestController.cs`
- Modify: `api/src/SurveyorLedger.API/Models/DocumentRequest/DocumentRequestResponse.cs`
- Create: `api/src/SurveyorLedger.API/Models/DocumentRequest/DocumentRequestLinkPreviewResponse.cs`
- Create: `api/src/SurveyorLedger.API/Models/DocumentRequest/DocumentRequestShareLinkResponse.cs`
- Create: `api/src/SurveyorLedger.API/Controllers/DocumentRequestLinkController.cs`
- Modify: `api/src/SurveyorLedger.API/Program.cs`

**Interfaces:**
- Consumes: `IDocumentRequestService` (Task 2) exact signatures.
- Produces: the 4 new HTTP routes listed in Global Constraints.

- [ ] **Step 1: Add `HasActiveShareLink` to the normal response**

In `DocumentRequestResponse.cs`, add after `TargetUserName`:

```csharp
public bool HasActiveShareLink { get; set; }
```

- [ ] **Step 2: Add the two new response DTOs**

`api/src/SurveyorLedger.API/Models/DocumentRequest/DocumentRequestLinkPreviewResponse.cs`:

```csharp
using SurveyorLedger.Core;

namespace SurveyorLedger.API.Models.DocumentRequest;

public class DocumentRequestLinkPreviewResponse
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DocumentCategory? Category { get; set; }
    public string? WorkspaceName { get; set; }
    public string? JobTitle { get; set; }
    public required bool Expired { get; set; }
    public required bool AlreadyFulfilled { get; set; }
}
```

All the request-identifying fields are nullable and only populated when `!Expired` - an expired link's preview has nothing to show beyond the two flags.

`api/src/SurveyorLedger.API/Models/DocumentRequest/DocumentRequestShareLinkResponse.cs`:

```csharp
namespace SurveyorLedger.API.Models.DocumentRequest;

public class DocumentRequestShareLinkResponse
{
    public required string Token { get; set; }
    public required DateTime ExpiresAt { get; set; }
}
```

- [ ] **Step 3: Add generate/revoke to `DocumentRequestController`**

In `DocumentRequestController.cs`, add after `UpdateTarget`:

```csharp
[HttpPost("{id}/share-link")]
public async Task<ActionResult<ApiResponse<DocumentRequestShareLinkResponse>>> GenerateShareLink(Guid workspaceId, Guid jobId, Guid id)
{
    var updated = await _requestService.GenerateShareLinkAsync(workspaceId, CallerId(), jobId, id);
    return Ok(ApiResponse<DocumentRequestShareLinkResponse>.Ok(new DocumentRequestShareLinkResponse
    {
        Token = updated.ShareToken!,
        ExpiresAt = updated.ShareTokenExpiresAt!.Value
    }));
}

[HttpDelete("{id}/share-link")]
public async Task<IActionResult> RevokeShareLink(Guid workspaceId, Guid jobId, Guid id)
{
    await _requestService.RevokeShareLinkAsync(workspaceId, CallerId(), jobId, id);
    return NoContent();
}
```

And add `HasActiveShareLink` to `ToResponse`:

```csharp
HasActiveShareLink = r.ShareToken != null && r.ShareTokenExpiresAt > DateTime.UtcNow,
```

- [ ] **Step 4: Write the public `DocumentRequestLinkController`**

`api/src/SurveyorLedger.API/Controllers/DocumentRequestLinkController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.DocumentRequest;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;

namespace SurveyorLedger.API.Controllers
{
    /// <summary>
    /// Deliberately separate from DocumentRequestController: every action here is
    /// unauthenticated by design (the token is the only credential), and keeping that on
    /// its own controller makes the trust boundary visible at a glance rather than mixed
    /// into a controller whose every other action requires [Authorize].
    /// </summary>
    [ApiController]
    [Route("api/document-request-links")]
    [EnableRateLimiting("auth")]
    public class DocumentRequestLinkController : ControllerBase
    {
        private readonly IDocumentRequestService _requestService;
        private readonly ApplicationDbContext _context;

        public DocumentRequestLinkController(IDocumentRequestService requestService, ApplicationDbContext context)
        {
            _requestService = requestService;
            _context = context;
        }

        [HttpGet("{token}")]
        public async Task<ActionResult<ApiResponse<DocumentRequestLinkPreviewResponse>>> Preview(string token)
        {
            var request = await _context.DocumentRequests.FirstOrDefaultAsync(r => r.ShareToken == token && r.IsActive);
            if (request == null)
                throw new NotFoundException("Link not found");

            var expired = request.ShareTokenExpiresAt is null || request.ShareTokenExpiresAt <= DateTime.UtcNow;
            if (expired)
                return Ok(ApiResponse<DocumentRequestLinkPreviewResponse>.Ok(new DocumentRequestLinkPreviewResponse { Expired = true, AlreadyFulfilled = false }));

            var job = await _context.Jobs.FirstAsync(j => j.Id == request.JobId);
            var workspace = await _context.Workspaces.FirstAsync(w => w.Id == job.WorkspaceId);

            return Ok(ApiResponse<DocumentRequestLinkPreviewResponse>.Ok(new DocumentRequestLinkPreviewResponse
            {
                Title = request.Title,
                Description = request.Description,
                Category = request.Category,
                WorkspaceName = workspace.Name,
                JobTitle = job.Title,
                Expired = false,
                AlreadyFulfilled = request.Status == "Fulfilled"
            }));
        }

        [HttpPost("{token}/upload")]
        [RequestSizeLimit(DocumentService.MaxFileSizeBytes)]
        public async Task<IActionResult> Upload(string token, [FromForm] DocumentRequestLinkUploadRequest request)
        {
            await _requestService.UploadViaShareTokenAsync(token, request.File, request.DisplayFileName);
            return NoContent();
        }
    }
}
```

`Preview` queries directly rather than through `GetByShareTokenAsync` because it needs the "expired vs not found" distinction absorbed into a 200 response with flags (per the spec, so the public page can render a clear message), while `Upload` goes through the service since it needs the actual mutation and the service's `NotFoundException` there is exactly the right behavior (unknown/expired/revoked token all look identical to an upload attempt).

- [ ] **Step 5: Add the upload request DTO**

`api/src/SurveyorLedger.API/Models/DocumentRequest/DocumentRequestLinkUploadRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace SurveyorLedger.API.Models.DocumentRequest;

public class DocumentRequestLinkUploadRequest
{
    [Required(ErrorMessage = "File is required.")]
    public required IFormFile File { get; set; }

    public string? DisplayFileName { get; set; }
}
```

No `Visibility` field here - hardcoded `ClientVisible` server-side per the spec, never caller-supplied.

- [ ] **Step 6: Build**

```bash
cd api && dotnet build SurveyorLedger.sln
```

Expected: succeeds.

---

### Task 4: Backend tests

**Files:**
- Modify: `api/tests/SurveyorLedger.API.Tests/Services/DocumentRequestServiceTests.cs`

**Interfaces:**
- Consumes: `IDocumentRequestService`'s 4 new methods (Task 2).

- [ ] **Step 1: Add the tests**

Add to the existing `DocumentRequestServiceTests` class (same base/seeding pattern already in the file):

```csharp
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
```

`UploadViaShareToken_FulfillsRequest_AttributedToRequester` asserts `FulfilledBy == AdminId` because `SeedJobsAsync` (existing helper in this file) always creates requests via `AdminId` - `RequestedBy` is whoever calls `CreateAsync`, and the token-upload path attributes to that same id.

- [ ] **Step 2: Run the tests**

```bash
cd api && dotnet test tests/SurveyorLedger.API.Tests --filter DocumentRequestServiceTests
```

Expected: all pass (previous count + 9 new).

- [ ] **Step 3: Run the full backend suite**

```bash
cd api && dotnet build SurveyorLedger.sln && dotnet test tests/SurveyorLedger.API.Tests
```

Expected: build succeeds, all tests pass.

---

### Task 5: Angular services

**Files:**
- Modify: `ui/src/app/core/document-request.service.ts`
- Create: `ui/src/app/core/document-request-link.service.ts`
- Modify: `ui/src/app/core/document-request.service.spec.ts`
- Create: `ui/src/app/core/document-request-link.service.spec.ts`

**Interfaces:**
- Produces: `DocumentRequest.hasActiveShareLink: boolean` (added field). `DocumentRequestService.generateShareLink(workspaceId, jobId, requestId): Observable<{ token: string; expiresAt: string }>`, `.revokeShareLink(workspaceId, jobId, requestId): Observable<void>`. New `DocumentRequestLinkService` with `getPreview(token): Observable<LinkPreview>`, `upload(token, file, displayFileName?): Observable<void>`. Task 6/7 consume these exact signatures.

- [ ] **Step 1: Add `hasActiveShareLink` and the two authenticated methods**

In `ui/src/app/core/document-request.service.ts`, add to the `DocumentRequest` interface, after `targetUserName`:

```ts
hasActiveShareLink: boolean;
```

Add to `DocumentRequestService`, after `updateTarget`:

```ts
generateShareLink(workspaceId: string, jobId: string, requestId: string): Observable<{ token: string; expiresAt: string }> {
  return this.http
    .post<ApiResponse<{ token: string; expiresAt: string }>>(`${this.base(workspaceId, jobId)}/${requestId}/share-link`, {})
    .pipe(map(res => res.data));
}

revokeShareLink(workspaceId: string, jobId: string, requestId: string): Observable<void> {
  return this.http.delete<void>(`${this.base(workspaceId, jobId)}/${requestId}/share-link`);
}
```

- [ ] **Step 2: Update the existing spec's sample data**

In `ui/src/app/core/document-request.service.spec.ts`, add `hasActiveShareLink: false,` to the `sample` object (after `targetUserName: null,`), and add two new tests after the `updateTarget()` test:

```ts
it('generateShareLink() posts to /{id}/share-link', () => {
  const link = { token: 'abc123', expiresAt: '2026-01-08' };
  service.generateShareLink(workspaceId, jobId, 'r1').subscribe(result => expect(result).toEqual(link));
  const req = httpMock.expectOne(`${base}/r1/share-link`);
  expect(req.request.method).toBe('POST');
  req.flush({ success: true, data: link });
});

it('revokeShareLink() deletes /{id}/share-link', () => {
  service.revokeShareLink(workspaceId, jobId, 'r1').subscribe();
  const req = httpMock.expectOne(`${base}/r1/share-link`);
  expect(req.request.method).toBe('DELETE');
  req.flush(null);
});
```

- [ ] **Step 3: Write the public link service**

`ui/src/app/core/document-request-link.service.ts`:

```ts
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface DocumentRequestLinkPreview {
  title: string | null;
  description: string | null;
  category: 'SurveyPlan' | 'LegalDocument' | 'Photo' | 'Other' | null;
  workspaceName: string | null;
  jobTitle: string | null;
  expired: boolean;
  alreadyFulfilled: boolean;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

/**
 * Deliberately separate from DocumentRequestService: this service never has a workspace
 * or job id to send, and never attaches an auth header - it structurally can't, since the
 * token is the only thing identifying what's being uploaded to.
 */
@Injectable({ providedIn: 'root' })
export class DocumentRequestLinkService {
  constructor(private http: HttpClient) {}

  private base(token: string): string {
    return `${environment.apiBaseUrl}/document-request-links/${token}`;
  }

  getPreview(token: string): Observable<DocumentRequestLinkPreview> {
    return this.http.get<ApiResponse<DocumentRequestLinkPreview>>(this.base(token)).pipe(map(res => res.data));
  }

  upload(token: string, file: File, displayFileName?: string): Observable<void> {
    const form = new FormData();
    form.append('File', file);
    if (displayFileName) form.append('DisplayFileName', displayFileName);
    return this.http.post<void>(`${this.base(token)}/upload`, form);
  }
}
```

- [ ] **Step 4: Write its spec**

`ui/src/app/core/document-request-link.service.spec.ts`:

```ts
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { DocumentRequestLinkService } from './document-request-link.service';
import { environment } from '../../environments/environment';

describe('DocumentRequestLinkService', () => {
  let service: DocumentRequestLinkService;
  let httpMock: HttpTestingController;
  const token = 'abc123';
  const base = `${environment.apiBaseUrl}/document-request-links/${token}`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [DocumentRequestLinkService]
    });
    service = TestBed.inject(DocumentRequestLinkService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getPreview() unwraps ApiResponse', () => {
    const preview = { title: 'Legal Deed', description: null, category: 'LegalDocument', workspaceName: 'Acme', jobTitle: 'Job 1', expired: false, alreadyFulfilled: false };
    service.getPreview(token).subscribe(result => expect(result).toEqual(preview));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: preview });
  });

  it('upload() posts FormData with File and optional DisplayFileName', () => {
    const file = new File(['bytes'], 'deed.pdf', { type: 'application/pdf' });
    service.upload(token, file, 'Renamed.pdf').subscribe();
    const req = httpMock.expectOne(`${base}/upload`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);
    const body = req.request.body as FormData;
    expect(body.get('File')).toBe(file);
    expect(body.get('DisplayFileName')).toBe('Renamed.pdf');
    req.flush(null);
  });
});
```

- [ ] **Step 5: Run both specs**

```bash
cd ui && ng test --watch=false --include='**/document-request.service.spec.ts' --include='**/document-request-link.service.spec.ts'
```

Expected: all pass.

---

### Task 6: Wire generate/revoke into `job-detail.component.ts`

**Files:**
- Modify: `ui/src/app/pages/job/job-detail.component.ts`

**Interfaces:**
- Consumes: `DocumentRequestService.generateShareLink`/`.revokeShareLink` (Task 5).

- [ ] **Step 1: Add the Copy link / Revoke link buttons**

In the pending/reopened request row's button group (the `<div class="flex items-center gap-sm flex-shrink-0 whitespace-nowrap">` block containing the Upload/Edit target/Cancel buttons), add after the "Edit target" button, still inside the existing `@if (!isClient())` block:

```html
@if (row.request!.hasActiveShareLink) {
  <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="revokeShareLink(row.request!)">Revoke link</button>
} @else {
  <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="copyShareLink(row.request!)">Copy link</button>
}
```

- [ ] **Step 2: Add the two methods**

Add near `startEditTarget`/`cancelEditTarget`:

```ts
copyShareLink(request: DocumentRequest): void {
  this.documentRequestService.generateShareLink(this.workspaceId, this.jobId, request.requestId).subscribe({
    next: ({ token }) => {
      this.documentRequests.update(list => list.map(r => (r.requestId === request.requestId ? { ...r, hasActiveShareLink: true } : r)));
      navigator.clipboard.writeText(`${window.location.origin}/document-upload/${token}`);
      this.requestError.set('');
    },
    error: (err) => this.requestError.set(err.error?.message ?? 'Could not generate link.')
  });
}

revokeShareLink(request: DocumentRequest): void {
  this.documentRequestService.revokeShareLink(this.workspaceId, this.jobId, request.requestId).subscribe({
    next: () => this.documentRequests.update(list => list.map(r => (r.requestId === request.requestId ? { ...r, hasActiveShareLink: false } : r))),
    error: (err) => this.requestError.set(err.error?.message ?? 'Could not revoke link.')
  });
}
```

`copyShareLink` patches `hasActiveShareLink` locally from the generate response rather than re-fetching the whole list - same pattern already used by `fulfillRequest`/`reopenRequest` elsewhere in this file.

- [ ] **Step 3: Build**

```bash
cd ui && ng build --configuration development
```

Expected: succeeds.

---

### Task 7: Public upload page

**Files:**
- Create: `ui/src/app/pages/document-upload/public-document-upload.component.ts`
- Modify: `ui/src/app/app.routes.ts`

**Interfaces:**
- Consumes: `DocumentRequestLinkService` (Task 5).
- Produces: route `/document-upload/:token`, outside the auth guard.

- [ ] **Step 1: Write the component**

`ui/src/app/pages/document-upload/public-document-upload.component.ts`, following `AcceptInviteComponent`'s standalone-public-page structure exactly (`min-h-screen flex items-center justify-center bg-neutral-50 px-lg` + `card w-full max-w-sm`, no shell):

```ts
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { DocumentRequestLinkService, DocumentRequestLinkPreview } from '../../core/document-request-link.service';

@Component({
  selector: 'app-public-document-upload',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="min-h-screen flex items-center justify-center bg-neutral-50 px-lg">
      <div class="card w-full max-w-sm">
        @if (loading()) {
          <p class="text-sm text-neutral-500">Loading…</p>
        } @else if (loadError()) {
          <h1 class="text-lg font-semibold text-neutral-900">Link unavailable</h1>
          <p class="text-sm text-neutral-600 mt-xs">{{ loadError() }}</p>
        } @else if (preview(); as p) {
          @if (p.expired) {
            <h1 class="text-lg font-semibold text-neutral-900">Link expired</h1>
            <p class="text-sm text-neutral-600 mt-xs">Ask whoever sent this link to generate a new one.</p>
          } @else if (p.alreadyFulfilled) {
            <h1 class="text-lg font-semibold text-neutral-900">Already provided</h1>
            <p class="text-sm text-neutral-600 mt-xs">This document has already been uploaded. No further action is needed.</p>
          } @else if (uploaded()) {
            <h1 class="text-lg font-semibold text-neutral-900">Uploaded</h1>
            <p class="text-sm text-neutral-600 mt-xs">Thank you - the file has been received.</p>
          } @else {
            <h1 class="text-lg font-semibold text-neutral-900">{{ p.title }}</h1>
            @if (p.jobTitle || p.workspaceName) {
              <p class="text-xs text-neutral-500 mt-xs">{{ p.workspaceName }} · {{ p.jobTitle }}</p>
            }
            @if (p.description) {
              <p class="text-sm text-neutral-600 mt-sm">{{ p.description }}</p>
            }

            <input
              #fileInput
              class="input-field text-sm mt-lg"
              type="file"
              (change)="onFileSelected(fileInput.files)"
            />
            @if (selectedFile) {
              <input class="input-field text-sm mt-sm" placeholder="File name" [(ngModel)]="fileNameDraft" />
            }
            @if (uploadError()) {
              <p class="text-sm text-primary-500 mt-sm">{{ uploadError() }}</p>
            }
            <button type="button" class="btn-primary w-full mt-lg" [disabled]="!selectedFile || uploading()" (click)="submit()">
              {{ uploading() ? 'Uploading…' : 'Upload' }}
            </button>
          }
        }
      </div>
    </div>
  `
})
export class PublicDocumentUploadComponent implements OnInit {
  token = '';
  loading = signal(true);
  loadError = signal('');
  preview = signal<DocumentRequestLinkPreview | null>(null);
  selectedFile: File | null = null;
  fileNameDraft = '';
  uploading = signal(false);
  uploadError = signal('');
  uploaded = signal(false);

  constructor(private route: ActivatedRoute, private linkService: DocumentRequestLinkService) {}

  ngOnInit(): void {
    this.token = this.route.snapshot.paramMap.get('token') ?? '';
    this.linkService.getPreview(this.token).subscribe({
      next: (preview) => {
        this.preview.set(preview);
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set('This link is invalid.');
        this.loading.set(false);
      }
    });
  }

  onFileSelected(files: FileList | null): void {
    this.selectedFile = files?.item(0) ?? null;
    this.fileNameDraft = this.selectedFile?.name ?? '';
    this.uploadError.set('');
  }

  submit(): void {
    if (!this.selectedFile) return;
    this.uploadError.set('');
    this.uploading.set(true);
    this.linkService.upload(this.token, this.selectedFile, this.fileNameDraft.trim()).subscribe({
      next: () => {
        this.uploading.set(false);
        this.uploaded.set(true);
      },
      error: (err) => {
        this.uploading.set(false);
        this.uploadError.set(err.error?.message ?? 'Could not upload file.');
      }
    });
  }
}
```

- [ ] **Step 2: Register the route**

In `ui/src/app/app.routes.ts`, add alongside the existing `invite/:token` route (outside the `app`/guarded section):

```ts
import { PublicDocumentUploadComponent } from './pages/document-upload/public-document-upload.component';
```

```ts
{ path: 'document-upload/:token', component: PublicDocumentUploadComponent },
```

- [ ] **Step 3: Build and manually verify**

```bash
cd ui && ng build --configuration development
cd api && dotnet build SurveyorLedger.sln && dotnet test tests/SurveyorLedger.API.Tests
```

Expected: both succeed, full backend suite green.

Manually, with API + UI running:
1. As Admin, click "Copy link" on a pending request - confirm the row switches to "Revoke link" and a URL is on the clipboard.
2. Open that URL in an incognito window (no login) - confirm the preview renders (title/description/category/job context), no app shell.
3. Upload a file - confirm success state, and that the request now shows Fulfilled in the normal authenticated view.
4. Revisit the same link - confirm it now shows "Already provided."
5. As Admin, "Revoke link" on a still-pending request, then visit the old (now-stolen-from-clipboard) URL - confirm it shows "Link unavailable" / invalid, not the upload form.
6. Generate a link twice in a row without revoking - confirm the first token no longer resolves once the second is issued.

- [ ] **Step 4: Do not commit** (per Global Constraints — wait for explicit instruction)

---

## Self-Review Notes

- **Spec coverage:** entity/migration (Task 1), service methods + shared fulfillment core refactor (Task 2), authenticated + public API surface + rate limiting (Task 3), backend tests including revoke/regenerate/expiry/attribution/visibility (Task 4), Angular services (Task 5), generate/revoke UI (Task 6), public page + route (Task 7). All spec sections covered, including the revoke addition from the follow-up round.
- **Type consistency:** `IDocumentRequestService`'s 4 new method signatures (Task 2) match exactly what Task 3's two controllers call. `DocumentRequestLinkService`'s methods (Task 5) match what Task 7's component calls.
- **Security check:** public routes carry no `[Authorize]` (correct - that's the point) but do carry `[EnableRateLimiting("auth")]` (reusing the existing per-IP policy, not a new one). Anonymous upload path never accepts a caller-supplied `Visibility`. Token comparison happens via EF's translated `==` (parameterized SQL), not string concatenation - no injection surface. `GetByShareTokenAsync` gives identical errors for missing/expired/revoked (no existence oracle for an attacker probing tokens).
