# Job Documents UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Angular UI for the Job Documents API — a Documents card on the Job Detail page where Admin/Surveyor/Client can upload, list, preview (images/PDF inline), download, and delete (Admin/Surveyor only) documents.

**Architecture:** `DocumentService` mirrors `MilestoneService`'s shape exactly. `DocumentUploadWidgetComponent` mirrors `AddPersonWidgetComponent`/`AddLandWidgetComponent`. A new `DocumentViewerModalComponent` renders images/PDF from a blob URL; everything else falls back to a Download button. One blob-fetch method (`getFileBlob`) backs both preview and download — no second HTTP method, no duplicated object-URL lifecycle code.

**Tech Stack:** Angular 21 standalone components, signals, `HttpClient` (blob response type for file fetch), Tailwind utility classes already defined in this codebase (`card`, `input-field`, `btn-primary`, `btn-secondary`), Karma/Jasmine + `HttpClientTestingModule` for service tests (matching `job.service.spec.ts`).

## Global Constraints

- No new npm dependencies — Word/Excel files are download-only, no rendering library (per UI design spec's YAGNI decision).
- Client role: visibility picker hidden on upload (always sends `ClientVisible`); no Remove button; no Visibility badge in the list (redundant — everything they see is already `ClientVisible`).
- Delete requires a confirm step (the one deviation from this page's single-click removes elsewhere) — a lost document upload isn't a quick re-add.
- Route/base URL convention: `${environment.apiBaseUrl}/workspace/{workspaceId}/job/{jobId}/document`, matching `MilestoneService`.
- Spec: `docs/superpowers/specs/2026-08-11-job-documents-ui-design.md`.
- Do not run `git commit` for any step in this plan — commit only when the user explicitly says to.

---

### Task 1: `DocumentService`

**Files:**
- Create: `ui/src/app/core/document.service.ts`
- Create: `ui/src/app/core/document.service.spec.ts`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `Document` interface (`documentId, jobId, fileName, contentType, fileSizeBytes, category, visibility, uploadedBy, createdAt, updatedAt`) and `DocumentService` with `list(workspaceId, jobId): Observable<Document[]>`, `upload(workspaceId, jobId, file: File, category: string, visibility: string): Observable<Document>`, `getFileBlob(workspaceId, jobId, documentId): Observable<Blob>`, `delete(workspaceId, jobId, documentId): Observable<void>`. Tasks 2–4 consume these exact signatures.

- [ ] **Step 1: Write the failing tests**

`ui/src/app/core/document.service.spec.ts`, matching `job.service.spec.ts`'s shape:

```ts
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { DocumentService } from './document.service';
import { environment } from '../../environments/environment';

describe('DocumentService', () => {
  let service: DocumentService;
  let httpMock: HttpTestingController;
  const workspaceId = 'ws-1';
  const jobId = 'j1';
  const base = `${environment.apiBaseUrl}/workspace/${workspaceId}/job/${jobId}/document`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [DocumentService]
    });
    service = TestBed.inject(DocumentService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list() unwraps ApiResponse and hits the correct URL', () => {
    const docs = [{ documentId: 'd1', jobId, fileName: 'plan.pdf', contentType: 'application/pdf', fileSizeBytes: 100, category: 'SurveyPlan', visibility: 'ClientVisible', uploadedBy: 'u1', createdAt: '2026-01-01', updatedAt: '2026-01-01' }];
    service.list(workspaceId, jobId).subscribe(result => expect(result).toEqual(docs));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: docs });
  });

  it('upload() posts a FormData body with File, Category, Visibility', () => {
    const doc = { documentId: 'd1', jobId, fileName: 'plan.pdf', contentType: 'application/pdf', fileSizeBytes: 100, category: 'SurveyPlan', visibility: 'ClientVisible', uploadedBy: 'u1', createdAt: '2026-01-01', updatedAt: '2026-01-01' };
    const file = new File(['content'], 'plan.pdf', { type: 'application/pdf' });

    service.upload(workspaceId, jobId, file, 'SurveyPlan', 'ClientVisible').subscribe(result => expect(result).toEqual(doc));

    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);
    const body = req.request.body as FormData;
    expect(body.get('File')).toBe(file);
    expect(body.get('Category')).toBe('SurveyPlan');
    expect(body.get('Visibility')).toBe('ClientVisible');
    req.flush({ success: true, data: doc });
  });

  it('getFileBlob() gets the document by id with blob response type', () => {
    const blob = new Blob(['bytes'], { type: 'application/pdf' });
    service.getFileBlob(workspaceId, jobId, 'd1').subscribe(result => expect(result).toEqual(blob));
    const req = httpMock.expectOne(`${base}/d1`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    req.flush(blob);
  });

  it('delete() deletes with no body', () => {
    service.delete(workspaceId, jobId, 'd1').subscribe();
    const req = httpMock.expectOne(`${base}/d1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail (compile error)**

```bash
cd ui && ng test --watch=false --include='**/document.service.spec.ts'
```

Expected: fails, `DocumentService` module not found.

- [ ] **Step 3: Write `DocumentService`**

`ui/src/app/core/document.service.ts`:

```ts
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

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

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
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

  /**
   * Fetches the file as a Blob - both preview and download go through this one method.
   * A plain <a href> to this endpoint would 401: the JWT rides an Authorization header
   * the jwtInterceptor attaches only to HttpClient requests, not bare navigation.
   */
  getFileBlob(workspaceId: string, jobId: string, documentId: string): Observable<Blob> {
    return this.http.get(`${this.base(workspaceId, jobId)}/${documentId}`, { responseType: 'blob' });
  }

  delete(workspaceId: string, jobId: string, documentId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId, jobId)}/${documentId}`);
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd ui && ng test --watch=false --include='**/document.service.spec.ts'
```

Expected: 4 passed.

- [ ] **Step 5: Do not commit** (per Global Constraints — wait for explicit instruction)

---

### Task 2: `DocumentUploadWidgetComponent`

**Files:**
- Create: `ui/src/app/pages/job/document-upload-widget/document-upload-widget.component.ts`

**Interfaces:**
- Consumes: `DocumentService.upload()` (Task 1).
- Produces: `DocumentUploadWidgetComponent` with `@Input() workspaceId: string`, `@Input() jobId: string`, `@Input() isClient: boolean`, `@Output() added = new EventEmitter<Document>()`. Task 4's `job-detail.component.ts` consumes this exact selector/inputs/output.

- [ ] **Step 1: Write the component**

`ui/src/app/pages/job/document-upload-widget/document-upload-widget.component.ts`, following `AddPersonWidgetComponent`'s structure (border-wrapped card, local signals for state, `error()` signal, no separate spec file — matches this directory's existing convention of no component-level tests for job sub-widgets):

```ts
import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Document, DocumentService } from '../../../core/document.service';

