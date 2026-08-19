# Land photos as separate card + batch/group document uploads

## Context

Follow-up to the Land/Job document unification work (`2026-08-1x` document-unification pass). Land photos currently render merged into the general `documentRows` list alongside surveys/deeds/general docs — user wants them visually separate again (image-only upload/request), since photos serve a different purpose (site images) than legal documents (deeds, survey plans).

Separately: uploading multiple files (a batch of scan pages, several site photos, a multi-page deed) currently uploads one-at-a-time with no visual relationship between the resulting rows, and a document request can only ever be fulfilled by a single file. Both surfaces need "upload/fulfill with several files, see them as one group, delete/reopen the group as a unit."

## Goals

1. Land gets a dedicated **Photos** card (own upload button restricted to images, own request-form trigger, own `<app-document-list>` instance) — no longer merged into the general Documents list.
2. Any multi-file selection (direct upload or request fulfillment), on both Land and Job, renders as one collapsible group in the document list instead of N unrelated rows.
3. A document request can be fulfilled with multiple files; reopening a fulfilled request keeps its existing files visible and accumulates newly uploaded files into the same group.
4. No new bulk-upload/bulk-delete backend endpoints — reuse the existing per-file upload/fulfill/delete calls, coordinated client-side.

## Non-goals

- No change to Job's Documents-card-shows-linked-Land's-docs behavior (unrelated, already shipped).
- No drag-to-reorder or drag-drop within a group.
- No cross-owner batches (a batch is always scoped to one owner — one land's photos, one survey's attachments, etc.) — matches how uploads already work (one owner per upload button instance).

## Design

### 1. Photos — separate card

`land-detail-panel.component.ts` gets a new `Photos` section (sibling to `Documents`, Surveys, Deeds), reusing the same three shared components already used everywhere else:
- `<app-document-upload-button [accept]="'image/*'" (filesSelected)="onPhotoFilesSelected($event)">`
- `<app-document-request-form>` gated by `isRequestFormTarget('landPhoto', landId)` / triggered by `startOwnerRequest('landPhoto', landId)` (both already generic over ownerKind, just need `'landPhoto'` added to the existing union types)
- `<app-document-list [rows]="photoRows()" ...>`

`photoRows()` (already exists, currently spread into `documentRows`) simply stops being included in `documentRows` and gets its own template block instead. `onDocumentFilesSelected`'s MIME-sniff auto-routing (added last session, routes images into `Category=Photo` via the general endpoint) is removed — photos now upload through the existing dedicated `/photos` endpoint again via a new `onPhotoFilesSelected(files)` handler (calls `landService.uploadPhoto` per file), since the card is a distinct upload surface again.

`DocumentUploadButtonComponent` gains `@Input() accept = '.pdf,.doc,.docx,.xls,.xlsx,.jpg,.jpeg,.png'` (currently hardcoded in the template) so the Photos card can narrow it to `image/*` without a new component.

### 2. Client-generated BatchId — no new backend upload shape

Every multi-file selection already goes through `DocumentUploadButtonComponent`, which emits one `File[]` and lets the caller loop, uploading each file through the existing single-file endpoint. That loop stays. The only backend addition: every upload/fulfill endpoint that creates a `Document` accepts one new optional form field, `BatchId` (`Guid?`) — if omitted, behaves exactly as today (no batch).

Caller-side, every "upload N files" handler generates one `crypto.randomUUID()` when `files.length > 1` (single-file selections pass `undefined` — no grouping overhead for the common case) and passes it on every call in that loop:
- `onDocumentFilesSelected` (Land general docs), `onPhotoFilesSelected` (Land photos), survey/deed attachment upload handlers, Job's document upload handler.

`Document` entity gains `UploadBatchId` (`Guid?`, nullable, indexed alongside the existing `(OwnerType, OwnerId)` index as `(OwnerType, OwnerId, UploadBatchId)`). Surfaced on `Document`/`OwnedDocument`/`LandPhotoResponse` response DTOs as `uploadBatchId`.

### 3. Grouping renders inside `DocumentListComponent` — no duplicate markup

`DocRow` gains `batchId?: string | null`, populated from the caller's row-mapping functions (`buildOwnerRows`, `photoRows`, Job's row mapping) from each document's `uploadBatchId`.

`DocumentListComponent` groups its `@Input() rows` by `batchId` before rendering (a `computed`-style grouping done in the template/component, not by the caller — callers keep passing a flat list, same as today). A batch of exactly one row renders exactly as today, unchanged. A batch of 2+ rows renders as one header row — file-type icon badge showing count ("4 files"), first file's uploader/date, the shared request title/status badge if the batch came from a request fulfillment — with a chevron that expands to the existing per-row markup for each member, unchanged. The existing single-row template block is extracted into an `<ng-template #rowTpl let-row>` reused both for standalone rows and for each expanded group member, so there is exactly one place that renders "a document row."

