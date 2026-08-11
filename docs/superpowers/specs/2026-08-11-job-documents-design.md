# Job Documents — Design Spec

Date: 2026-08-11

## Purpose

Let Admin, Surveyor, and Client roles upload and view documents (survey plans, legal documents, photos) attached to a Job. Some documents are internal-only (Admin/Surveyor), some are visible to the Client.

## Scope

- Documents attach to a **Job** only (not Land) for v1.
- No version history — re-upload creates a new document row; old one can be deleted separately.
- Local disk storage for now, behind an abstraction so Azure Blob Storage can be swapped in later without touching service/controller code.

## Data Model

### `Document` entity (`api/src/SurveyorLedger.Data/Entities/Document.cs`)

```csharp
public class Document
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string FileName { get; set; }          // original filename shown to users
    public string StoredPath { get; set; }        // relative path on disk
    public string ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public DocumentCategory Category { get; set; }
    public DocumentVisibility Visibility { get; set; }
    public Guid UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Job Job { get; set; }
    public User UploadedByUser { get; set; }
}
```

Tenant isolation is transitive via `JobId -> Job.WorkspaceId`, same pattern as `Milestone`.

### Enums (`api/src/SurveyorLedger.Core/Enums.cs`)

```csharp
public enum DocumentCategory
{
    SurveyPlan,
    LegalDocument,
    Photo,
    Other
}

public enum DocumentVisibility
{
    Internal,       // Admin + Surveyor only
    ClientVisible   // also visible to Client
}
```

### Migration

`dotnet ef migrations add AddDocumentEntity` — new table + FK to Job, standard `IsActive`/`CreatedAt`/`UpdatedAt` columns matching other entities. Follow `migration-check` skill checklist.

## Storage Abstraction

`IFileStorageService` (`api/src/SurveyorLedger.API/Services/IFileStorageService.cs`):

```csharp
public interface IFileStorageService
{
    Task<string> SaveAsync(Stream content, string relativePath, CancellationToken ct);
    Task<Stream> OpenAsync(string relativePath, CancellationToken ct);
    Task DeleteAsync(string relativePath, CancellationToken ct);
}
```

`LocalFileStorageService`:
- Writes under `{UploadsRootPath}/{workspaceId}/{jobId}/{guid}_{sanitizedFileName}`.
- `UploadsRootPath` configurable via appsettings (`Storage:UploadsRootPath`), defaults to a local `uploads/` folder outside `wwwroot` (not served directly — must go through the download endpoint so visibility rules apply).
- Registered in `Program.cs` DI as `IFileStorageService`.

Swapping to Azure Blob later = add `AzureBlobFileStorageService` implementing the same interface, flip the DI registration. No controller/service changes.

## Upload Validation

- Extension allowlist: `.pdf .doc .docx .xls .xlsx .jpg .jpeg .png`
- Max size: 25 MB
- Reject anything else with a 400 before touching storage.

## Service Layer

`DocumentService` (`api/src/SurveyorLedger.API/Services/DocumentService.cs`), following the exact shape of `MilestoneService` — documents are a job sub-resource, so they reuse Job's authorization rather than getting their own:

- `UploadAsync(workspaceId, callerUserId, jobId, file, category, visibility)` — `FindJobAsync` (tenant check) + `EnsureJobAccessAsync(..., "view")`, validates file, saves via `IFileStorageService`, inserts `Document` row.
- `GetDocumentsAsync(workspaceId, callerUserId, jobId)` — same job-access check, lists active documents filtered through the shared visibility rule below.
- `GetFileAsync(workspaceId, callerUserId, jobId, documentId)` — same job-access check, resolves one document through the same visibility rule, returns stream + filename + content-type. Used for both preview and download; caller sets `Content-Disposition` (`inline` vs `attachment`) based on a query flag — one endpoint, no duplicate download route.
- `DeleteAsync(workspaceId, callerUserId, jobId, documentId)` — `EnsureJobAccessAsync(..., "edit")`, soft delete (`IsActive = false`); file stays on disk (cleanup job is future work, out of scope for v1).

**Job access, not new permissions.** No `Document.*` permissions, no seeding migration. Upload/list/download check `job.view` (Client has this — that's how they see the job at all). Delete checks `job.edit` (Admin/Surveyor only — Client never holds `job.edit`, so delete is blocked with zero new permission rows). This mirrors Milestone's documented reasoning: a new permission set for one sub-resource with two effective actions isn't justified. `EnsureJobAccessAsync` also layers the job-assignment scoping rule Milestone uses (unless caller has `job.view_all`, they must hold a job-scoped `UserAccess` row for this job) — copied verbatim, not reinvented.

**Shared visibility rule** — one private helper, `bool IsVisible(Document doc, string role) => role != "Client" || doc.Visibility == DocumentVisibility.ClientVisible`, called from both `GetDocumentsAsync` (as a filter) and `GetFileAsync` (as a guard before returning the stream). This is the only check specific to documents — everything else is inherited Job authorization.

## API

`DocumentController` (`api/src/SurveyorLedger.API/Controllers/DocumentController.cs`), same route shape as `MilestoneController`:

`api/workspace/{workspaceId}/job/{jobId}/document`

| Method | Route | Effective access | Notes |
|---|---|---|---|
| POST | `/` | `job.view` + assignment | multipart/form-data upload |
| GET | `/` | `job.view` + assignment | list; Client sees visibility-filtered set |
| GET | `/{id}?download=true` | `job.view` + assignment | stream file; inline by default, `attachment` when `download=true` |
| DELETE | `/{id}` | `job.edit` + assignment | Admin/Surveyor only — Client can upload, not delete |

## RBAC

None new. Reuses the existing `job.view`/`job.edit` Casbin permissions already seeded for Admin/Surveyor/Client — see Service Layer above.

## Error Handling

Reuses the same exception types as `MilestoneService`, caught by `ErrorHandlingMiddleware`:

- Invalid file type/size → `ValidationException` (400).
- Job not found in workspace, or document not found for job → `NotFoundException` (404) — via `FindJobAsync`/a new `FindDocumentAsync` helper matching `FindMilestoneAsync`'s shape.
- Caller lacks `job.view`/`job.edit` or isn't assigned to the job → `ForbiddenException` (403), via `EnsureJobAccessAsync`.
- Client requesting an `Internal` document directly by ID → `NotFoundException` (404, not 403 — don't reveal existence).

## Testing

- Service tests: upload validation (bad extension, oversized file), visibility filtering (Client never gets Internal docs back), tenant isolation (job in another workspace = not found), job-assignment scoping (caller without `job.view_all` and no job-scoped `UserAccess` row is forbidden).
- Controller tests: role-based access per endpoint (Client blocked from DELETE via missing `job.edit`).

## Out of Scope (v1)

- Version history / rollback.
- Land-level documents.
- File cleanup job for soft-deleted documents.
- Azure Blob Storage implementation (interface only, local impl for now).