const CATEGORIES = ['SurveyPlan', 'LegalDocument', 'Photo', 'Other'];

@Component({
  selector: 'app-document-upload-widget',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="border border-neutral-200 rounded-md p-md space-y-sm">
      <input
        #fileInput
        class="input-field text-sm"
        type="file"
        (change)="onFileSelected(fileInput.files)"
      />

      <select class="input-field text-sm" [(ngModel)]="category">
        @for (c of categories; track c) {
          <option [value]="c">{{ c }}</option>
        }
      </select>

      @if (!isClient) {
        <select class="input-field text-sm" [(ngModel)]="visibility">
          <option value="Internal">Internal (Admin/Surveyor only)</option>
          <option value="ClientVisible">Client Visible</option>
        </select>
      }

      @if (error()) {
        <p class="text-xs text-primary-500">{{ error() }}</p>
      }

      <button
        type="button"
        class="btn-primary text-xs"
        [disabled]="!selectedFile || uploading()"
        (click)="submit()"
      >
        {{ uploading() ? 'Uploading…' : 'Upload' }}
      </button>
    </div>
  `
})
export class DocumentUploadWidgetComponent {
  @Input() workspaceId = '';
  @Input() jobId = '';
  @Input() isClient = false;
  @Output() added = new EventEmitter<Document>();

  categories = CATEGORIES;
  category = 'Other';
  visibility = 'Internal';
  selectedFile: File | null = null;
  uploading = signal(false);
  error = signal('');

  constructor(private documentService: DocumentService) {}

  onFileSelected(files: FileList | null): void {
    this.selectedFile = files?.item(0) ?? null;
    this.error.set('');
  }

  submit(): void {
    if (!this.selectedFile) return;
    const effectiveVisibility = this.isClient ? 'ClientVisible' : this.visibility;

    this.error.set('');
    this.uploading.set(true);
    this.documentService.upload(this.workspaceId, this.jobId, this.selectedFile, this.category, effectiveVisibility).subscribe({
      next: (doc) => {
        this.added.emit(doc);
        this.reset();
      },
      error: (err) => {
        this.uploading.set(false);
        this.error.set(err.error?.message ?? 'Could not upload document.');
      }
    });
  }

  private reset(): void {
    this.selectedFile = null;
    this.category = 'Other';
    this.visibility = 'Internal';
    this.uploading.set(false);
  }
}
```

`isClient` is passed in from `job-detail.component.ts` (Task 4) rather than the widget calling `CurrentWorkspaceService` itself — keeps the widget a pure input/output component like `AddPersonWidgetComponent`, no hidden service dependency on workspace state.

- [ ] **Step 2: Build to verify it compiles**

```bash
cd ui && ng build --configuration development
```

Expected: succeeds, no template/type errors.

- [ ] **Step 3: Do not commit** (per Global Constraints)

---

### Task 3: `DocumentViewerModalComponent`

**Files:**
- Create: `ui/src/app/pages/job/document-viewer-modal/document-viewer-modal.component.ts`

**Interfaces:**
- Consumes: `Document` type (Task 1). Receives an already-fetched blob URL — does not call `DocumentService` itself.
- Produces: `DocumentViewerModalComponent` with `@Input() document!: Document`, `@Input() blobUrl!: string`, `@Output() closed = new EventEmitter<void>()`. Task 4 owns the blob fetch/revoke lifecycle and passes the URL in.

- [ ] **Step 1: Write the component**

`ui/src/app/pages/job/document-viewer-modal/document-viewer-modal.component.ts`, following the fixed-overlay modal pattern already used for the "unsaved changes" dialog in `job-detail.component.ts`:

```ts
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { Document } from '../../../core/document.service';

@Component({
  selector: 'app-document-viewer-modal',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="close()">
      <div class="card w-full max-w-2xl" (click)="$event.stopPropagation()">
        <div class="flex items-center justify-between mb-md">
          <h2 class="text-sm font-semibold text-neutral-900 truncate">{{ document.fileName }}</h2>
          <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" (click)="close()">Close</button>
        </div>

        @if (isImage()) {
          <img [src]="safeUrl()" class="max-w-full max-h-[70vh] mx-auto" [alt]="document.fileName" />
        } @else if (isPdf()) {
          <iframe [src]="safeUrl()" class="w-full h-[70vh] border-0"></iframe>
        } @else {
          <div class="text-center py-lg">
            <p class="text-sm text-neutral-600 mb-md">Preview isn't available for this file type.</p>
            <a [href]="blobUrl" [download]="document.fileName" class="btn-primary text-xs">Download</a>
          </div>
        }
      </div>
    </div>
  `
})
export class DocumentViewerModalComponent {
  @Input() document!: Document;
  @Input() blobUrl!: string;
  @Output() closed = new EventEmitter<void>();

