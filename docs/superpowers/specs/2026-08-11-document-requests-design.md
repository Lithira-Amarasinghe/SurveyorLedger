# Document Requests — Design Spec

Date: 2026-08-11

## Purpose

Let Admin/Surveyor ask a Client to upload a specific document (survey plan, legal deed, etc) instead of chasing a physical handover. The Client sees the ask inside the Job's Documents list and uploads directly against it.

## Scope

- Requests attach to a Job, same as Documents (`docs/superpowers/specs/2026-08-11-job-documents-design.md`).
- One-directional link `DocumentRequest -> Document`; the existing `Document` entity is untouched.
- Fulfilling reuses `DocumentService.UploadAsync` — no duplicated upload/validation/storage logic.
- UI: one flat list (Documents card), not a separate subsection or tabs — a pending request is a document row with no file yet, and fulfilling it turns it into a normal row in place (decided during brainstorming: two lists or tabs would fabricate a grouping the data doesn't need at this scale).

## Data Model

### `DocumentRequest` entity (`api/src/SurveyorLedger.Data/Entities/DocumentRequest.cs`)

```csharp
public class DocumentRequest
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string Title { get; set; }
    public string? Description { get; set; }
    public DocumentCategory Category { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Fulfilled
    public Guid? FulfilledDocumentId { get; set; }
    public DateTime? FulfilledAt { get; set; }
    public Guid? FulfilledBy { get; set; }
    public Guid RequestedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Job Job { get; set; }
    public Document? FulfilledDocument { get; set; }
    public User RequestedByUser { get; set; }
    public User? FulfilledByUser { get; set; }
}
```

Reuses `DocumentCategory` (Task 1 of the Documents backend plan) rather than a new enum — a request's category is the same concept as a document's category (what kind of file is this).

### Migration

`dotnet ef migrations add AddDocumentRequestEntity` — new table, FK to `Jobs`, nullable FK to `Documents` (`FulfilledDocumentId`), FKs to `Users` for `RequestedBy`/`FulfilledBy`. Status stored as string (matches `Job.Status`/`Milestone.Status` convention — plain string, not an enum, since request status is a simple two-value lifecycle flag the same way those are).

## Service Layer

`DocumentRequestService` (`api/src/SurveyorLedger.API/Services/DocumentRequestService.cs`), same job-scoped RBAC reuse as `DocumentService`/`MilestoneService` — no new permissions:

- `CreateAsync(workspaceId, callerUserId, jobId, title, description, category)` — `EnsureJobAccessAsync(..., "edit")` (Admin/Surveyor only, same gate as Milestone create/Document delete).
- `GetForJobAsync(workspaceId, callerUserId, jobId)` — `EnsureJobAccessAsync(..., "view")` (everyone, including Client). No visibility filter needed on the request itself — a request ("we need a Legal Deed") isn't sensitive; only its fulfilled document is, and that's already governed by `DocumentService`'s existing Internal/ClientVisible rule when the document is later listed.
- `FulfillAsync(workspaceId, callerUserId, jobId, requestId, file, visibility)` — `EnsureJobAccessAsync(..., "view")`. Calls `IDocumentService.UploadAsync(...)` internally (category comes from the request, not a caller choice — keeps the uploaded document's category consistent with what was asked for), then sets `FulfilledDocumentId`, `FulfilledAt`, `FulfilledBy`, `Status = "Fulfilled"`.
- `ReopenAsync(workspaceId, callerUserId, jobId, requestId)` — `EnsureJobAccessAsync(..., "edit")`. Clears `FulfilledDocumentId`/`FulfilledAt`/`FulfilledBy`, `Status = "Pending"`. Does **not** delete the previously uploaded `Document` — it's just unlinked; staff can remove it separately through the existing Document delete endpoint if it was genuinely wrong, or leave it if it just needs a second file alongside.
- `CancelAsync(workspaceId, callerUserId, jobId, requestId)` — `EnsureJobAccessAsync(..., "edit")`. Soft delete (`IsActive = false`), matching every other entity in this codebase.

`DocumentRequestService` takes `IDocumentService` as a constructor dependency (both are scoped, both operate on the same `ApplicationDbContext` instance per-request, so `FulfillAsync`'s call into `UploadAsync` and the subsequent request-row update share one transaction boundary via `SaveChangesAsync` calls on the same context).

## API

`DocumentRequestController` (`api/src/SurveyorLedger.API/Controllers/DocumentRequestController.cs`), route shape matching `MilestoneController`/`DocumentController`:

`api/workspace/{workspaceId}/job/{jobId}/document-request`

| Method | Route | Effective access | Notes |
|---|---|---|---|
| POST | `/` | `job.edit` + assignment | Admin/Surveyor create a request |
| GET | `/` | `job.view` + assignment | everyone, including Client |
| POST | `/{id}/fulfill` | `job.view` + assignment | multipart upload, any role — Client fulfills their own, Admin/Surveyor can fulfill on a client's behalf (e.g. scanning a handed-over paper document) |
| POST | `/{id}/reopen` | `job.edit` + assignment | Admin/Surveyor only |
| DELETE | `/{id}` | `job.edit` + assignment | cancel a pending request |

## UI

Extends the existing Documents card (`ui/src/app/pages/job/job-detail.component.ts`) rather than adding a new card or route.

- `DocumentRequestService` (Angular), mirrors `DocumentService`'s shape: `list()`, `create()`, `fulfill()` (multipart), `reopen()`, `cancel()`.
- The card's list becomes a merge of `documents()` and `documentRequests()`, rendered as one `@for` over a combined, sorted view model — not two separate `@for` blocks — so a fulfilled request's row and its document's row are naturally the same row, not two.
- Row rendering: a request with `status === 'Pending'` renders the dashed-border "Requested: {title}" style with an Upload button (opens a file picker, calls `fulfill()`). A request with `status === 'Fulfilled'`, or a plain document with no originating request, renders the existing normal row (View/Download, plus Reopen when it has a `FulfilledDocumentId` on the request side, plus Remove for Admin/Surveyor).
- "+ Request document" small inline form (title, category, description) at the bottom of the card, visible only when `!isClient()` — same visibility gate already used for the Remove buttons and the visibility picker on the upload widget.

## Error Handling

Same exception types as `DocumentService`/`MilestoneService`: `ValidationException` (bad category/missing title), `NotFoundException` (request not found for job, or fulfilling an already-fulfilled request), `ForbiddenException` (role/assignment checks via `EnsureJobAccessAsync`).

## Testing

- Service tests (mirroring `DocumentServiceTests`): Admin/Surveyor can create, Client cannot; everyone can list; Client can fulfill their own assigned job's request; fulfilling sets the link and status; reopen clears the link without deleting the `Document`; cancel soft-deletes; tenant/assignment isolation (request from a different job/workspace is not found).
- No dedicated Angular component tests (matches this codebase's existing convention — no spec files for `job-detail.component.ts`'s sub-resource cards).

## Out of Scope (v1)

- Notifying the Client (email/in-app) when a request is created — purely visible-on-next-visit for v1.
- Due dates or reminders on requests.
- Bulk request creation (e.g. "request all standard documents for a job type").
