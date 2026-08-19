import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { CommonModule, NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { DocumentRequestLinkService, DocumentRequestLinkPreview } from '../../core/document-request-link.service';
import { DocumentUploadButtonComponent } from '../../shared/document-upload-button/document-upload-button.component';
import { DocumentViewerModalComponent } from '../../shared/document-viewer-modal/document-viewer-modal.component';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-public-document-upload',
  standalone: true,
  imports: [CommonModule, FormsModule, NgTemplateOutlet, DocumentUploadButtonComponent, DocumentViewerModalComponent, IconComponent],
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
            <p class="text-sm text-neutral-600 mt-xs">Thank you - the file{{ selectedFiles.length > 1 ? 's have' : ' has' }} been received.</p>
          } @else {
            <h1 class="text-lg font-semibold text-neutral-900">{{ p.title }}</h1>
            @if (p.jobTitle || p.workspaceName) {
              <p class="text-xs text-neutral-500 mt-xs">{{ p.workspaceName }} · {{ p.jobTitle }}</p>
            }
            @if (p.description) {
              <p class="text-sm text-neutral-600 mt-sm">{{ p.description }}</p>
            }

            <!-- Same row/group look as every document list in the app - one file plain, several grouped and named after the request. -->
            @if (selectedFiles.length > 1) {
              <div class="rounded bg-neutral-50 text-sm mt-lg">
                <div class="flex items-center gap-sm px-md py-sm cursor-pointer" (click)="groupExpanded.set(!groupExpanded())">
                  <div class="w-14 h-14 rounded-md bg-neutral-200 flex items-center justify-center flex-shrink-0 text-neutral-500 text-xs font-medium">
                    {{ selectedFiles.length }} files
                  </div>
                  <span class="text-neutral-900 flex-1">{{ p.title }}</span>
                  <button type="button" class="icon-btn" title="Expand" (click)="$event.stopPropagation(); groupExpanded.set(!groupExpanded())">
                    <app-icon [name]="groupExpanded() ? 'chevronUp' : 'chevronDown'" />
                  </button>
                </div>
                @if (groupExpanded()) {
                  <div class="pl-md pb-sm space-y-xs">
                    @for (file of selectedFiles; track file) {
                      <ng-container *ngTemplateOutlet="fileRow; context: { $implicit: file }"></ng-container>
                    }
                  </div>
                }
              </div>
            } @else if (selectedFiles.length === 1) {
              <div class="mt-lg">
                <ng-container *ngTemplateOutlet="fileRow; context: { $implicit: selectedFiles[0] }"></ng-container>
              </div>
            }
            <div class="mt-sm">
              <app-document-upload-button [label]="selectedFiles.length > 0 ? '+ Add another file' : '+ Choose file(s)'" (filesSelected)="onFilesSelected($event)" />
            </div>
            @if (uploadError()) {
              <p class="text-sm text-primary-500 mt-sm">{{ uploadError() }}</p>
            }
            <button type="button" class="btn-primary w-full mt-lg" [disabled]="selectedFiles.length === 0 || uploading()" (click)="submit()">
              {{ uploading() ? 'Uploading…' : 'Upload' }}
            </button>
          }
        }
      </div>
    </div>

    <ng-template #fileRow let-file>
      <div class="flex items-center gap-sm px-md py-sm rounded bg-neutral-50 text-sm">
        @if (previewUrl(file); as url) {
          <button type="button" class="flex-shrink-0" title="Preview" (click)="openPreview(file)">
            <img [src]="url" class="w-14 h-14 rounded-md object-cover border border-neutral-200" [alt]="file.name" />
          </button>
        } @else {
          <button type="button" class="w-14 h-14 rounded-md bg-neutral-200 flex items-center justify-center flex-shrink-0 text-neutral-500 border border-neutral-200" title="Preview" (click)="openPreview(file)">
            <app-icon name="view" />
          </button>
        }
        <div class="min-w-0 flex-1">
          @if (renamingFile() === file) {
            <input class="input-field text-xs px-xs py-xs" [(ngModel)]="renameValue" (keydown.enter)="confirmRename(file)" />
          } @else {
            <button type="button" class="text-primary-600 hover:text-primary-700 truncate text-left block w-full" [title]="file.name" (click)="openPreview(file)">
              {{ file.name }}
            </button>
          }
        </div>
        <div class="flex items-center gap-xs flex-shrink-0">
          @if (renamingFile() === file) {
            <button type="button" class="text-xs text-primary-600 font-medium" (click)="confirmRename(file)">Save</button>
            <button type="button" class="text-xs text-neutral-500" (click)="renamingFile.set(null)">Cancel</button>
          } @else {
            <button type="button" class="icon-btn" title="Rename" (click)="startRename(file)"><app-icon name="rename" /></button>
            <button type="button" class="icon-btn text-primary-500" title="Remove" (click)="removeFile(file)"><app-icon name="delete" /></button>
          }
        </div>
      </div>
    </ng-template>

    @if (viewingFile(); as vf) {
      <app-document-viewer-modal [document]="{ fileName: vf.name, contentType: vf.type }" [blobUrl]="viewingUrl()!" (closed)="closePreview()" />
    }
  `,
  styles: [`.icon-btn { display: flex; align-items: center; justify-content: center; width: 1.75rem; height: 1.75rem; border-radius: 0.25rem; color: var(--color-neutral-500, #737373); } .icon-btn:hover { background: var(--color-neutral-100, #f5f5f5); color: var(--color-primary-600, #0284c7); }`]
})
export class PublicDocumentUploadComponent implements OnInit, OnDestroy {
  token = '';
  loading = signal(true);
  loadError = signal('');
  preview = signal<DocumentRequestLinkPreview | null>(null);
  selectedFiles: File[] = [];
  groupExpanded = signal(true);
  uploading = signal(false);
  uploadError = signal('');
  uploaded = signal(false);