  constructor(private sanitizer: DomSanitizer) {}

  isImage(): boolean {
    return this.document.contentType.startsWith('image/');
  }

  isPdf(): boolean {
    return this.document.contentType === 'application/pdf';
  }

  safeUrl(): SafeResourceUrl {
    return this.sanitizer.bypassSecurityTrustResourceUrl(this.blobUrl);
  }

  close(): void {
    this.closed.emit();
  }
}
```

`bypassSecurityTrustResourceUrl` is required for the `<iframe src>` binding (Angular blocks unsafe URLs there by default) — the URL is a same-origin `blob:` URL this component's caller created from a response it fetched itself, not user-supplied input, so this is the standard safe use of that API, not a bypass of real user input sanitization.

- [ ] **Step 2: Build to verify it compiles**

```bash
cd ui && ng build --configuration development
```

Expected: succeeds.

- [ ] **Step 3: Do not commit** (per Global Constraints)

---

### Task 4: Wire into `job-detail.component.ts`

**Files:**
- Modify: `ui/src/app/pages/job/job-detail.component.ts`

**Interfaces:**
- Consumes: `DocumentService` (Task 1), `DocumentUploadWidgetComponent` (Task 2), `DocumentViewerModalComponent` (Task 3).
- Produces: nothing further — this is the integration point.

- [ ] **Step 1: Add imports and the `documents` signal**

In `ui/src/app/pages/job/job-detail.component.ts`, add to the top imports:

```ts
import { Document, DocumentService } from '../../core/document.service';
import { DocumentUploadWidgetComponent } from './document-upload-widget/document-upload-widget.component';
import { DocumentViewerModalComponent } from './document-viewer-modal/document-viewer-modal.component';
```

Add `DocumentUploadWidgetComponent, DocumentViewerModalComponent` to the `@Component` `imports` array.

Add near the other signal declarations:

```ts
documents = signal<Document[]>([]);
viewingDocument = signal<Document | null>(null);
viewingBlobUrl = signal<string | null>(null);
confirmingDeleteDocument = signal<Document | null>(null);
documentError = signal('');
```

Inject `DocumentService` in the constructor alongside `MilestoneService`.

- [ ] **Step 2: Include documents in the initial fetch**

In `fetch()`'s `forkJoin`, add `documents: this.documentService.list(this.workspaceId, this.jobId)` and, in the `next` handler, `this.documents.set(documents);`.

- [ ] **Step 3: Add the Documents card to the template**

After the Milestones card's closing `</div>` (before the final closing `</div>` of the `max-w-3xl` wrapper), add:

```html
<div class="card">
  <h2 class="text-sm font-semibold text-neutral-900 mb-md">Documents</h2>
  @if (documents().length > 0) {
    <div class="space-y-xs mb-md">
      @for (d of documents(); track d.documentId) {
        <div class="flex items-center justify-between gap-sm px-md py-sm rounded bg-neutral-50">
          <div class="min-w-0">
            <span class="text-sm text-neutral-900 truncate block">{{ d.fileName }}</span>
            <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600 mr-xs">{{ d.category }}</span>
            @if (!isClient()) {
              <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ d.visibility }}</span>
            }
          </div>
          <div class="flex items-center gap-sm flex-shrink-0 whitespace-nowrap">
            <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="viewDocument(d)">View</button>
            <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="downloadDocument(d)">Download</button>
            @if (!isClient()) {
              <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="confirmingDeleteDocument.set(d)">Remove</button>
            }
          </div>
        </div>
      }
    </div>
  }
  @if (documentError()) {
    <p class="text-xs text-primary-500 mb-sm">{{ documentError() }}</p>
  }
  <app-document-upload-widget
    [workspaceId]="workspaceId"
    [jobId]="jobId"
    [isClient]="isClient()"
    (added)="onDocumentAdded($event)"
  />
