# Land Convenience Features — Design Spec

Date: 2026-08-13

## Purpose

Four independent, accessibility-focused additions to the Land feature: click-to-call/message the owner, a printable land summary, a QR code for the land's location, and site photos. Aimed at people who aren't comfortable with software — tapping a phone number, scanning a QR code, or looking at a photo should replace typing/reading wherever it can.

## Scope

- All four features are additive to the existing Land domain (`api/src/SurveyorLedger.API/Services/LandService.cs`, `api/src/SurveyorLedger.API/Controllers/LandController.cs`, `ui/src/app/pages/land/`).
- No changes to `Document`/`DocumentRequest` — evaluated and rejected both a shared polymorphic Document (nullable dual-FK) and a Job/Land join-table redesign; land photos get their own entity (see Feature 4).
- No new external service dependencies: QR generation and PDF export are both client-side, no API key, no billing, no network call at render time.

## Feature 1: Click-to-call / message owner

Pure frontend, no backend change, no validation change to `OwnerPhone` (stays free text).

Wherever `land.ownerPhone` is rendered (`land-detail-panel.component.ts`, and the collapsed row summary that already shows `ownerName` in `land-list.component.ts`/`job-detail.component.ts`), add two links next to it:
- `tel:{digits}` — phone call
- `https://wa.me/{digits}` — WhatsApp message

`{digits}` is `ownerPhone.replace(/[^\d+]/g, '')` — strip everything but digits and a leading `+`, computed at render time only for the `href`; the displayed text stays exactly as entered. A single small pure function, `telHref(phone)` / `whatsAppHref(phone)`, added to `land.service.ts` next to the existing `addressLine` helper — same "shared formatting helper" pattern already established there. Malformed numbers just produce a link that doesn't resolve correctly on tap; no validation added, matches the earlier decision to not add phone format enforcement.

## Feature 2: Printable land summary

New route `/app/workspace/:id/lands/:landId/print`, registered like the other authenticated routes but rendering standalone (no `AppShellComponent` — sidebar/topbar/command-palette would only clutter a printed page). New `LandPrintComponent`, fetches the same `Land`/`LandSurvey[]`/`LandDeed[]`/`LandBoundary[]` data `land-detail-panel` already fetches (reuses `LandService` as-is, no new endpoints) and lays it out as a single printable page: address, owner (with the click-to-call links from Feature 1 — harmless on paper, useful if viewed on a phone before printing), size, deeds/surveys/boundaries lists, and — if a location is set — a static map image (`https://staticmap.openstreetmap.de/staticmap.php?center={lat},{lng}&zoom=16&size=600x300&markers={lat},{lng},red-pushpin`, OSM's free static-map renderer; a plain `<img>`, not the interactive Leaflet map, since print media can't rely on canvas/JS running) plus the QR code from Feature 3.

A `@media print` block in the component's styles hides anything not meant for paper (there's nothing else on this page to hide, but keeps the page print-safe if margins/branding get added later) and a "Print / Save as PDF" button calls `window.print()`. The browser's native print dialog is the actual PDF export mechanism — no server-side PDF library, no new dependency.

"Print summary" button added to `land-detail-panel.component.ts`'s existing header row (next to Delete), navigating to the new route.

## Feature 3: QR code for land location

