# Land Photos Card + Batch Document Uploads Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split Land photos back into their own card, let multi-file uploads (direct or via a document request) render/manage as one group, let a request accumulate files across a reopen cycle, add proper Sri Lanka province/district selects with cross-clearing, fix unreliable Leaflet map rendering, and tidy the owner-picker's "not in system" link.

**Architecture:** Client-generated `BatchId` (a `crypto.randomUUID()`) is threaded through the existing per-file upload/fulfill endpoints as one new optional form field — no new bulk endpoints. `Document`/`DocumentRequest`/`LandDocumentRequest` gain a nullable `UploadBatchId`/`FulfilledBatchId` column each. `DocumentListComponent` groups its flat `rows` input by `batchId` at render time. Everything else (province/district data, map `ResizeObserver`, owner-picker layout) is self-contained frontend-only changes.

**Tech Stack:** .NET 9 / EF Core 9 / SQL Server LocalDB backend; Angular 21 standalone components/signals frontend.

## Global Constraints

- Tenant isolation: every tenant-scoped query goes through `WorkspaceId` filtering — no exceptions.
- Migrations are generated via `dotnet ef migrations add`, never hand-edited.
- No new bulk-upload/bulk-delete backend endpoints — batch grouping is a client-side read over existing single-file calls (per the approved spec, section 2/3).
- `docs/superpowers/specs/2026-08-19-land-photos-batch-uploads-design.md` is the source of truth for behavior; this plan implements it task by task.

---

### Task 1: `Document.UploadBatchId` column

**Files:**
- Modify: `api/src/SurveyorLedger.Data/Entities/Document.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/DocumentConfiguration.cs`
- Create: EF migration via `dotnet ef migrations add`

**Interfaces:**
- Produces: `Document.UploadBatchId` (`Guid?`) — every later task that creates/reads a `Document` uses this.

- [ ] **Step 1: Add the property**

In `api/src/SurveyorLedger.Data/Entities/Document.cs`, add after `public bool IsActive { get; set; } = true;`:

```csharp
/// <summary>Groups Documents uploaded together (direct multi-file select or a multi-file request fulfillment) into one unit for display/delete - null for a lone upload. Client-generated, not server-assigned.</summary>
public Guid? UploadBatchId { get; set; }
```

- [ ] **Step 2: Index it**

In `api/src/SurveyorLedger.Data/Configurations/DocumentConfiguration.cs`, change:

```csharp
builder.HasIndex(x => new { x.OwnerType, x.OwnerId });
```

to:

```csharp
builder.HasIndex(x => new { x.OwnerType, x.OwnerId });
builder.HasIndex(x => new { x.OwnerType, x.OwnerId, x.UploadBatchId });
```

- [ ] **Step 3: Generate the migration**

Run: `cd api && dotnet ef migrations add AddDocumentUploadBatchId --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`

Verify the generated migration only adds the nullable `UploadBatchId` column and the new index — no unrelated diffs. Do not hand-edit it.

- [ ] **Step 4: Apply and build**

Run: `dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`
Run: `dotnet build`
Expected: builds clean.

- [ ] **Step 5: Commit**

```bash
git add api/src/SurveyorLedger.Data/Entities/Document.cs api/src/SurveyorLedger.Data/Configurations/DocumentConfiguration.cs api/src/SurveyorLedger.Data/Migrations/
git commit -m "feat: add Document.UploadBatchId for grouping multi-file uploads"
```

---

### Task 2: `BatchId` flows through Document upload endpoints

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/DocumentService.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Document/DocumentUploadRequest.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/DocumentController.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/LandController.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Document/DocumentResponse.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Land/OwnedDocumentResponse.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Land/LandPhotoResponse.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/LandOwnershipTests.cs` (or nearest existing document-upload test file)

**Interfaces:**
- Consumes: `Document.UploadBatchId` from Task 1.
- Produces: `IDocumentService.UploadAsync(..., Guid? batchId = null)`, `UploadOwnedDocumentAsync(..., Guid? batchId = null)`, `UploadOwnedDocumentForFulfillmentAsync(..., Guid? batchId = null)` — Task 4/5 (request fulfillment) call these with a batch id. `DocumentResponse`/`OwnedDocumentResponse`/`LandPhotoResponse` gain `UploadBatchId`/`BatchId`.

- [ ] **Step 1: Add `batchId` parameter to `IDocumentService`**

In `api/src/SurveyorLedger.API/Services/DocumentService.cs`, change the interface signatures:

```csharp
Task<Document> UploadAsync(Guid workspaceId, Guid callerUserId, Guid jobId, IFormFile file, DocumentCategory category, DocumentVisibility visibility, string? displayFileName = null, Guid? batchId = null);
...
Task<Document> UploadOwnedDocumentAsync(Guid workspaceId, Guid callerUserId, Guid landId, string ownerType, Guid ownerId, DocumentCategory category, IFormFile file, string? displayFileName = null, Guid? batchId = null);
Task<Document> UploadOwnedDocumentForFulfillmentAsync(Guid workspaceId, Guid callerUserId, Guid landId, string ownerType, Guid ownerId, DocumentCategory category, IFormFile file, string? displayFileName = null, Guid? batchId = null);
```

Update the three implementations to accept and thread `batchId` through, and in `UploadAsync` set `UploadBatchId = batchId` on the new `Document`. In `UploadOwnedDocumentCoreAsync`, add a `Guid? batchId` parameter (threaded from both public callers) and set `UploadBatchId = batchId` on the new `Document` there too.

- [ ] **Step 2: `DocumentUploadRequest` gains `BatchId`**

In `api/src/SurveyorLedger.API/Models/Document/DocumentUploadRequest.cs`, add:

```csharp
public Guid? BatchId { get; set; }
```

- [ ] **Step 3: `DocumentController.Upload` passes it through**

In `api/src/SurveyorLedger.API/Controllers/DocumentController.cs`, change the `Upload` action body:

```csharp
var document = await _documentService.UploadAsync(workspaceId, CallerId(), jobId, request.File, request.Category, request.Visibility, request.DisplayFileName, request.BatchId);
```

- [ ] **Step 4: `LandController`'s three land upload actions accept `batchId`**

In `api/src/SurveyorLedger.API/Controllers/LandController.cs`, change `UploadDocument`, `UploadSurveyDocument`, `UploadDeedDocument`, `UploadPhoto` to accept `[FromQuery] Guid? batchId = null` alongside their existing `IFormFile file` parameter, and pass it as the last argument to the matching `_documentService.UploadOwnedDocumentAsync(...)` call. Example for `UploadDocument`:

```csharp
[HttpPost("{id}/documents")]
[RequestSizeLimit(DocumentService.MaxFileSizeBytes)]
public async Task<ActionResult<ApiResponse<OwnedDocumentResponse>>> UploadDocument(Guid workspaceId, Guid id, IFormFile file, [FromQuery] DocumentCategory category = DocumentCategory.Other, [FromQuery] Guid? batchId = null)
{
    var callerId = CallerId();
    var document = await _documentService.UploadOwnedDocumentAsync(workspaceId, callerId, id, "Land", id, category, file, batchId: batchId);
    return Ok(ApiResponse<OwnedDocumentResponse>.Ok(ToOwnedDocumentResponse(document)));
}
```

Apply the same `[FromQuery] Guid? batchId = null` + pass-through pattern to `UploadSurveyDocument`, `UploadDeedDocument`, `UploadPhoto`.

- [ ] **Step 5: Surface `UploadBatchId` on response DTOs**

Add `public Guid? UploadBatchId { get; set; }` to `DocumentResponse` and `OwnedDocumentResponse`, and `public Guid? BatchId { get; set; }` to `LandPhotoResponse` (kept as a distinct name there only because `LandPhotoResponse` already uses `PhotoId` not `DocumentId` — same underlying value). Update the three `ToResponse`/`ToOwnedDocumentResponse`/`ToPhotoResponse` mapping methods in `DocumentController.cs` and `LandController.cs` to set it from `d.UploadBatchId`.

- [ ] **Step 6: Build and run existing tests**

Run: `cd api && dotnet build`
Run: `dotnet test --filter "FullyQualifiedName~Document"`
Expected: builds clean, existing tests still pass (no test yet exercises `batchId` — that comes in Task 3).

- [ ] **Step 7: Commit**

```bash
git add api/src/SurveyorLedger.API/Services/DocumentService.cs api/src/SurveyorLedger.API/Models/Document/ api/src/SurveyorLedger.API/Models/Land/OwnedDocumentResponse.cs api/src/SurveyorLedger.API/Models/Land/LandPhotoResponse.cs api/src/SurveyorLedger.API/Controllers/DocumentController.cs api/src/SurveyorLedger.API/Controllers/LandController.cs
git commit -m "feat: thread client-generated BatchId through document upload endpoints"
```

---

### Task 3: Backend test — uploads sharing a `batchId` come back grouped

**Files:**
- Test: `api/tests/SurveyorLedger.API.Tests/Services/LandOwnershipTests.cs` (add to this file — it already exercises `IDocumentService.UploadOwnedDocumentAsync` for Land-owned documents; follow its existing setup pattern for `_documentService`/`_context`)

**Interfaces:**
- Consumes: `IDocumentService.UploadOwnedDocumentAsync(..., Guid? batchId = null)` from Task 2.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task UploadOwnedDocumentAsync_WithSharedBatchId_BothDocumentsCarryIt()
{
    var batchId = Guid.NewGuid();
    var file1 = CreateTestFile("a.pdf");
    var file2 = CreateTestFile("b.pdf");

    var doc1 = await _documentService.UploadOwnedDocumentAsync(_workspaceId, _adminUserId, _landId, "Land", _landId, DocumentCategory.Other, file1, batchId: batchId);
    var doc2 = await _documentService.UploadOwnedDocumentAsync(_workspaceId, _adminUserId, _landId, "Land", _landId, DocumentCategory.Other, file2, batchId: batchId);

    Assert.Equal(batchId, doc1.UploadBatchId);
    Assert.Equal(batchId, doc2.UploadBatchId);
}
```

