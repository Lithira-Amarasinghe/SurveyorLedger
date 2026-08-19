import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';

/** The "+ Upload" trigger, one place instead of three ad hoc <label><input type="file"> blocks (Job, Land, Survey/Deed). Multi-file; each selected file is emitted separately so the caller can upload independently (matches the existing "each file uploads independently" reasoning - deeds/surveys often have multiple scanned pages, no batch-upload endpoint). */
@Component({
  selector: 'app-document-upload-button',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <label class="inline-flex items-center gap-xs text-sm text-primary-600 hover:text-primary-700 cursor-pointer">
      <app-icon name="upload" />
      {{ label }}
      <input type="file" [multiple]="multiple" accept=".pdf,.doc,.docx,.xls,.xlsx,.jpg,.jpeg,.png" class="hidden" (change)="onFilesSelected($event)" />
    </label>
  `
})
export class DocumentUploadButtonComponent {
  @Input() label = 'Upload document';
  @Input() multiple = true;
  @Output() filesSelected = new EventEmitter<File[]>();

  onFilesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = input.files;
    if (files && files.length > 0) {
      this.filesSelected.emit(Array.from(files));
    }
    input.value = '';
  }
}