Client-side only. New dependency: `qrcode` npm package (renders to a `<canvas>`/data-URL locally, no network call, no external QR-image API — consistent with the earlier Leaflet-vendoring decision to avoid runtime dependencies on outside services). New small `LandLocationQrComponent` (`ui/src/app/shared/land-location-qr/`), takes `@Input() lat/lng`, renders a canvas encoding `https://www.google.com/maps?q={lat},{lng}` (the same deep link Feature 1's Google Maps button already uses — one URL format, not two), with a "Download PNG" button (`canvas.toDataURL()` → anchor download, no server round-trip).

Shown in `land-detail-panel.component.ts`'s Location block, next to the existing "Open in Google Maps"/"Copy Google Maps link" buttons, only when a location is set. Also embedded in the Feature 2 print summary — printing/sticking it on-site (a gate, a marker) is the actual point: someone who can't read the address can still point a phone camera at the QR and get directions.

## Feature 4: Land photos

New `LandPhoto` entity — deliberately separate from `Document` (see Scope: a shared/polymorphic Document was considered and rejected to avoid widening the tenant-isolation join path and to keep `Category`/`Visibility`, which are Job-fulfillment concepts, off a table that doesn't need them).

### Entity

```csharp
public class LandPhoto
{
    public Guid Id { get; set; }
    public Guid LandId { get; set; }
    public string FileName { get; set; }
    public string StoredPath { get; set; }
    public string ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public Guid UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Land Land { get; set; }
    public User UploadedByUser { get; set; }
}
```

No `IsActive`/soft-delete — a mis-uploaded photo is a hard delete, same reasoning already used for `LandSurvey`/`LandDeed`/`LandBoundary` ("corrects a mis-entered record, not meaningful history to preserve once wrong"). No `Category`/`Visibility` — every photo is visible to everyone with land view access, there's no fulfillment workflow here.

### Backend

`LandPhotoService` (or methods added directly to `LandService`, matching how Surveys/Deeds/Boundaries already live inside it rather than their own services — same file, same pattern, no new service class): `UploadPhotoAsync`, `GetPhotosAsync`, `DeletePhotoAsync`. Reuses the existing `IFileStorageService` unchanged (`SaveAsync`/`OpenAsync`/`DeleteAsync` are already storage-path-agnostic) with the path convention `{workspaceId}/land/{landId}/{guid}_{filename}`, mirroring Document's `{workspaceId}/{jobId}/{guid}_{filename}`. Image-only allowlist (`.jpg .jpeg .png`), reuses `DocumentService.MaxFileSizeBytes` by reference (25MB) rather than a duplicated constant. Same permission gate as every other land sub-resource: `EnsureLandAccessAsync(..., "edit")` to upload/delete, `"view"` to list.

Routes on `LandController`: `POST /{id}/photos` (multipart), `GET /{id}/photos`, `GET /{id}/photos/{photoId}` (streams the file, mirrors `DocumentController`'s download action), `DELETE /{id}/photos/{photoId}`.

### Frontend

New `PhotoGridComponent` (`ui/src/app/shared/photo-grid/`) — the first shared upload UI in the app (the two existing upload widgets are each purpose-built to their own page). Thumbnail grid, a file input for adding photos, delete button per thumbnail, upload progress state. Takes `@Input() photos`, `@Output() upload`/`delete` — no HTTP inside the component, same "picker owns no save logic" pattern already used for `LandLocationPickerComponent`. Used in `land-detail-panel.component.ts` as a new "Photos" block, and read-only (no upload/delete inputs wired) in the Feature 2 print summary if photos exist — a printed photo is exactly the kind of "look, don't read" aid this whole feature set is about.

## Error Handling

- Feature 1: no validation, no error states — a bad link just doesn't resolve on tap, same as any `mailto:`/`tel:` link on the web today.
- Feature 2: if location isn't set, the static map/QR sections are simply omitted from the printed page (no broken image, no placeholder).
- Feature 3: `qrcode` generation is synchronous and local — no network failure mode to handle. If lat/lng are absent, the component isn't rendered at all (guarded by the same `@if` the existing Google Maps buttons use).
- Feature 4: extension/size validation reuses `DocumentService`'s existing `ValidationException` pattern, surfaced the same way file-upload errors already are elsewhere in the app.

## Testing

- Feature 4: service tests mirroring `LandLocationServiceTests` — upload succeeds for Admin/Surveyor, forbidden for Client; wrong extension/oversized file rejected; delete removes both the DB row and the stored file. `DocumentServiceTests` registers the real `LocalFileStorageService` (not a mock) against a temp-directory-backed test host — same approach applies here, no fake needed.
- Features 1–3: no backend, verified manually in-browser (tel/wa.me links resolve to the right href, print layout renders and omits map/QR when no location, QR encodes the correct URL, downloaded PNG opens and scans to the right link).

## Out of Scope (v1)

- EXIF stripping / photo compression before upload — matches Document's existing behavior (also unvalidated), not a regression, just not solved here.
- Photo captions/ordering — plain upload-time list order.
- Offline print styling (letterhead, page numbers) — a single clean page is enough for v1.
- A generic polymorphic attachment system — explicitly evaluated and rejected in favor of two purpose-built entities (Document, LandPhoto), each simple on its own.