(Match whatever helper this test file already uses to build an `IFormFile` for uploads — e.g. an existing `CreateTestFile`/`MockFormFile` helper in the same file; use it instead of inventing a new one.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "UploadOwnedDocumentAsync_WithSharedBatchId_BothDocumentsCarryIt"`
Expected: FAIL — `batchId:` named argument doesn't exist yet if Task 2 wasn't applied first; if Task 2 is already done, this should already PASS (confirming Task 2's wiring), which is also an acceptable outcome — in that case skip to Step 4.

- [ ] **Step 3: N/A — implementation is Task 2**

This test exists to lock in Task 2's behavior; no new production code here.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "UploadOwnedDocumentAsync_WithSharedBatchId_BothDocumentsCarryIt"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add api/tests/SurveyorLedger.API.Tests/Services/LandOwnershipTests.cs
git commit -m "test: batch id round-trips through UploadOwnedDocumentAsync"
```

---

### Task 4: `LandDocumentRequest.FulfilledDocumentId` → `FulfilledBatchId`, multi-file fulfill

**Files:**
- Modify: `api/src/SurveyorLedger.Data/Entities/LandDocumentRequest.cs`
- Modify: `api/src/SurveyorLedger.API/Services/LandDocumentRequestService.cs`
- Modify: `api/src/SurveyorLedger.API/Models/LandDocumentRequest/LandDocumentRequestResponse.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/LandDocumentRequestController.cs`
- Modify: `api/src/SurveyorLedger.API/Models/LandDocumentRequest/LandDocumentRequestFulfillRequest.cs` (find via `Glob api/src/SurveyorLedger.API/Models/LandDocumentRequest/*.cs` — it exists since `LandDocumentRequestController.Fulfill` already binds `[FromForm] LandDocumentRequestFulfillRequest`)
- Create: EF migration
- Test: existing Land document-request test file (locate via `Glob api/tests/**/*LandDocumentRequest*`)

**Interfaces:**
- Consumes: `IDocumentService.UploadOwnedDocumentForFulfillmentAsync(..., Guid? batchId)` from Task 2.
- Produces: `LandDocumentRequest.FulfilledBatchId` (`Guid?`), `LandDocumentRequestResponse.FulfilledBatchId` — Task 8 (frontend grouping) reads this field.

- [ ] **Step 1: Swap the entity field**

In `api/src/SurveyorLedger.Data/Entities/LandDocumentRequest.cs`, replace:

```csharp
public Guid? FulfilledDocumentId { get; set; }
```

with:

```csharp
/// <summary>Every Document with this UploadBatchId is a file this request was fulfilled with - set on first fulfillment, reused (not replaced) on every re-fulfillment after a Reopen, so old and new files accumulate in one group.</summary>
public Guid? FulfilledBatchId { get; set; }
```

Also remove the now-stale `public Document? FulfilledDocument { get; set; }` navigation property (there's no single document to navigate to anymore) and its `FulfilledDocumentId`-based FK config, if any, in `LandDocumentRequestConfiguration.cs` (check that file for a `HasOne(x => x.FulfilledDocument)` block and remove it).

- [ ] **Step 2: `FulfillAsync` accepts multiple files and a client `batchId`**

In `api/src/SurveyorLedger.API/Services/LandDocumentRequestService.cs`, change the interface and implementation:

```csharp
Task<LandDocumentRequest> FulfillAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid requestId, List<IFormFile> files, Guid batchId, string? displayFileName = null);
```

Rewrite `LinkFulfilledDocumentAsync` to loop over `files`, uploading each via `UploadOwnedDocumentForFulfillmentAsync(..., batchId: batchId)`, and set `request.FulfilledBatchId = batchId` once (not per file):

```csharp
private async Task<LandDocumentRequest> LinkFulfilledDocumentAsync(Guid workspaceId, Guid landId, LandDocumentRequest request, List<IFormFile> files, Guid attributedUserAccountId, Guid batchId, string? displayFileName)
{
    var attributedPersonId = await _access.ResolvePersonIdAsync(attributedUserAccountId);

    foreach (var file in files)
    {
        await _documentService.UploadOwnedDocumentForFulfillmentAsync(workspaceId, attributedUserAccountId, landId, request.OwnerType, request.OwnerId, request.Category, file, displayFileName, batchId);
    }

    request.FulfilledBatchId = batchId;
    request.FulfilledAt = DateTime.UtcNow;
    request.FulfilledBy = attributedPersonId;
    request.Status = "Fulfilled";
    request.UpdatedAt = DateTime.UtcNow;

    await _context.SaveChangesAsync();
    return request;
}
```

Note what's dropped versus the old version: the "supersede the previous single document" soft-delete branch. With batching, re-fulfilling after Reopen *adds* to the same `FulfilledBatchId` rather than replacing — matches the approved spec ("old files stay, new files join the same group"). `FulfillAsync` and `UploadViaShareTokenAsync`/`LinkFulfilledDocumentAsyncForToken` both call this — update both call sites to pass `files`/`batchId` (for the anonymous-token path, the caller generates a fresh `Guid.NewGuid()` batchId if `request.FulfilledBatchId` is null, else reuses `request.FulfilledBatchId.Value` — same reuse-if-present rule described in Step 3).

Update `FulfillAsync`'s own signature/body to accept `List<IFormFile> files, Guid batchId` instead of `IFormFile file`, and pass `batchId` through unchanged (the *caller*, i.e. the frontend via the controller, decides whether to reuse `request.fulfilledBatchId` or mint a fresh one — see Task 8).

- [ ] **Step 3: DTO/controller changes**

In `LandDocumentRequestFulfillRequest.cs`, change `IFormFile File` to `List<IFormFile> Files` and add `Guid BatchId`. In `LandDocumentRequestController.Fulfill`, update the call to `_requestService.FulfillAsync(workspaceId, CallerId(), landId, id, request.Files, request.BatchId, request.DisplayFileName)`.

In `LandDocumentRequestResponse.cs`, replace `public Guid? FulfilledDocumentId { get; set; }` with `public Guid? FulfilledBatchId { get; set; }`. In `LandDocumentRequestController.ToResponse`, replace the `FulfilledDocumentId = ...` line with `FulfilledBatchId = r.FulfilledBatchId,`.

- [ ] **Step 4: Migration**

Run: `sqlcmd -S "(localdb)\MSSQLLocalDB" -d SurveyorLedger -Q "SELECT COUNT(*) FROM LandDocumentRequests WHERE FulfilledDocumentId IS NOT NULL"` first — if the count is 0, skip straight to generating the schema-only migration below. If non-zero, run this data-preservation step before generating the migration (mirrors the earlier `LandPhoto`→`Document` migration pattern):

```sql
-- one-time backfill: give every already-fulfilled request its own fresh batch id,
-- and stamp that id onto the one Document it already points to
UPDATE r SET r.FulfilledDocumentId = NEWID()  -- temp reuse of the column as a batch id holder, see next step
FROM LandDocumentRequests r WHERE r.FulfilledDocumentId IS NOT NULL;
```

(If the row count from the count query above is 0, skip this SQL entirely — there is nothing to preserve, matching how the earlier `OwnerType`/`OwnerId` addition to this same table was handled with a zero-row check.)

Then generate the migration:

Run: `cd api && dotnet ef migrations add RenameLandDocumentRequestFulfilledDocumentIdToBatchId --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`

Inspect the generated migration: it should drop `FulfilledDocumentId` and add `FulfilledBatchId` (both nullable `uniqueidentifier`). If row count was non-zero, the migration's `Up()` needs the actual data copy — since hand-editing generated migrations is blocked by this repo's PreToolUse hook, do the copy via `sqlcmd` immediately after `dotnet ef database update` runs the schema change, using the temp values written into the old column name captured beforehand (in practice: run the `SELECT ... FulfilledDocumentId` backup query, apply the migration, then `UPDATE LandDocumentRequests SET FulfilledBatchId = <captured value>` per row via `sqlcmd`). Given this repo's current dev data, the zero-row path is expected — confirm with the `COUNT(*)` query before assuming the manual copy is needed.

Run: `dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: clean (fix any remaining `FulfilledDocumentId` references the compiler flags — check `LandDocumentRequestConfiguration.cs` for a stale FK config).

- [ ] **Step 6: Update/write tests**

Locate the existing Land document-request test file (`Glob api/tests/**/*LandDocumentRequest*`). Update any test currently calling `FulfillAsync(..., file, ...)` to the new `FulfillAsync(..., new List<IFormFile> { file }, Guid.NewGuid(), ...)` shape, and any assertion on `request.FulfilledDocumentId` to `request.FulfilledBatchId`. Add one new test:

```csharp
[Fact]
public async Task FulfillAsync_WithMultipleFiles_AllShareTheBatchId()
{
    var batchId = Guid.NewGuid();
    var files = new List<IFormFile> { CreateTestFile("a.pdf"), CreateTestFile("b.pdf") };

    var fulfilled = await _requestService.FulfillAsync(_workspaceId, _adminUserId, _landId, _requestId, files, batchId);

    Assert.Equal(batchId, fulfilled.FulfilledBatchId);
    Assert.Equal("Fulfilled", fulfilled.Status);
    var docs = await _context.Documents.Where(d => d.UploadBatchId == batchId).ToListAsync();
    Assert.Equal(2, docs.Count);
}

[Fact]
public async Task FulfillAsync_AfterReopen_ReusesExistingBatchId()
{
    var firstBatch = Guid.NewGuid();
    await _requestService.FulfillAsync(_workspaceId, _adminUserId, _landId, _requestId, new List<IFormFile> { CreateTestFile("a.pdf") }, firstBatch);
    await _requestService.ReopenAsync(_workspaceId, _adminUserId, _landId, _requestId);

    var refulfilled = await _requestService.FulfillAsync(_workspaceId, _adminUserId, _landId, _requestId, new List<IFormFile> { CreateTestFile("b.pdf") }, firstBatch);

    Assert.Equal(firstBatch, refulfilled.FulfilledBatchId);
    var docs = await _context.Documents.Where(d => d.UploadBatchId == firstBatch).ToListAsync();
    Assert.Equal(2, docs.Count); // both a.pdf and b.pdf, matching "keep old files, group goes back to pending"
}
```

Run: `dotnet test --filter "FullyQualifiedName~LandDocumentRequest"`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add api/src/SurveyorLedger.Data/Entities/LandDocumentRequest.cs api/src/SurveyorLedger.Data/Configurations/ api/src/SurveyorLedger.API/Services/LandDocumentRequestService.cs api/src/SurveyorLedger.API/Models/LandDocumentRequest/ api/src/SurveyorLedger.API/Controllers/LandDocumentRequestController.cs api/src/SurveyorLedger.Data/Migrations/ api/tests/
git commit -m "feat: LandDocumentRequest fulfilled by a batch of files instead of one"
```

---

### Task 5: Same change for Job's `DocumentRequest`

**Files:**
- Modify: `api/src/SurveyorLedger.Data/Entities/DocumentRequest.cs`
- Modify: `api/src/SurveyorLedger.API/Services/DocumentRequestService.cs`
- Modify: `api/src/SurveyorLedger.API/Models/DocumentRequest/DocumentRequestResponse.cs` (locate via `Glob api/src/SurveyorLedger.API/Models/DocumentRequest/*.cs`)
- Modify: `api/src/SurveyorLedger.API/Controllers/DocumentRequestController.cs` (locate via `Glob`)
- Create: EF migration
- Test: existing Job document-request test file

**Interfaces:**
- Consumes: `IDocumentService.UploadAsync(..., Guid? batchId)` from Task 2.
- Produces: `DocumentRequest.FulfilledBatchId`, `DocumentRequestResponse.FulfilledBatchId`.

- [ ] **Step 1: Mirror Task 4's entity/service change for Job**

Same shape as Task 4 Steps 1-3, applied to `DocumentRequest`/`DocumentRequestService`/`DocumentRequestResponse`/`DocumentRequestController`: `FulfilledDocumentId` → `FulfilledBatchId`, `FulfillAsync` takes `List<IFormFile> files, Guid batchId, DocumentVisibility visibility, string? displayFileName = null` (visibility is Job-specific, keep it), `LinkFulfilledDocumentAsync` loops files calling `_documentService.UploadAsync(..., batchId: batchId)` per file, sets `request.FulfilledBatchId = batchId` once, no more soft-delete-the-previous-document branch.

- [ ] **Step 2: Migration**

Run: `sqlcmd -S "(localdb)\MSSQLLocalDB" -d SurveyorLedger -Q "SELECT COUNT(*) FROM DocumentRequests WHERE FulfilledDocumentId IS NOT NULL"` — same zero-row-check-first approach as Task 4 Step 4.

Run: `cd api && dotnet ef migrations add RenameDocumentRequestFulfilledDocumentIdToBatchId --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`
Run: `dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: clean.

- [ ] **Step 4: Update/write tests**

Mirror Task 4 Step 6's two new tests (`FulfillAsync_WithMultipleFiles_AllShareTheBatchId`, `FulfillAsync_AfterReopen_ReusesExistingBatchId`) against `DocumentRequestService` in the existing Job document-request test file, adjusting for the extra `visibility` parameter `FulfillAsync` takes here.

Run: `dotnet test --filter "FullyQualifiedName~DocumentRequest"`
Expected: PASS

- [ ] **Step 5: Full backend suite**

Run: `dotnet test`
Expected: all pass — this is the last backend change, confirms nothing elsewhere broke.

- [ ] **Step 6: Commit**

```bash
git add api/src/SurveyorLedger.Data/Entities/DocumentRequest.cs api/src/SurveyorLedger.API/Services/DocumentRequestService.cs api/src/SurveyorLedger.API/Models/DocumentRequest/ api/src/SurveyorLedger.API/Controllers/DocumentRequestController.cs api/src/SurveyorLedger.Data/Migrations/ api/tests/
git commit -m "feat: DocumentRequest fulfilled by a batch of files instead of one"
```

---

### Task 6: Frontend services expose `batchId`/multi-file fulfill

**Files:**
- Modify: `ui/src/app/core/document.service.ts`
- Modify: `ui/src/app/core/land.service.ts`
- Modify: `ui/src/app/core/land-document-request.service.ts`
- Modify: `ui/src/app/core/document-request.service.ts`

**Interfaces:**
- Produces: `DocumentService.upload(..., batchId?: string)`, `LandService.uploadDocument/uploadSurveyDocument/uploadDeedDocument/uploadPhoto(..., batchId?: string)`, `LandDocumentRequestService.fulfill(workspaceId, landId, requestId, files: File[], batchId: string, displayFileName?)`, `DocumentRequestService.fulfill(workspaceId, jobId, requestId, files: File[], batchId: string, visibility, displayFileName?)`. `Document`/`OwnedDocument`/`LandPhoto` interfaces gain `uploadBatchId: string | null`. `LandDocumentRequest`/`DocumentRequest` interfaces: `fulfilledDocumentId` → `fulfilledBatchId: string | null`.

- [ ] **Step 1: `document.service.ts`**

Add `uploadBatchId: string | null;` to the `Document` interface. Change `upload`:

```typescript
upload(workspaceId: string, jobId: string, file: File, category: string, visibility: string, displayFileName?: string, batchId?: string): Observable<Document> {
  const form = new FormData();
  form.append('File', file);
  form.append('Category', category);
  form.append('Visibility', visibility);
  if (displayFileName) form.append('DisplayFileName', displayFileName);
  if (batchId) form.append('BatchId', batchId);
  return this.http.post<ApiResponse<Document>>(this.base(workspaceId, jobId), form).pipe(map(res => res.data));
}
```

- [ ] **Step 2: `land.service.ts`**

Add `uploadBatchId: string | null;` to `OwnedDocument`, and `batchId: string | null;` to `LandPhoto`. Change `uploadDocument`, `uploadSurveyDocument`, `uploadDeedDocument`, `uploadPhoto` to accept an optional trailing `batchId?: string` and append it as a query param (matches Task 2 Step 4's `[FromQuery] Guid? batchId`):

```typescript
uploadDocument(workspaceId: string, landId: string, file: File, category: string = 'Other', batchId?: string): Observable<OwnedDocument> {
  const form = new FormData();
  form.append('file', file);
  const params: Record<string, string> = { category };
  if (batchId) params['batchId'] = batchId;
  return this.http
    .post<ApiResponse<OwnedDocument>>(`${this.base(workspaceId)}/${landId}/documents`, form, { params })
    .pipe(map(res => res.data));
}
```

Apply the same `params`-with-optional-`batchId` pattern to `uploadSurveyDocument`, `uploadDeedDocument`, `uploadPhoto` (the latter two currently take no query params at all, so add `{ params: batchId ? { batchId } : {} }` to their `http.post` calls).

- [ ] **Step 3: `land-document-request.service.ts`**

Change `LandDocumentRequest.fulfilledDocumentId` to `fulfilledBatchId: string | null;`. Change `fulfill`:

```typescript
fulfill(workspaceId: string, landId: string, requestId: string, files: File[], batchId: string, displayFileName?: string): Observable<LandDocumentRequest> {
  const form = new FormData();
  files.forEach(file => form.append('Files', file));
  form.append('BatchId', batchId);
  if (displayFileName) form.append('DisplayFileName', displayFileName);
  return this.http
    .post<ApiResponse<LandDocumentRequest>>(`${this.base(workspaceId, landId)}/${requestId}/fulfill`, form)
    .pipe(map(res => res.data));
}
```

- [ ] **Step 4: `document-request.service.ts`**

Same shape as Step 3 for `DocumentRequest.fulfilledDocumentId` → `fulfilledBatchId`, and `fulfill(workspaceId, jobId, requestId, files: File[], batchId: string, visibility: string, displayFileName?: string)` appending `Files`/`BatchId`/`Visibility`/`DisplayFileName` to the form.

- [ ] **Step 5: Build**

Run: `cd ui && npm run build`
Expected: TypeScript errors in `land-detail-panel.component.ts`/`job-detail.component.ts` at every old `fulfilledDocumentId`/single-file `fulfill(...)` call site — expected, fixed in Tasks 8/10.

- [ ] **Step 6: Commit**

```bash
git add ui/src/app/core/document.service.ts ui/src/app/core/land.service.ts ui/src/app/core/land-document-request.service.ts ui/src/app/core/document-request.service.ts
git commit -m "feat: frontend services accept BatchId, fulfill takes multiple files"
```

---

### Task 7: `DocRow` grouping + `DocumentListComponent` renders batches

**Files:**
- Modify: `ui/src/app/shared/document-list/document-list.component.ts`
- Test: `ui/src/app/shared/document-list/document-list.component.spec.ts` (create if it doesn't exist — check with `Glob ui/src/app/shared/document-list/*.spec.ts` first)

**Interfaces:**
- Consumes: nothing new from earlier tasks — this is a pure UI change to the existing `DocRow`/`DocumentListComponent` shapes.
- Produces: `DocRow.batchId?: string | null`, `DocumentListComponent.removeGroup: EventEmitter<DocRow[]>` — Task 8/10 wire these into `land-detail-panel.component.ts`/`job-detail.component.ts`.

- [ ] **Step 1: Add `batchId` to `DocRow`**

In `ui/src/app/shared/document-list/document-list.component.ts`, add to the `DocRow` interface:

```typescript
/** Rows sharing the same non-null batchId render as one collapsible group instead of separate rows - set from Document.uploadBatchId or a request's fulfilledBatchId. */
batchId?: string | null;
```

- [ ] **Step 2: Write the failing test — single-row batch renders unchanged, multi-row batch groups**

Create `ui/src/app/shared/document-list/document-list.component.spec.ts`:

```typescript
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DocumentListComponent, DocRow } from './document-list.component';

describe('DocumentListComponent', () => {
  let fixture: ComponentFixture<DocumentListComponent>;
  let component: DocumentListComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [DocumentListComponent] }).compileComponents();
    fixture = TestBed.createComponent(DocumentListComponent);
    component = fixture.componentInstance;
  });

  const row = (key: string, batchId: string | null): DocRow => ({
    key, ownerKind: 'land', ownerId: 'land-1', documentId: key, fileName: `${key}.pdf`,
    contentType: 'application/pdf', uploadedByName: 'A', createdAt: '2026-01-01', batchId
  });

  it('renders a batch of one as a plain row, no group chrome', () => {
    component.rows = [row('a', 'batch-1')];
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).not.toContain('files');
    expect(el.querySelectorAll('[data-testid="group-header"]').length).toBe(0);
  });

  it('groups 2+ rows sharing a batchId under one collapsible header', () => {
    component.rows = [row('a', 'batch-1'), row('b', 'batch-1'), row('c', null)];
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelectorAll('[data-testid="group-header"]').length).toBe(1);
    expect(el.textContent).toContain('2 files');
  });

  it('emits removeGroup with every member row', () => {
    component.rows = [row('a', 'batch-1'), row('b', 'batch-1')];
    fixture.detectChanges();
    let emitted: DocRow[] | undefined;
    component.removeGroup.subscribe((rows: DocRow[]) => (emitted = rows));
    component.confirmRemoveGroup('batch-1');
    expect(emitted?.map(r => r.key).sort()).toEqual(['a', 'b']);
  });
});
```

- [ ] **Step 3: Run test to verify it fails**

Run: `cd ui && npx ng test --include **/document-list.component.spec.ts`
Expected: FAIL — `removeGroup`/`confirmRemoveGroup`/group markup don't exist yet.

- [ ] **Step 4: Implement grouping**

Restructure `DocumentListComponent`'s template to compute groups and render per-group. Add to the class:

```typescript
@Output() removeGroup = new EventEmitter<DocRow[]>();
confirmingRemoveGroupId = signal<string | null>(null);
expandedGroupId = signal<string | null>(null);