New output `removeGroup = new EventEmitter<DocRow[]>()` fires with every member row when the group's delete action is confirmed; existing per-row `remove` output stays for standalone rows and is also available inside an expanded group (delete one member without removing the whole group). Callers implement `removeGroup` as a loop over their existing single-remove method — no new service methods, no new backend endpoint.

### 4. Requests fulfilled by multiple files, reopen keeps the group

`LandDocumentRequest`/`DocumentRequest` entities: `FulfilledDocumentId (Guid?)` → `FulfilledBatchId (Guid?)`. `FulfillAsync` (both Land and Job variants) accepts a `BatchId` parameter from the caller instead of generating an id internally for the created `Document`; if the request already has a `FulfilledBatchId` (i.e. this is a re-fulfillment after Reopen), the caller passes that same value back so new files join the existing group rather than starting a new one. First-ever fulfillment: caller generates a fresh id (same `crypto.randomUUID()` used for direct multi-uploads — one file still gets a batch id here since a request's fulfillment is always "the group for this request," even at size 1, so the group header can show the request's status/title even for a single-file fulfillment). `Reopen` is unchanged (status flips, `FulfilledBatchId` untouched) — the group keeps rendering its existing files, status badge reads "Reopened," and the row's fulfill affordance (`<input type="file" multiple>`) becomes available again on the group header for adding more.

`buildOwnerRows`'s existing per-request-row construction changes from "find the one doc matching `FulfilledDocumentId`" to "find every doc matching `FulfilledBatchId`" and attaches all of them (plus the request's `batchId`) so they render as one group carrying the request's title/status.

### 5. Migration

`FulfilledDocumentId` → `FulfilledBatchId` on both `LandDocumentRequest` and `DocumentRequest`, plus new `UploadBatchId` on `Document`. Data-preservation step before the schema-dropping migration (mirrors the earlier `LandPhoto`→`Document` migration): for every existing request row with a non-null `FulfilledDocumentId`, generate a fresh `Guid`, write it to that row's new `FulfilledBatchId` column and to the linked `Document.UploadBatchId`, via a one-time `sqlcmd` pass — then generate the clean EF migration that drops `FulfilledDocumentId` and adds `FulfilledBatchId`/`UploadBatchId`. Row counts get checked first (`SELECT COUNT(*) WHERE FulfilledDocumentId IS NOT NULL`); if zero, skip straight to the schema-only migration, same as the earlier `LandDocumentRequest.OwnerType` addition.

## 6. Province/district dropdowns, map reliability, owner picker wording

**Province/District dropdowns**: new `ui/src/app/shared/sri-lanka-locations.ts` exporting `PROVINCES: string[]` (9) and `DISTRICTS_BY_PROVINCE: Record<string, string[]>` (25 districts, official Sri Lanka province/district mapping). `land-detail-panel.component.ts`'s free-text `province`/`district` inputs become `<select>`s. `onProvinceChange(newProvince)`: if current `district` isn't in `DISTRICTS_BY_PROVINCE[newProvince]`, clear `district`. `onDistrictChange(newDistrict)`: reverse-lookup which province contains `newDistrict` and set `province` to it if different — so picking a district always fixes up the province too, not just the other direction. `add-land-widget.component.ts`'s standalone `district` free-text input (no `province` field there) becomes the same `<select>` sourced from the flattened district list, for consistency, with no cross-clear logic needed (nothing to clear against).

**Map not rendering reliably**: `land-location-picker.component.ts` calls `this.map.invalidateSize()` via a `ResizeObserver` on `mapEl.nativeElement`, added in `ngOnInit` and torn down in `ngOnDestroy`. Fires whenever the container's real size changes (e.g. a collapsed/tabbed panel becoming visible after the map was constructed at zero size) instead of relying on the user manually zooming to force Leaflet to relayout.

**Owner picker wording**: `owner-picker.component.ts`'s "Owner isn't in the system — enter details" link moves from below the search results to the same row as the "Owner" label (right-aligned), shortened to "+ New owner". Only rendered in search mode (unchanged condition), so it no longer shifts position as search results load/change.

## Testing

- Backend: `dotnet test --filter "FullyQualifiedName~LandDocumentRequest|FullyQualifiedName~DocumentRequest|FullyQualifiedName~LandPhoto"` after the entity/migration changes; full suite once at the end.
- Frontend: `npm run build`; `ng test --include **/document*.spec.ts --include **/land.service.spec.ts`.
- Manual: upload 3 photos at once on the new Photos card, confirm they group and expand; create a document request, fulfill with 2 files, confirm one group with the request's status badge; reopen it, fulfill again with 1 more file, confirm all 3 files now show in the same group; delete a whole group and confirm all member files are gone; upload a single file anywhere and confirm it still renders as a plain row (no group chrome); pick a province, confirm the district clears if it no longer matches; pick a district from another province, confirm the province field follows it; open a land panel inside a collapsed section, expand it, confirm the map tiles/pins render without needing a manual zoom; check the owner search screen and confirm "+ New owner" sits next to the label and doesn't move as results change.
