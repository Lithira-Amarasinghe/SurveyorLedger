import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { LandDocumentRequestLinkService, LandDocumentRequestLinkPreview } from '../../core/land-document-request-link.service';
import { DocumentUploadButtonComponent } from '../../shared/document-upload-button/document-upload-button.component';
import { IconComponent } from '../../shared/icon/icon.component';

/** Land counterpart to PublicDocumentUploadComponent - mirrors it exactly, one field renamed (jobTitle -> landAddressLine). */
@Component({
  selector: 'app-public-land-document-upload',
  standalone: true,
  imports: [CommonModule, DocumentUploadButtonComponent, IconComponent],
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
            @if (p.landAddressLine || p.workspaceName) {
              <p class="text-xs text-neutral-500 mt-xs">{{ p.workspaceName }} · {{ p.landAddressLine }}</p>
            }
            @if (p.description) {
              <p class="text-sm text-neutral-600 mt-sm">{{ p.description }}</p>
            }

            <!-- Same row look as every other document list in the app - one file or several, shown the same way. -->
            @if (selectedFiles.length > 0) {
              <div class="space-y-xs mt-lg">
                @for (file of selectedFiles; track file.name + file.size) {
                  <div class="flex items-center gap-sm px-md py-sm rounded bg-neutral-50 text-sm">
                    <span class="text-neutral-900 truncate flex-1">{{ file.name }}</span>
                    <button type="button" class="icon-btn text-primary-500" title="Remove" (click)="removeFile(file)"><app-icon name="delete" /></button>
                  </div>
                }
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
  `,
  styles: [`.icon-btn { display: flex; align-items: center; justify-content: center; width: 1.75rem; height: 1.75rem; border-radius: 0.25rem; color: var(--color-neutral-500, #737373); } .icon-btn:hover { background: var(--color-neutral-100, #f5f5f5); color: var(--color-primary-600, #0284c7); }`]
})
export class PublicLandDocumentUploadComponent implements OnInit {
  token = '';
  loading = signal(true);
  loadError = signal('');
  preview = signal<LandDocumentRequestLinkPreview | null>(null);
  selectedFiles: File[] = [];
  uploading = signal(false);
  uploadError = signal('');
  uploaded = signal(false);

  constructor(private route: ActivatedRoute, private linkService: LandDocumentRequestLinkService) {}

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

  onFilesSelected(files: File[]): void {
    this.selectedFiles = [...this.selectedFiles, ...files];
    this.uploadError.set('');
  }

  removeFile(file: File): void {
    this.selectedFiles = this.selectedFiles.filter(f => f !== file);
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