get groups(): { batchId: string | null; rows: DocRow[] }[] {
  const order: (string | null)[] = [];
  const byBatch = new Map<string | null, DocRow[]>();
  for (const row of this.rows) {
    const key = row.batchId ?? row.key; // ungrouped rows are their own singleton group, keyed uniquely so they never merge with each other
    if (!byBatch.has(key)) { byBatch.set(key, []); order.push(key); }
    byBatch.get(key)!.push(row);
  }
  return order.map(key => ({ batchId: byBatch.get(key)!.length > 1 ? (key as string) : null, rows: byBatch.get(key)! }));
}

toggleGroup(batchId: string): void {
  this.expandedGroupId.update(current => (current === batchId ? null : batchId));
}

confirmRemoveGroup(batchId: string): void {
  const members = this.rows.filter(r => r.batchId === batchId);
  this.removeGroup.emit(members);
  this.confirmingRemoveGroupId.set(null);
}
```

Extract the existing single-row `<div class="px-md py-sm ...">...</div>` block (lines 43-119 of the current file) into an `<ng-template #rowTpl let-row>`. Replace the top-level `@for (row of rows; track row.key)` with:

```html
<div class="space-y-xs">
  @for (group of groups; track group.batchId ?? group.rows[0].key) {
    @if (group.batchId) {
      <div class="rounded bg-neutral-50 text-sm" data-testid="group-header">
        <div class="flex items-center gap-sm px-md py-sm cursor-pointer" (click)="toggleGroup(group.batchId)">
          <div class="w-14 h-14 rounded-md bg-neutral-200 flex items-center justify-center flex-shrink-0 text-neutral-500 text-xs font-medium">
            {{ group.rows.length }} files
          </div>
          <div class="min-w-0 flex-1">
            <span class="text-neutral-900">{{ group.rows[0].requestTitle ?? (group.rows.length + ' files') }}</span>
            @if (group.rows[0].requestStatus) {
              <span class="text-xs px-sm py-xs rounded bg-amber-100 text-amber-700 ml-xs">{{ group.rows[0].requestStatus }}</span>
            }
            <span class="text-neutral-500 block text-xs">{{ group.rows[0].uploadedByName }} · {{ group.rows[0].createdAt | date: 'mediumDate' }}</span>
          </div>
          <div class="flex items-center gap-xs flex-shrink-0" (click)="$event.stopPropagation()">
            @if (group.rows[0].requestId) {
              <!-- A request-derived group is reopened, not deleted - matches the existing single-row rule (row.requestId shows Reopen instead of Delete), just applied to the whole group instead of one doc. -->
              <button type="button" class="icon-btn" title="Reopen request" (click)="requestReopen.emit(group.rows[0])"><app-icon name="reopen" /></button>
            } @else if (confirmingRemoveGroupId() === group.batchId) {
              <span class="text-xs text-neutral-600 whitespace-nowrap">
                Remove all?
                <button type="button" class="text-primary-500 font-medium ml-xs" (click)="confirmRemoveGroup(group.batchId)">Yes</button>
                <button type="button" class="text-neutral-500 ml-xs" (click)="confirmingRemoveGroupId.set(null)">No</button>
              </span>
            } @else {
              <button type="button" class="icon-btn text-primary-500" title="Remove all" (click)="confirmingRemoveGroupId.set(group.batchId)"><app-icon name="delete" /></button>
            }
            <app-icon [name]="expandedGroupId() === group.batchId ? 'chevronUp' : 'chevronDown'" />
          </div>
        </div>
        @if (expandedGroupId() === group.batchId) {
          <div class="pl-md pb-sm space-y-xs">
            @for (row of group.rows; track row.key) {
              <ng-container *ngTemplateOutlet="rowTpl; context: { $implicit: row }"></ng-container>
            }
          </div>
        }
      </div>
    } @else {
      <ng-container *ngTemplateOutlet="rowTpl; context: { $implicit: group.rows[0] }"></ng-container>
    }
  }
</div>
<ng-template #rowTpl let-row>
  <!-- existing single-row markup (current lines 43-119), unchanged -->
</ng-template>
```

