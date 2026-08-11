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

`DocumentService` (`api/src/SurveyorLedger.API/Services/DocumentService.cs`), following Controllers → Services → Data:

- `UploadAsync(jobId, workspaceId, file, category, visibility, userId)` — resolves Job within workspace (tenant check), validates file, saves via `IFileStorageService`, inserts `Document` row.
- `GetForJobAsync(jobId, workspaceId, requestingUserRole)` — lists active documents for the job; filters out `Internal` documents when caller's role is Client. Enforced server-side, not just hidden in UI.
- `GetFileAsync(documentId, workspaceId, requestingUserRole)` — same visibility check, returns stream + filename + content-type. Used for both preview and download; caller sets `Content-Disposition` (`inline` vs `attachment`) based on a query flag.
- `DeleteAsync(documentId, workspaceId, userId)` — soft delete (`IsActive = false`); file stays on disk (cleanup job is future work, out of scope for v1).

## API

`DocumentsController`, routes under `/api/jobs/{jobId}/documents`:

| Method | Route | Roles | Notes |
|---|---|---|---|
| POST | `/` | Admin, Surveyor, Client | multipart/form-data upload |
| GET | `/` | Admin, Surveyor, Client | list; Client sees filtered set |
| GET | `/{id}?download=true` | Admin, Surveyor, Client | stream file; inline preview by default, `attachment` header when `download=true` |
| DELETE | `/{id}` | Admin, Surveyor | Client can upload, not delete |

## RBAC

Extend the Permission matrix following the `AddJobLandSoftDeleteAndClientPermissions` migration pattern:
- New permissions: `Document.View`, `Document.Upload`, `Document.Delete`.
- Seed role-permission rows: Admin (all three), Surveyor (all three), Client (`View`, `Upload` only).
- Casbin enforces the permission check; the `Internal`/`ClientVisible` filter in `DocumentService` is a separate, additional check — a Client with `Document.View` still never sees `Internal` docs.

## Error Handling

- Invalid file type/size → 400 via existing `AppException` pattern, caught by `ErrorHandlingMiddleware`.
- Job not found in workspace → 404 (existing tenant-check pattern).
- Client requesting an `Internal` document directly by ID → 404 (not 403 — don't reveal existence).

## Testing

- Service tests: upload validation (bad extension, oversized file), visibility filtering (Client never gets Internal docs back), tenant isolation (job in another workspace = not found).
- Controller tests: role-based access per endpoint (Client blocked from DELETE).

## Out of Scope (v1)

- Version history / rollback.
- Land-level documents.
- File cleanup job for soft-deleted documents.
- Azure Blob Storage implementation (interface only, local impl for now).
