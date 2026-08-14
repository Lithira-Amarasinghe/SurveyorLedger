import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LandPhoto } from '../../core/land.service';

/**
 * Thumbnail grid with an upload input and per-photo delete - no HTTP inside the
 * component, same "picker owns no save logic" pattern as LandLocationPickerComponent.
 * Callers fetch photo bytes (auth-header-gated) and pass object-URLs in via photoUrls.
 */
@Component({
  selector: 'app-photo-grid',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="flex flex-wrap gap-sm">
      @for (photo of photos; track photo.photoId) {
        <div class="relative w-24 h-24 rounded-md overflow-hidden border border-neutral-200 bg-neutral-100">
          @if (photoUrls[photo.photoId]) {
            <img [src]="photoUrls[photo.photoId]" [alt]="photo.fileName" class="w-full h-full object-cover" />
          }
          @if (!readonly) {
            <button
              type="button"
              class="absolute top-0 right-0 bg-black/60 text-white text-xs w-6 h-6 leading-6 text-center"
              [attr.aria-label]="'Delete ' + photo.fileName"
              (click)="remove.emit(photo.photoId)"
            >
              ×
            </button>
          }
        </div>
      }
      @if (!readonly) {
        <label class="w-24 h-24 rounded-md border-2 border-dashed border-neutral-300 flex items-center justify-center text-xs text-neutral-500 cursor-pointer hover:bg-neutral-50">
          + Add
          <input type="file" accept="image/jpeg,image/png" class="hidden" (change)="onFileSelected($event)" />
        </label>
      }
    </div>
  `
})
export class PhotoGridComponent {
  @Input() photos: LandPhoto[] = [];
  @Input() readonly = false;
  @Input() photoUrls: Record<string, string> = {};
  @Output() upload = new EventEmitter<File>();
  @Output() remove = new EventEmitter<string>();

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.upload.emit(file);
    input.value = '';
  }
}
