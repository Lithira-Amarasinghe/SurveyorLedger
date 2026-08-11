# Job Documents UI — Design Spec

Date: 2026-08-11

## Purpose

Angular UI for the Job Documents API (see `2026-08-11-job-documents-design.md`): upload, list, preview, download, delete documents from the Job Detail page.

## Scope

- Documents card added to `job-detail.component.ts`, same slot as People/Land/Milestones.
- No new route/page.
- Images and PDF preview inline (blob URL). Word/Excel/other: download-only, no rendering library.

## DRY: one blob-fetch path for preview and download

The JWT is delivered via an `Authorization` header the `jwtInterceptor` attaches to `HttpClient` requests only — a plain `<a href>` to the file endpoint 401s. Both preview and download therefore need the file as a `Blob` fetched through `HttpClient`, so there is exactly one method that does that fetch:

```ts
getFileBlob(workspaceId: string, jobId: string, documentId: string): Observable<Blob> {
  return this.http.get(`${this.base(workspaceId, jobId)}/${documentId}`, { responseType: 'blob' });
}
```

- **Preview** (View button): `getFileBlob()` → `URL.createObjectURL(blob)` → pass to `DocumentViewerModalComponent`, which puts it in `<img>`/`<iframe> src`. Revoked on modal close.
- **Download** (Download button): `getFileBlob()` → `URL.createObjectURL(blob)` → synthetic `<a download>` click → revoke immediately after.

Both call sites live in `DocumentService` and the modal/component that consumes it — no second fetch method, no duplicated blob/object-URL lifecycle code. A shared private helper `openBlobUrl(blob, fileName, mode: 'view' | 'download')` in the component that needs both (list row actions) does the `createObjectURL`/revoke bookkeeping once.

## Files

### `ui/src/app/core/document.service.ts` (new)

Mirrors `MilestoneService`'s shape:

```ts
export interface Document {
  documentId: string;
  jobId: string;
  fileName: string;
  contentType: string;
  fileSizeBytes: number;
  category: 'SurveyPlan' | 'LegalDocument' | 'Photo' | 'Other';
  visibility: 'Internal' | 'ClientVisible';
  uploadedBy: string;
  createdAt: string;
  updatedAt: string;
}

@Injectable({ providedIn: 'root' })
export class DocumentService {
  constructor(private http: HttpClient) {}

  private base(workspaceId: string, jobId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/job/${jobId}/document`;
  }

  list(workspaceId: string, jobId: string): Observable<Document[]> {
    return this.http.get<ApiResponse<Document[]>>(this.base(workspaceId, jobId)).pipe(map(res => res.data));
  }

  upload(workspaceId: string, jobId: string, file: File, category: string, visibility: string): Observable<Document> {
    const form = new FormData();
    form.append('File', file);
    form.append('Category', category);
    form.append('Visibility', visibility);
    return this.http.post<ApiResponse<Document>>(this.base(workspaceId, jobId), form).pipe(map(res => res.data));
  }

  getFileBlob(workspaceId: string, jobId: string, documentId: string): Observable<Blob> {
    return this.http.get(`${this.base(workspaceId, jobId)}/${documentId}`, { responseType: 'blob' });
  }

  delete(workspaceId: string, jobId: string, documentId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId, jobId)}/${documentId}`);
  }
}
```

No separate download-vs-preview HTTP method — same reasoning as the API's single `GET /{id}?download=` endpoint (Task 4 of the backend plan), carried through to the client.

### `ui/src/app/pages/job/document-upload-widget/document-upload-widget.component.ts` (new)

Matches `AddPersonWidgetComponent`/`AddLandWidgetComponent` convention: `@Input() workspaceId`, `@Input() jobId`, `@Output() added = new EventEmitter<Document>()`. Internal state: file, category, visibility. Visibility select only rendered when caller role isn't Client (checked via `CurrentWorkspaceService`, same pattern `job-detail.component.ts` already uses for `isClient()`); Client uploads always send `ClientVisible`.

### `ui/src/app/pages/job/document-viewer-modal/document-viewer-modal.component.ts` (new)

`@Input() document: Document`, `@Input() blobUrl: string`, `@Output() closed = new EventEmitter<void>()`. Renders `<img [src]="blobUrl">` for `Photo`/image content-types, `<iframe [src]="blobUrl">` for `application/pdf`, otherwise a filename + Download button. Does not fetch the blob itself — receives it from the parent, which owns the fetch/revoke lifecycle (single ownership, no duplicate object-URL management between modal and list).

### `ui/src/app/pages/job/job-detail.component.ts` (modified)

New card, same structure as the Milestones card:
- `documents = signal<Document[]>([])`, fetched in `fetch()`'s existing `forkJoin` alongside job/participants/lands/milestones.
- Flat list, one row per document: filename, category badge, visibility badge (hidden for Client — they only ever see `ClientVisible` docs anyway), View / Download / Remove actions.
- Remove requires typed-through confirm (`confirmingDelete = signal<Document | null>(null)`, small inline confirm block — not a new modal component, reuses the page's existing `card` styling) — the one deliberate deviation from this page's single-click removes elsewhere, because a lost upload isn't a quick re-add.
- `<app-document-upload-widget>` at the bottom of the card, same slot pattern as `<app-add-person-widget>`/`<app-add-land-widget>`.

## Error Handling

Same pattern as every other section on this page: `err.error?.message ?? 'Could not <verb> document.'` surfaced via the page's existing `error` signal (upload/list/delete) or a local widget-scoped error signal (upload widget, matching `AddPersonWidgetComponent`'s `markFailed()`).

## Testing

Manual verification only, matching this page's existing convention (`job-detail.component.ts` has no dedicated spec file for its sub-resource cards; `MilestoneService`/`JobService` Angular services likewise have no `.spec.ts` beyond `job.service.spec.ts`, which predates this pattern). Verify via the running UI + API:
1. Upload a PDF as Admin with `ClientVisible` — appears in list, View renders inline, Download saves the file.
2. Upload a `.docx` — View shows filename + Download fallback, no render attempt.
3. Log in as Client — visibility picker absent on upload, Internal documents (if any) don't appear in the list.
4. Remove as Admin — confirm dialog appears, document disappears after confirm.
5. Remove button absent for Client.

## Out of Scope (v1)

- Third-party Office-document rendering (per brainstorming decision — YAGNI, no browser-native support exists).
- Category filtering/grouping in the list (flat list for v1).
- Drag-drop upload (matches existing widgets, which are click-to-select).