Add `NgTemplateOutlet` to the component's `imports` array (from `@angular/common`).

- [ ] **Step 5: Run test to verify it passes**

Run: `npx ng test --include **/document-list.component.spec.ts`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add ui/src/app/shared/document-list/
git commit -m "feat: DocumentListComponent groups rows sharing a batchId"
```

---

### Task 8: Land — batch ids on upload, grouped request fulfillment, `removeGroup`

**Files:**
- Modify: `ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts`

**Interfaces:**
- Consumes: `LandService.uploadDocument/uploadSurveyDocument/uploadDeedDocument/uploadPhoto(..., batchId?)`, `LandDocumentRequestService.fulfill(..., files, batchId, ...)`, `LandDocumentRequest.fulfilledBatchId`, `DocumentListComponent.removeGroup` — all from Tasks 6/7.
- Produces: updated `buildOwnerRows`/`photoRows` mapping `batchId` onto every `DocRow`; `onDocumentFilesSelected`/`onSurveyDocUpload`/`onDeedDocUpload` generate one batch id per multi-file selection; `onFulfillDocRequest` reuses `row.batchId` if present else mints one; new `onRemoveGroup(rows: DocRow[])` handler.

- [ ] **Step 1: `buildOwnerRows` matches requests to docs by batch, not by a single id**

The old singular match (`requests.find(r => r.fulfilledDocumentId === doc.documentId)`) only ever tagged one doc per request. With multi-file fulfillment every doc in the batch needs the request's title/status attached — so the group header can show it, and so a reopened request's new files land in the same visible group as its old ones. At `land-detail-panel.component.ts:553-576`, rewrite `buildOwnerRows`:

```typescript
private buildOwnerRows(docs: OwnedDocument[], ownerKind: DocRow['ownerKind'], apiOwnerType: string, ownerId: string, subId?: string): DocRow[] {
  const requests = this.documentRequests().filter(r => r.ownerType === apiOwnerType && r.ownerId === ownerId);
  const rows: DocRow[] = [];

  for (const doc of docs) {
    // A doc belongs to a request's group when its batch id matches the request's fulfilledBatchId -
    // every file uploaded via that request's fulfill action, first time or after a reopen, shares it.
    const request = doc.uploadBatchId ? requests.find(r => r.fulfilledBatchId === doc.uploadBatchId) ?? null : null;
    rows.push({
      key: doc.documentId, ownerKind, ownerId, subId, documentId: doc.documentId,
      fileName: doc.fileName, contentType: doc.contentType, uploadedByName: doc.uploadedByName, createdAt: doc.createdAt,
      batchId: doc.uploadBatchId ?? null,
      requestId: request?.requestId ?? null, requestTitle: request?.title ?? null, requestStatus: request?.status ?? null
    });
  }
  for (const request of requests) {
    // Still-pending (never fulfilled) requests have no batch yet - render as the existing bare placeholder row, unchanged.
    if (!request.fulfilledBatchId) {
      rows.push({
        key: request.requestId, ownerKind, ownerId, subId, documentId: null,
        fileName: null, contentType: null, uploadedByName: null, createdAt: null,
        requestId: request.requestId, requestTitle: request.title, requestStatus: request.status,
        requestDescription: request.description, hasActiveShareLink: request.hasActiveShareLink
      });
    }
  }
  return rows;
}
```

`photoRows` (line ~544-550) stops being a hand-rolled map and instead adapts `photos()` into the same `OwnedDocument` shape `buildOwnerRows` expects, so photo document-requests (pending, fulfilled, reopened, multi-file) get identical grouping/status behavior to every other owner kind instead of a second, incomplete code path:

```typescript
photoRows = computed<DocRow[]>(() =>
  this.buildOwnerRows(
    this.photos().map(p => ({ documentId: p.photoId, fileName: p.fileName, contentType: p.contentType, fileSizeBytes: p.fileSizeBytes, uploadedByName: p.uploadedByName, createdAt: p.createdAt, uploadBatchId: p.batchId })),
    'landPhoto', 'LandPhoto', this.landId
  )
);
```

- [ ] **Step 2: Multi-file direct uploads generate one batch id**

Change `onDocumentFilesSelected` (currently at line ~1261-1270):

```typescript
onDocumentFilesSelected(files: File[]): void {
  this.documentError.set('');
  const batchId = files.length > 1 ? crypto.randomUUID() : undefined;
  files.forEach(file => {
    const category = file.type.startsWith('image/') ? 'Photo' : 'Other';
    this.landService.uploadDocument(this.workspaceId, this.landId, file, category, batchId).subscribe({
      next: (doc) => this.documents.update(list => [doc, ...list]),
      error: (err) => this.documentError.set(err.error?.message ?? 'Could not upload document.')
    });
  });
}
```

Apply the same `const batchId = files.length > 1 ? crypto.randomUUID() : undefined;` + pass-through pattern to `onSurveyDocUpload` (line ~1145-1153, passing `batchId` as `uploadSurveyDocument`'s new trailing arg) and `onDeedDocUpload` (line ~1155-1163).

- [ ] **Step 3: Photo upload handler (Task 9 adds this method) also batches**

Covered in Task 9 — `onPhotoFilesSelected` follows the same `batchId` pattern.

- [ ] **Step 4: Request fulfillment sends all files with one batch id, reusing an existing one**

Change `onFulfillDocRequest` (currently at line ~1287-1296, currently taking `{ row: DocRow; file: File }` from `DocumentListComponent`'s single-file `requestFulfill` output):

First, in `DocumentListComponent` (Task 7's file), change `onFulfillFileSelected`'s `<input type="file">` to `<input type="file" multiple>` and its emitted payload from `{ row, file }` to `{ row, files: File[] }`; update the `requestFulfill` output type to `EventEmitter<{ row: DocRow; files: File[] }>`.

Then here:

```typescript
onFulfillDocRequest(event: { row: DocRow; files: File[] }): void {
  this.documentError.set('');
  const batchId = event.row.batchId ?? crypto.randomUUID();
  this.documentRequestService.fulfill(this.workspaceId, this.landId, event.row.requestId!, event.files, batchId).subscribe({
    next: (updated) => {
      this.documentRequests.update(list => list.map(r => (r.requestId === updated.requestId ? updated : r)));
      this.landService.getDocuments(this.workspaceId, this.landId).subscribe(docs => this.documents.set(docs));
    },
    error: (err) => this.documentError.set(err.error?.message ?? 'Could not fulfill request.')
  });
}
```

- [ ] **Step 5: `removeGroup` handler, wired into every `<app-document-list>`**

Add:

```typescript
onRemoveGroup(rows: DocRow[]): void {
  rows.forEach(row => this.onOwnedDocRemove(row));
}
```

In the template, add `(removeGroup)="onRemoveGroup($event)"` next to the existing `(remove)="onOwnedDocRemove($event)"` on every `<app-document-list>` instance (the general Documents card at line ~437-448, the Survey row at line ~270, the Deed row at line ~343, and the new Photos card from Task 9).

- [ ] **Step 6: Build**

Run: `cd ui && npm run build`
Expected: clean.

- [ ] **Step 7: Commit**

```bash
git add ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts ui/src/app/shared/document-list/document-list.component.ts
git commit -m "feat: Land documents/surveys/deeds get batch grouping and group-delete"
```

---

### Task 9: Land Photos — separate card

**Files:**
- Modify: `ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts`
- Modify: `ui/src/app/shared/document-upload-button/document-upload-button.component.ts`

**Interfaces:**
- Consumes: `photoRows` (already exists, Task 8 added `batchId`), `startOwnerRequest`/`isRequestFormTarget` (already generic over ownerKind — confirm `'landPhoto'` is already in their union types at line ~530-541; it is, from prior work), `LandService.uploadPhoto(..., batchId?)`.
- Produces: `DocumentUploadButtonComponent.accept` input; `onPhotoFilesSelected(files: File[])` handler.

- [ ] **Step 1: `DocumentUploadButtonComponent` gains `accept`**

In `ui/src/app/shared/document-upload-button/document-upload-button.component.ts`, change the hardcoded template attribute and add an input:

```typescript
@Input() accept = '.pdf,.doc,.docx,.xls,.xlsx,.jpg,.jpeg,.png';
```

```html
<input type="file" [multiple]="multiple" [accept]="accept" class="hidden" (change)="onFilesSelected($event)" />
```

- [ ] **Step 2: Stop routing photos through the general upload**

In `land-detail-panel.component.ts`, revert `onDocumentFilesSelected` (line ~1261-1270) to no longer MIME-sniff into `Category=Photo`:

```typescript
onDocumentFilesSelected(files: File[]): void {
  this.documentError.set('');
  const batchId = files.length > 1 ? crypto.randomUUID() : undefined;
  files.forEach(file => {
    this.landService.uploadDocument(this.workspaceId, this.landId, file, 'Other', batchId).subscribe({
      next: (doc) => this.documents.update(list => [doc, ...list]),
      error: (err) => this.documentError.set(err.error?.message ?? 'Could not upload document.')
    });
  });
}
```

- [ ] **Step 3: Add `onPhotoFilesSelected`**

```typescript
onPhotoFilesSelected(files: File[]): void {
  this.documentError.set('');
  const batchId = files.length > 1 ? crypto.randomUUID() : undefined;
  files.forEach(file => {
    this.landService.uploadPhoto(this.workspaceId, this.landId, file, batchId).subscribe({
      next: (photo) => this.photos.update(list => [photo, ...list]),
      error: (err) => this.documentError.set(err.error?.message ?? 'Could not upload photo.')
    });
  });
}
```

- [ ] **Step 4: `documentRows` stops including `photoRows`**

At line ~582-585, change:

```typescript
documentRows = computed<DocRow[]>(() => [
  ...this.buildOwnerRows(this.documents(), 'land', 'Land', this.landId),
  ...this.photoRows()
]);
```

to:

```typescript
documentRows = computed<DocRow[]>(() => this.buildOwnerRows(this.documents(), 'land', 'Land', this.landId));
```

- [ ] **Step 5: New Photos card in the template**

Insert a new section right after the Documents section (after line ~460, before `}` at line ~461, matching the Documents block's structure exactly):

```html
<div>
  <h3 class="text-xs font-semibold text-neutral-500 uppercase mb-sm">Photos</h3>
  <app-document-list
    [rows]="photoRows()"
    [previewUrls]="previewUrls()"
    (view)="onOwnedDocView($event)"
    (download)="onOwnedDocDownload($event)"
    (remove)="onOwnedDocRemove($event)"
    (removeGroup)="onRemoveGroup($event)"
    (rename)="onOwnedDocRename($event)"
    (requestFulfill)="onFulfillDocRequest($event)"
    (requestReopen)="reopenDocRequestRow($event)"
    (requestCancel)="cancelDocRequestRow($event)"
    (requestCopyShareLink)="copyDocRequestShareLinkRow($event)"
  />
  @if (isRequestFormTarget('landPhoto', landId)) {
    <app-document-request-form (submitted)="submitDocRequest($event)" (cancelled)="requestFormTarget.set(null)" />
  } @else {
    <div class="flex gap-md mt-sm">
      <app-document-upload-button label="+ Add photo" accept="image/*" (filesSelected)="onPhotoFilesSelected($event)" />
      <button type="button" class="text-sm text-primary-600" (click)="startOwnerRequest('landPhoto', landId)">+ Request photo</button>
    </div>
  }
