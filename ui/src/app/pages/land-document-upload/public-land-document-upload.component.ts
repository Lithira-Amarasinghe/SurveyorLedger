import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { LandDocumentRequestLinkService, LandDocumentRequestLinkPreview } from '../../core/land-document-request-link.service';

/** Land counterpart to PublicDocumentUploadComponent - mirrors it exactly, one field renamed (jobTitle -> landAddressLine). */
@Component({
  selector: 'app-public-land-document-upload',
  standalone: true,
  imports: [CommonModule, FormsModule],
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
            <p class="text-sm text-neutral-600 mt-xs">Thank you - the file has been received.</p>
          } @else {
            <h1 class="text-lg font-semibold text-neutral-900">{{ p.title }}</h1>
            @if (p.landAddressLine || p.workspaceName) {
              <p class="text-xs text-neutral-500 mt-xs">{{ p.workspaceName }} · {{ p.landAddressLine }}</p>
            }
            @if (p.description) {
              <p class="text-sm text-neutral-600 mt-sm">{{ p.description }}</p>
            }

            <input
              #fileInput
              class="input-field text-sm mt-lg"
              type="file"
              (change)="onFileSelected(fileInput.files)"
            />
            @if (selectedFile) {
              <input class="input-field text-sm mt-sm" placeholder="File name" [(ngModel)]="fileNameDraft" />
            }
            @if (uploadError()) {
              <p class="text-sm text-primary-500 mt-sm">{{ uploadError() }}</p>
            }
            <button type="button" class="btn-primary w-full mt-lg" [disabled]="!selectedFile || uploading()" (click)="submit()">
              {{ uploading() ? 'Uploading…' : 'Upload' }}
            </button>
          }
        }
      </div>
    </div>
  `
})
export class PublicLandDocumentUploadComponent implements OnInit {
  token = '';
  loading = signal(true);
  loadError = signal('');
  preview = signal<LandDocumentRequestLinkPreview | null>(null);
  selectedFile: File | null = null;
  fileNameDraft = '';
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

  onFileSelected(files: FileList | null): void {
    this.selectedFile = files?.item(0) ?? null;
    this.fileNameDraft = this.selectedFile?.name ?? '';
    this.uploadError.set('');
  }

  submit(): void {
    if (!this.selectedFile) return;
    this.uploadError.set('');
    this.uploading.set(true);
    this.linkService.upload(this.token, this.selectedFile, this.fileNameDraft.trim()).subscribe({
      next: () => {
        this.uploading.set(false);
        this.uploaded.set(true);
      },
      error: (err) => {
        this.uploading.set(false);
        this.uploadError.set(err.error?.message ?? 'Could not upload file.');
      }
    });
  }
}