  renamingFile = signal<File | null>(null);
  renameValue = '';
  viewingFile = signal<File | null>(null);
  viewingUrl = signal<string | null>(null);

  private previewUrls = new Map<File, string>();

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

  ngOnDestroy(): void {
    this.previewUrls.forEach(url => URL.revokeObjectURL(url));
    const viewing = this.viewingUrl();
    if (viewing) URL.revokeObjectURL(viewing);
  }

  onFilesSelected(files: File[]): void {
    this.selectedFiles = [...this.selectedFiles, ...files];
    for (const file of files) {
      if (file.type.startsWith('image/')) this.previewUrls.set(file, URL.createObjectURL(file));
    }
    this.uploadError.set('');
  }

  previewUrl(file: File): string | null {
    return this.previewUrls.get(file) ?? null;
  }

  openPreview(file: File): void {
    this.viewingFile.set(file);
    this.viewingUrl.set(this.previewUrls.get(file) ?? URL.createObjectURL(file));
  }

  closePreview(): void {
    const file = this.viewingFile();
    const url = this.viewingUrl();
    // Only revoke if it wasn't already cached as a thumbnail - that one stays alive for the row.
    if (url && file && this.previewUrls.get(file) !== url) URL.revokeObjectURL(url);
    this.viewingFile.set(null);
    this.viewingUrl.set(null);
  }

  removeFile(file: File): void {
    this.selectedFiles = this.selectedFiles.filter(f => f !== file);
    const url = this.previewUrls.get(file);
    if (url) {
      URL.revokeObjectURL(url);
      this.previewUrls.delete(file);
    }
  }

  startRename(file: File): void {
    this.renameValue = file.name;
    this.renamingFile.set(file);
  }

  /** Renamed by swapping in a new File with the desired name - the backend takes the multipart file's own name when no separate display-name field is sent, so this needs no API change. */
  confirmRename(file: File): void {
    const trimmed = this.renameValue.trim();
    if (!trimmed) return;
    const renamed = new File([file], trimmed, { type: file.type });
    const index = this.selectedFiles.indexOf(file);
    if (index !== -1) this.selectedFiles = [...this.selectedFiles.slice(0, index), renamed, ...this.selectedFiles.slice(index + 1)];

    const url = this.previewUrls.get(file);
    if (url) {
      this.previewUrls.delete(file);
      this.previewUrls.set(renamed, url);
    }
    this.renamingFile.set(null);
  }

  submit(): void {
    if (this.selectedFiles.length === 0) return;
    this.uploadError.set('');
    this.uploading.set(true);
    this.linkService.upload(this.token, this.selectedFiles).subscribe({
      next: () => {
        this.uploading.set(false);
        this.uploaded.set(true);
      },
      error: (err) => {
        this.uploading.set(false);
        this.uploadError.set(err.error?.message ?? 'Could not upload file(s).');
      }
    });
  }
}