</div>
```

`submitDocRequest` (line ~1272-1285) already reads `requestFormTarget()`'s `ownerType`/`ownerId` generically — no change needed there; confirm `startOwnerRequest`'s parameter type (line ~534, currently `'land' | 'landSurvey' | 'landDeed'`) includes `'landPhoto'` — extend the type union if it doesn't:

```typescript
startOwnerRequest(ownerType: 'land' | 'landSurvey' | 'landDeed' | 'landPhoto', ownerId: string): void {
```

(and the matching `isRequestFormTarget` parameter type).

- [ ] **Step 6: Build and manual check**

Run: `cd ui && npm run build`
Expected: clean.
Manual: open a Land, confirm Documents card no longer shows photos, new Photos card shows them, uploading 2+ photos at once groups them, "+ Request photo" only accepts images (browser file picker filter).

- [ ] **Step 7: Commit**

```bash
git add ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts ui/src/app/shared/document-upload-button/
git commit -m "feat: Land photos get their own card, separate from general documents"
```

---

### Task 10: Job — batch ids on upload, grouped request fulfillment

**Files:**
- Modify: `ui/src/app/pages/job/job-detail.component.ts`

**Interfaces:**
- Consumes: `DocumentService.upload(..., batchId?)`, `DocumentRequestService.fulfill(..., files, batchId, visibility, ...)`, `DocumentRequest.fulfilledBatchId`, `DocumentListComponent.removeGroup`/grouped `requestFulfill` event shape from Tasks 6/7.

- [ ] **Step 1: Locate Job's upload/fulfill handlers**

Run: `Grep -n "onJobDocFilesSelected|documentService.upload|onFulfillDocRequest|requestFulfill" ui/src/app/pages/job/job-detail.component.ts` to find the exact current method names/line numbers (this file wasn't re-read in full during planning — the prior session's summary confirms `docBlob`, `onJobDocRename`, `loadPreviews`, and a document-upload handler exist, but not their exact names post-refactor).

- [ ] **Step 2: Apply the same batch-id pattern as Task 8 Steps 2 and 4**

In whatever method currently loops `files.forEach(file => this.documentService.upload(...))`, add `const batchId = files.length > 1 ? crypto.randomUUID() : undefined;` and pass it as `upload`'s new trailing argument.

In whatever method currently handles the `requestFulfill` output (single-file `{ row, file }`), change it to the new `{ row, files: File[] }` shape (from Task 7 Step 4's `DocumentListComponent` change) and call `this.documentRequestService.fulfill(this.workspaceId, this.jobId, event.row.requestId!, event.files, event.row.batchId ?? crypto.randomUUID(), visibilityValue)` — check what `visibility` value the current single-file call passes (likely a fixed `'ClientVisible'` or similar) and keep it unchanged.

Add `batchId: doc.uploadBatchId` to whatever row-mapping function builds Job's `DocRow[]` (mirrors Task 8 Step 1 for Land), and a `removeGroup` handler mirroring Task 8 Step 5, wired to every `<app-document-list>` in this file's template.

- [ ] **Step 3: Build**

Run: `cd ui && npm run build`
Expected: clean.

- [ ] **Step 4: Manual check**

Open a Job, upload 3 files at once via the Documents card, confirm they group; fulfill a document request with 2 files, confirm one group with the request's status.

- [ ] **Step 5: Commit**

```bash
git add ui/src/app/pages/job/job-detail.component.ts
git commit -m "feat: Job documents get batch grouping and multi-file request fulfillment"
```

---

### Task 11: Sri Lanka province/district data + cross-clearing selects

**Files:**
- Create: `ui/src/app/shared/sri-lanka-locations.ts`
- Modify: `ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts`
- Modify: `ui/src/app/pages/job/add-land-widget/add-land-widget.component.ts`
- Test: `ui/src/app/shared/sri-lanka-locations.spec.ts`

**Interfaces:**
- Produces: `PROVINCES: string[]`, `DISTRICTS_BY_PROVINCE: Record<string, string[]>`, `provinceForDistrict(district: string): string | undefined`.

- [ ] **Step 1: Write the failing test**

Create `ui/src/app/shared/sri-lanka-locations.spec.ts`:

```typescript
import { PROVINCES, DISTRICTS_BY_PROVINCE, provinceForDistrict } from './sri-lanka-locations';

describe('sri-lanka-locations', () => {
  it('has 9 provinces', () => expect(PROVINCES.length).toBe(9));

  it('has 25 districts total across all provinces', () => {
    const total = Object.values(DISTRICTS_BY_PROVINCE).reduce((sum, list) => sum + list.length, 0);
    expect(total).toBe(25);
  });

  it('every province in PROVINCES has a district list', () => {
    for (const province of PROVINCES) expect(DISTRICTS_BY_PROVINCE[province]?.length).toBeGreaterThan(0);
  });

  it('provinceForDistrict finds the right province', () => {
    expect(provinceForDistrict('Colombo')).toBe('Western Province');
    expect(provinceForDistrict('Kandy')).toBe('Central Province');
  });

  it('provinceForDistrict returns undefined for an unknown district', () => {
    expect(provinceForDistrict('Nowhere')).toBeUndefined();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd ui && npx ng test --include **/sri-lanka-locations.spec.ts`
Expected: FAIL — module doesn't exist.

- [ ] **Step 3: Implement**

Create `ui/src/app/shared/sri-lanka-locations.ts`:

```typescript
/** Sri Lanka's 9 provinces and 25 districts - used by every province/district <select> pair (Land's address form, add-land-widget's quick-create). */
export const DISTRICTS_BY_PROVINCE: Record<string, string[]> = {
  'Western Province': ['Colombo', 'Gampaha', 'Kalutara'],
  'Central Province': ['Kandy', 'Matale', 'Nuwara Eliya'],
  'Southern Province': ['Galle', 'Matara', 'Hambantota'],
  'Northern Province': ['Jaffna', 'Kilinochchi', 'Mannar', 'Vavuniya', 'Mullaitivu'],
  'Eastern Province': ['Trincomalee', 'Batticaloa', 'Ampara'],
  'North Western Province': ['Kurunegala', 'Puttalam'],
  'North Central Province': ['Anuradhapura', 'Polonnaruwa'],
  'Uva Province': ['Badulla', 'Monaragala'],
  'Sabaragamuwa Province': ['Ratnapura', 'Kegalle']
};

export const PROVINCES: string[] = Object.keys(DISTRICTS_BY_PROVINCE);

const DISTRICT_TO_PROVINCE: Record<string, string> = Object.fromEntries(
  Object.entries(DISTRICTS_BY_PROVINCE).flatMap(([province, districts]) => districts.map(d => [d, province]))
);

export function provinceForDistrict(district: string): string | undefined {
  return DISTRICT_TO_PROVINCE[district];
}

/** Flattened, alphabetical - for a lone district select with no paired province field (add-land-widget). */
export const ALL_DISTRICTS: string[] = Object.values(DISTRICTS_BY_PROVINCE).flat().sort();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx ng test --include **/sri-lanka-locations.spec.ts`
Expected: PASS

- [ ] **Step 5: Land detail panel — province/district selects with cross-clearing**

In `land-detail-panel.component.ts`, import `PROVINCES, DISTRICTS_BY_PROVINCE, provinceForDistrict` from `'../../../shared/sri-lanka-locations'`. Replace the two `<input>`s at lines 86-87:

```html
<select class="input-field" [(ngModel)]="district" (ngModelChange)="onDistrictChange($event)">
  <option value="">District</option>
  @for (d of districtOptions(); track d) {
    <option [value]="d">{{ d }}</option>
  }
</select>
<select class="input-field" [(ngModel)]="province" (ngModelChange)="onProvinceChange($event)">
  <option value="">Province</option>
  @for (p of provinces; track p) {
    <option [value]="p">{{ p }}</option>
  }
</select>
```

Add to the class:

```typescript
provinces = PROVINCES;

/** Only the selected province's districts once one is chosen - otherwise every district, so picking a district first still works (and auto-fills the province via onDistrictChange). */
districtOptions = computed(() => (this.province ? DISTRICTS_BY_PROVINCE[this.province] ?? [] : Object.values(DISTRICTS_BY_PROVINCE).flat()));

onProvinceChange(newProvince: string): void {
  this.province = newProvince;
  if (this.district && !DISTRICTS_BY_PROVINCE[newProvince]?.includes(this.district)) {
    this.district = '';
  }
}

onDistrictChange(newDistrict: string): void {
  this.district = newDistrict;
  const owningProvince = provinceForDistrict(newDistrict);
  if (owningProvince && owningProvince !== this.province) {
    this.province = owningProvince;
  }
}
```

`district`/`province` are plain class fields (not signals — confirmed at lines 599-600), so `districtOptions` must be a `computed(() => ...)` reading them via a signal wrapper, OR (simpler, matching how the rest of this non-signal form works) a plain getter recalculated on every change-detection pass:

```typescript
get districtOptions(): string[] {
  return this.province ? DISTRICTS_BY_PROVINCE[this.province] ?? [] : Object.values(DISTRICTS_BY_PROVINCE).flat();
}
```

Use the getter form (`districtOptions` without `()` in the template's `@for`) — it matches this component's existing pattern of plain fields + `ngModel`, not signals, for the address form fields.

- [ ] **Step 6: `add-land-widget` — district select, no province field**

In `add-land-widget.component.ts`, import `ALL_DISTRICTS` from `'../../../shared/sri-lanka-locations'`. Replace line 55:

```html
<select class="input-field" [(ngModel)]="district">
  <option value="">District (optional)</option>
  @for (d of allDistricts; track d) {
    <option [value]="d">{{ d }}</option>
  }
</select>
```

Add `allDistricts = ALL_DISTRICTS;` to the class.

- [ ] **Step 7: Build**

Run: `cd ui && npm run build`
Expected: clean.

- [ ] **Step 8: Commit**

```bash
git add ui/src/app/shared/sri-lanka-locations.ts ui/src/app/shared/sri-lanka-locations.spec.ts ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts ui/src/app/pages/job/add-land-widget/add-land-widget.component.ts
git commit -m "feat: province/district dropdowns with cross-clearing, replacing free text"
```

---

### Task 12: Map reliability — `ResizeObserver` + `invalidateSize`

**Files:**
- Modify: `ui/src/app/shared/land-location-picker/land-location-picker.component.ts`

**Interfaces:**
- No new public interface — internal fix only.

- [ ] **Step 1: Add the observer**

In `ui/src/app/shared/land-location-picker/land-location-picker.component.ts`, add a private field:

```typescript
private resizeObserver: ResizeObserver | null = null;
```

In `ngOnInit`, after `this.renderMarkers(); this.renderPendingMarker(); this.fitToMarkers();` (line ~130-132), add:

```typescript
this.resizeObserver = new ResizeObserver(() => this.map.invalidateSize());
this.resizeObserver.observe(this.mapEl.nativeElement);
```

In `ngOnDestroy` (line ~146-148), add before `this.map?.remove();`:

```typescript
this.resizeObserver?.disconnect();
```

- [ ] **Step 2: Build**

Run: `cd ui && npm run build`
Expected: clean.

- [ ] **Step 3: Manual verification in the preview**

Use the Browser pane: open a Land detail panel inside whatever wraps the map (a tab/collapsed section, if applicable), confirm tiles render fully without a manual zoom, and confirm existing pins are visible immediately on load rather than only after interaction.

- [ ] **Step 4: Commit**

```bash
git add ui/src/app/shared/land-location-picker/land-location-picker.component.ts
git commit -m "fix: map re-renders reliably via ResizeObserver instead of needing a manual zoom"
```

---

### Task 13: Owner picker — inline "+ New owner" trigger

**Files:**
- Modify: `ui/src/app/pages/land/owner-picker/owner-picker.component.ts`

**Interfaces:**
- No new public interface — template-only change.

- [ ] **Step 1: Move the label row**

In `owner-picker.component.ts`, change the `@else` (search mode) branch's structure. Currently the label is on its own line (line 27) and the trigger button is at the bottom of the search block (lines 80-82). Restructure to:

```html
@if (selectedAccount(); as account) {
  <label class="block text-xs font-medium text-neutral-700 mb-xs">Owner</label>
  <div class="flex items-center justify-between px-md py-sm rounded bg-neutral-50">
    ...unchanged...
  </div>
} @else if (manualMode()) {
  <label class="block text-xs font-medium text-neutral-700 mb-xs">Owner</label>
  <div class="space-y-sm">
    ...unchanged...
  </div>
} @else {
  <div class="flex items-center justify-between mb-xs">
    <label class="block text-xs font-medium text-neutral-700">Owner</label>
    <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="useManual()">
      + New owner
    </button>
  </div>
  <input
    class="input-field"
    placeholder="Search people by name or email…"
    [ngModel]="query"
    (ngModelChange)="onQueryChange($event)"
    name="ownerSearch"
  />
  ...rest of search-mode block (results list, "No match." message) unchanged, MINUS the old bottom trigger button (lines 80-82), which is now removed entirely...
}
```

Remove the top-level `<label class="block text-xs font-medium text-neutral-700 mb-xs">Owner</label>` that currently wraps all three branches (line 27) — each branch now renders its own label so the search-mode one can sit next to the button.

- [ ] **Step 2: Build**

Run: `cd ui && npm run build`
Expected: clean.

- [ ] **Step 3: Manual verification**

Open a Land's owner picker in search mode, confirm "+ New owner" sits on the label row and doesn't move as search results appear/disappear.

- [ ] **Step 4: Commit**

```bash
git add ui/src/app/pages/land/owner-picker/owner-picker.component.ts
git commit -m "fix: owner picker's new-owner trigger sits next to the label, not below results"
```

---

### Task 14: Full verification pass

**Files:** none (verification only)

- [ ] **Step 1: Backend full suite**

Run: `cd api && dotnet test`
Expected: all pass.

- [ ] **Step 2: Frontend full build + relevant specs**

Run: `cd ui && npm run build`
Run: `npx ng test --include **/document*.spec.ts --include **/land.service.spec.ts --include **/sri-lanka-locations.spec.ts`
Expected: all pass.

- [ ] **Step 3: Manual click-through (Browser pane)**

Per the spec's testing section: upload 3 photos at once on the Photos card, confirm grouping; create a document request, fulfill with 2 files, confirm one group with status badge; reopen, fulfill with 1 more file, confirm all 3 in the same group; delete a whole group, confirm all members gone; upload a single file anywhere, confirm no group chrome; pick a province, confirm district clears if mismatched; pick a district from another province, confirm province follows; confirm map tiles/pins render without a manual zoom; confirm "+ New owner" sits next to the label.

- [ ] **Step 4: No commit needed** — this task is verification-only.
