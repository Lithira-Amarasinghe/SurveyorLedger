# Shareable Upload Link for Document Requests — Design Spec

Date: 2026-08-11

## Purpose

Let Admin/Surveyor generate a link for a document request that can be shared outside the app (messaging app, email) so the recipient can upload the requested file without an account or login.

## Scope

- One link per `DocumentRequest` — shares "upload this specific ask," not general job access.
- Reuses `Invitation`'s existing unauthenticated-token pattern (`api/src/SurveyorLedger.Data/Entities/Invitation.cs`, `InvitationController`'s token routes) rather than inventing a new one.
- No new entity/table — two nullable columns on `DocumentRequest`.
- No document versioning change — the existing "replace deletes the previous file" rule (from the reopen/note work) applies identically whether the replacement comes from an authenticated fulfill or an anonymous link upload.

## Data Model

### `DocumentRequest` additions

```csharp
public string? ShareToken { get; set; }
public DateTime? ShareTokenExpiresAt { get; set; }
```

### EF configuration

```csharp
builder.Property(x => x.ShareToken).HasMaxLength(64);
builder.HasIndex(x => x.ShareToken).IsUnique().HasFilter("[ShareToken] IS NOT NULL");
```

Filtered unique index (only enforced when non-null) — matches SQL Server's standard pattern for a nullable-but-unique-when-present column, avoids every `NULL` row colliding on uniqueness.

### Migration

`dotnet ef migrations add AddDocumentRequestShareLink` — two columns + one filtered unique index. No FK, no other schema impact.

## Backend

### `DocumentRequestService` additions

- `GenerateShareLinkAsync(workspaceId, callerUserId, jobId, requestId)` — `EnsureJobAccessAsync(..., "edit")` (Admin/Surveyor only, same gate as create/reopen/cancel/edit-target). Sets `ShareToken = Guid.NewGuid("N")`, `ShareTokenExpiresAt = DateTime.UtcNow.AddDays(7)`. Calling it again on the same request overwrites the token — the old link stops resolving immediately, so regenerate doubles as "revoke and reissue in one step."
- `RevokeShareLinkAsync(workspaceId, callerUserId, jobId, requestId)` — same `job.edit` gate. Sets `ShareToken = null`, `ShareTokenExpiresAt = null`, no replacement issued. For the case where staff wants the compromised link dead *now* and will decide separately whether to issue a new one — collapsing that into "just regenerate" would force an immediate decision on the replacement at the exact moment someone's reacting to a leak.
- `GetByShareTokenAsync(token)` — no job/workspace scoping (caller has neither, by definition of being unauthenticated). Looks up by `ShareToken`, validates `IsActive`, not expired. Returns the request for the controller to project into a minimal preview (title, description/note, category, job/workspace name) — deliberately not the full entity shape, so nothing beyond what's needed to identify the ask leaks to an anonymous caller.
- `UploadViaShareTokenAsync(token, IFormFile file, string? displayFileName)` — looks up by token (same validation as above), rejects if `Status == "Fulfilled"` (`ValidationException`, clear "already fulfilled" message — the link stays usable while `Pending`/`Reopened`, per the earlier reusable-until-fulfilled decision). Calls the shared fulfillment core (see below) with `attributedUserId = request.RequestedBy` and `visibility = DocumentVisibility.ClientVisible` (hardcoded, not caller-supplied).

### Shared fulfillment core (refactor)

`FulfillAsync`'s body (upload → soft-delete previous document if replacing → link `FulfilledDocumentId`/`FulfilledAt`/`FulfilledBy` → set `Status = "Fulfilled"`) is extracted into a private `LinkFulfilledDocumentAsync(workspaceId, jobId, request, IFormFile file, DocumentVisibility visibility, Guid attributedUserId, string? displayFileName)`. Both `FulfillAsync` (authenticated, `attributedUserId = callerUserId`) and `UploadViaShareTokenAsync` (`attributedUserId = request.RequestedBy`) call it — one implementation of the no-versioning invariant, not two.

### API surface

New `DocumentRequestLinkController` (separate from `DocumentRequestController` since these routes are unauthenticated and workspace/job-less — keeping them in their own controller makes the trust boundary visible at a glance rather than mixed into a controller whose other actions all require `[Authorize]`):

```
POST   /api/workspace/{workspaceId}/job/{jobId}/document-request/{id}/share-link   [Authorize], job.edit — generate/regenerate
DELETE /api/workspace/{workspaceId}/job/{jobId}/document-request/{id}/share-link   [Authorize], job.edit — revoke
GET    /api/document-request-links/{token}                                         public
POST   /api/document-request-links/{token}/upload                                  public
```

The share-link generate/revoke actions stay on the existing authenticated `DocumentRequestController` (they need workspace/job scoping and the existing auth gate); only the two public routes live in the new controller.

