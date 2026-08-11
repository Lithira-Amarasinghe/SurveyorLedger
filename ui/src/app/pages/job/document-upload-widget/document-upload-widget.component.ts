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