</div>
```

Note this reuses `isClient()`, already defined on this component for the Milestones section.

- [ ] **Step 4: Add the viewer modal and delete-confirm dialog to the template**

After the existing `@if (confirmingLeave())` block, add:

```html
@if (viewingDocument(); as doc) {
  @if (viewingBlobUrl(); as url) {
    <app-document-viewer-modal [document]="doc" [blobUrl]="url" (closed)="closeViewer()" />
  }
}

@if (confirmingDeleteDocument(); as doc) {
  <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg">
    <div class="card w-full max-w-sm">
      <h2 class="text-base font-semibold text-neutral-900">Remove document?</h2>
      <p class="text-sm text-neutral-600 mt-xs">
        "{{ doc.fileName }}" will be removed. This can't be undone from here.
      </p>
      <div class="flex items-center justify-end gap-sm mt-lg">
        <button type="button" class="btn-secondary text-xs" (click)="confirmingDeleteDocument.set(null)">Cancel</button>
        <button type="button" class="btn-primary text-xs" (click)="deleteDocument(doc)">Remove</button>
      </div>
    </div>
  </div>
}
```

- [ ] **Step 5: Add the component methods**

Alongside the existing Milestone methods (`onMilestoneStatusChange`, `removeMilestone`, etc.):

```ts
onDocumentAdded(doc: Document): void {
  this.documents.update(list => [doc, ...list]);
}