`[EnableRateLimiting("auth")]` on `DocumentRequestLinkController` — reuses the existing per-IP policy from `Program.cs` rather than defining a new one; this is exactly the kind of unauthenticated surface that policy exists to protect.

File validation (extension allowlist, 25MB cap) is unchanged — inherited from `DocumentService.UploadAsync`, which the shared fulfillment core still calls into. No duplicate validation logic.

### Response shapes

`DocumentRequestLinkPreviewResponse`: `Title, Description, Category, WorkspaceName, JobTitle, Expired (bool), AlreadyFulfilled (bool)`. No IDs, no target info, no other job data.

The raw `ShareToken` is **not** added to the normal authenticated `DocumentRequestResponse` — it's returned only from the generate endpoint's response, so the actual token doesn't casually appear in every list/get call an Admin's browser makes. But the UI still needs to know, across page reloads, whether an active link currently exists (to show "Copy link" vs "Revoke link") — so `DocumentRequestResponse` does gain one boolean, `HasActiveShareLink` (`ShareToken != null && ShareTokenExpiresAt > now`, computed server-side). This leaks no secret, only existence — the Admin/Surveyor viewing their own request already implicitly has that context.

## UI

- **Generating**: "Copy link" button on pending/reopened request rows (Admin/Surveyor only, next to the existing Edit target/Cancel buttons). Calls the generate endpoint, builds `${location.origin}/document-upload/{token}`, copies to clipboard via `navigator.clipboard.writeText`, shows a brief "Link copied" confirmation (reuses the existing inline-feedback pattern, e.g. a transient text swap on the button — no new toast system).
- **Revoking**: the row shows "Revoke link" instead of "Copy link" when `request.hasActiveShareLink` is true (sourced from the normal list response's new flag, so it's correct on first load, not just after a same-session action). Revoking calls the revoke endpoint, flips the row back to "Copy link". Generating again after a revoke issues a fresh token as normal.
- **Public page**: new standalone route `/document-upload/:token`, registered outside the existing auth guard in `app.routes.ts`. New `PublicDocumentUploadComponent`:
  - Fetches the preview on load. Shows a plain "This link has expired" or "This document has already been provided" state if applicable (no form in either case).
  - Otherwise shows the request's title/note/category/job context and a minimal upload form: file picker, filename field (reusing the existing rename-before-upload UX), Upload button. No visibility/category picker — both are fixed server-side.
  - No app shell/sidebar/topbar — this page is reached by people with no account, so it renders standalone.
- New `DocumentRequestLinkService` (Angular), two methods: `getPreview(token)`, `upload(token, file, displayFileName)`. Separate from `DocumentRequestService` for the same trust-boundary-visibility reason as the backend controller split — this service never sends a workspace/job id or auth header because it structurally can't.

## Error Handling

- Invalid/unknown token → `NotFoundException` (404) — same "don't reveal existence" reasoning already used for a Client requesting an `Internal` document by id.
- Expired token → surfaced as `Expired: true` in the preview response (200, not an error) so the public page can render a clear message rather than a generic failure.
- Already-fulfilled → surfaced as `AlreadyFulfilled: true` in preview (200); the upload endpoint itself still hard-rejects with `ValidationException` (400) as defense in depth if someone POSTs directly without checking the preview first.
- File validation failures (bad extension, oversized) → unchanged `ValidationException` from `DocumentService.UploadAsync`, surfaced the same way as every other upload path.

## Testing

- Service tests: generate link (Admin/Surveyor only, Client forbidden — reuses `EnsureJobAccessAsync`'s existing behavior, so this is mostly a signature-level check); regenerating overwrites the old token, old token no longer resolves; revoke clears the token and old token no longer resolves; `GetByShareTokenAsync` on unknown/expired/inactive/revoked token throws `NotFoundException`/reflects expiry; `UploadViaShareTokenAsync` succeeds on `Pending`/`Reopened`, rejects on `Fulfilled`; uploaded document's `UploadedBy` equals `RequestedBy`; uploaded document's `Visibility` is always `ClientVisible` regardless of what (if anything) is passed.
- Manual: generate a link as Admin, open it in an unauthenticated context (incognito/different browser), confirm preview renders, upload succeeds, request flips to Fulfilled; confirm re-visiting the same link now shows "already provided"; confirm an expired link (manually back-date `ShareTokenExpiresAt` in the DB for the test, or wait — document whichever is used) shows "expired."

## Out of Scope (v1)

- Email delivery of the link (user explicitly wants manual share via messaging app) — no email integration.
- Link analytics (view count, IP logging beyond the existing rate limiter).
- Multiple simultaneous links per request, or per-recipient links.
