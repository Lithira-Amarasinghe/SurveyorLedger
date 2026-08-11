import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Document, DocumentService } from '../../../core/document.service';

const CATEGORIES = ['SurveyPlan', 'LegalDocument', 'Photo', 'Other'];

@Component({
  selector: 'app-document-upload-widget',
  standalone: true,
  imports: [CommonModule, FormsModule],
  host: { style: 'display: contents' },
  template: `
    @if (expanded()) {
      <div class="border border-neutral-200 rounded-md p-md space-y-sm">
        <input
          #fileInput
          class="input-field text-sm"
          type="file"
          (change)="onFileSelected(fileInput.files)"
        />

        @if (selectedFile) {
          <input class="input-field text-sm" placeholder="File name" [(ngModel)]="fileNameDraft" />
        }

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

        <div class="flex items-center justify-end gap-sm">
          <button type="button" class="btn-secondary text-xs" (click)="collapse()">Cancel</button>
          <button
            type="button"
            class="btn-primary text-xs"
            [disabled]="!selectedFile || uploading()"
            (click)="submit()"
          >
            {{ uploading() ? 'Uploading…' : 'Upload' }}
          </button>
        </div>
      </div>
    } @else {
      <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="expanded.set(true)">
        + Upload document
      </button>
    }
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
  fileNameDraft = '';
  uploading = signal(false);
  error = signal('');
  expanded = signal(false);

  constructor(private documentService: DocumentService) {}

  onFileSelected(files: FileList | null): void {
    this.selectedFile = files?.item(0) ?? null;
    this.fileNameDraft = this.selectedFile?.name ?? '';
    this.error.set('');
  }

  submit(): void {
    if (!this.selectedFile) return;
    const effectiveVisibility = this.isClient ? 'ClientVisible' : this.visibility;

    this.error.set('');
    this.uploading.set(true);
    this.documentService.upload(this.workspaceId, this.jobId, this.selectedFile, this.category, effectiveVisibility, this.fileNameDraft.trim()).subscribe({
      next: (doc) => {
        this.added.emit(doc);
        this.collapse();
      },
      error: (err) => {
        this.uploading.set(false);
        this.error.set(err.error?.message ?? 'Could not upload document.');
      }
    });
  }

  collapse(): void {
    this.reset();
    this.expanded.set(false);
  }

  private reset(): void {
    this.selectedFile = null;
    this.fileNameDraft = '';
    this.category = 'Other';
    this.visibility = 'Internal';
    this.uploading.set(false);
    this.error.set('');
  }
}