viewDocument(doc: Document): void {
  this.documentError.set('');
  this.documentService.getFileBlob(this.workspaceId, this.jobId, doc.documentId).subscribe({
    next: (blob) => {
      this.viewingDocument.set(doc);
      this.viewingBlobUrl.set(URL.createObjectURL(blob));
    },
    error: (err) => this.documentError.set(err.error?.message ?? 'Could not open document.')
  });
}

closeViewer(): void {
  const url = this.viewingBlobUrl();
  if (url) URL.revokeObjectURL(url);
  this.viewingDocument.set(null);
  this.viewingBlobUrl.set(null);
}

downloadDocument(doc: Document): void {
  this.documentError.set('');
  this.documentService.getFileBlob(this.workspaceId, this.jobId, doc.documentId).subscribe({
    next: (blob) => {
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = doc.fileName;
      link.click();
      URL.revokeObjectURL(url);
    },
    error: (err) => this.documentError.set(err.error?.message ?? 'Could not download document.')
  });
}

deleteDocument(doc: Document): void {
  this.documentService.delete(this.workspaceId, this.jobId, doc.documentId).subscribe({
    next: () => {
      this.documents.update(list => list.filter(d => d.documentId !== doc.documentId));
      this.confirmingDeleteDocument.set(null);
    },
    error: (err) => {
      this.documentError.set(err.error?.message ?? 'Could not remove document.');
      this.confirmingDeleteDocument.set(null);
    }
  });
}
```

`viewDocument`/`downloadDocument` both call the same `getFileBlob` — this is the DRY point from the spec: one fetch, two different things done with the resulting blob. Note the local variable `document` inside `downloadDocument` shadows the global `Document` (DOM) type only within that method body, not the `Document` interface imported from `document.service` — TypeScript resolves `document.createElement` to the DOM global correctly since the component's own `Document` import is a type, not a runtime value, so there's no actual collision, but if the build flags an ambiguity, rename the component's imported type usage sites are all as parameter/field types only (never referenced as `Document.something`), so this is safe.

- [ ] **Step 6: Build and manually verify**

```bash
cd ui && ng build --configuration development
```

Expected: succeeds.

Run the `run` skill (or `ng serve` via the Browser pane tooling) against the already-running API from the backend plan, and:
1. As Admin: upload a PDF with category SurveyPlan, visibility ClientVisible — appears in the list with badges.
2. Click View — modal opens, PDF renders in the iframe.
3. Click Download — file downloads with its original filename.
4. Upload a `.docx` — View shows the "Preview isn't available" fallback with a working Download link.
5. Click Remove — confirm dialog appears; Cancel keeps it, Remove deletes it and closes the dialog.
6. Log in as Client — visibility select is absent on the upload widget, visibility badges absent in the list, Remove button absent per row.

- [ ] **Step 7: Do not commit** (per Global Constraints — this plan's implementation is done; wait for the user's explicit go-ahead before running `git commit` on any of it)

---

## Self-Review Notes

- **Spec coverage:** `DocumentService` + blob DRY point (Task 1), upload widget matching existing convention (Task 2), viewer modal with image/PDF/fallback branches (Task 3), full card wiring including confirm-delete and Client restrictions (Task 4). All spec sections covered.
- **Type consistency:** `Document` interface defined once in Task 1's `document.service.ts`, imported (not redefined) by Tasks 2–4. `DocumentService` method signatures used identically across all three consuming tasks.
- **Commit discipline:** every task ends with an explicit "do not commit" step per the user's standing instruction — this plan does not auto-commit at task boundaries the way the backend plan did.
