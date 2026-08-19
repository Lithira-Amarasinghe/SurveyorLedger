import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LandPhoto } from '../../core/land.service';
import { IconComponent } from '../icon/icon.component';

/**
 * Thumbnail grid with upload, click-to-preview, download, rename, and confirm-before-delete
 * - no HTTP inside the component, same "picker owns no save logic" pattern as
 * LandLocationPickerComponent. Callers fetch photo bytes (auth-header-gated) and pass
 * object-URLs in via photoUrls; view/download/rename are emitted for the caller to fulfil.
 * Every tile has a fixed-height caption row (truncated, full name in the title attr) so
 * long filenames never push the icon row out of alignment with neighboring tiles.
 */
@Component({
  selector: 'app-photo-grid',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    <div class="flex flex-wrap gap-sm">
      @for (photo of photos; track photo.photoId) {
        <div class="w-28">
          <button
            type="button"
            class="relative block w-28 h-28 rounded-md overflow-hidden border border-neutral-200 bg-neutral-100"
            (click)="view.emit(photo)"
          >
            @if (photoUrls[photo.photoId]) {
              <img [src]="photoUrls[photo.photoId]" [alt]="photo.fileName" class="w-full h-full object-cover" />
            }
          </button>
          @if (renamingId() === photo.photoId) {
            <div class="mt-xs space-y-xs">
              <input class="input-field text-xs px-xs py-xs" [(ngModel)]="renameValue" (keydown.enter)="confirmRename(photo.photoId)" />
              <div class="flex gap-sm text-xs">
                <button type="button" class="text-primary-600 font-medium" (click)="confirmRename(photo.photoId)">Save</button>
                <button type="button" class="text-neutral-500" (click)="renamingId.set(null)">Cancel</button>
              </div>
            </div>
          } @else if (confirmingDeleteId() === photo.photoId) {
            <div class="mt-xs text-xs text-neutral-600 h-8">
              Delete?
              <button type="button" class="text-primary-500 font-medium ml-xs" (click)="confirmDelete(photo.photoId)">Yes</button>
              <button type="button" class="text-neutral-500 ml-xs" (click)="confirmingDeleteId.set(null)">No</button>
            </div>
          } @else {
            <p class="text-xs text-neutral-700 truncate w-28 h-4 mt-xs" [title]="photo.fileName">{{ photo.fileName }}</p>
            <div class="flex items-center justify-center gap-xs h-6">
              <button type="button" class="icon-btn" title="Download" (click)="download.emit(photo)"><app-icon name="download" /></button>
              @if (!readonly) {
                <button type="button" class="icon-btn" title="Rename" (click)="startRename(photo)"><app-icon name="rename" /></button>
                <button type="button" class="icon-btn text-primary-500" title="Delete" (click)="confirmingDeleteId.set(photo.photoId)"><app-icon name="delete" /></button>
              }
            </div>
          }
        </div>
      }
      @if (!readonly) {
        <label class="w-28 h-28 rounded-md border-2 border-dashed border-neutral-300 flex items-center justify-center text-xs text-neutral-500 cursor-pointer hover:bg-neutral-50">
          + Add
          <input type="file" accept="image/jpeg,image/png" class="hidden" (change)="onFileSelected($event)" />
        </label>
      }
    </div>
  `,
  styles: [`.icon-btn { display: flex; align-items: center; justify-content: center; width: 1.5rem; height: 1.5rem; border-radius: 0.25rem; color: var(--color-neutral-500, #737373); } .icon-btn:hover { background: var(--color-neutral-100, #f5f5f5); color: var(--color-primary-600, #0284c7); }`]
})
export class PhotoGridComponent {
  @Input() photos: LandPhoto[] = [];
  @Input() readonly = false;
  @Input() photoUrls: Record<string, string> = {};
  @Output() upload = new EventEmitter<File>();
  @Output() remove = new EventEmitter<string>();
  @Output() view = new EventEmitter<LandPhoto>();
  @Output() download = new EventEmitter<LandPhoto>();
  @Output() rename = new EventEmitter<{ photoId: string; fileName: string }>();

  confirmingDeleteId = signal<string | null>(null);
  renamingId = signal<string | null>(null);
  renameValue = '';

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.upload.emit(file);
    input.value = '';
  }

  confirmDelete(photoId: string): void {
    this.remove.emit(photoId);
    this.confirmingDeleteId.set(null);
  }

  startRename(photo: LandPhoto): void {
    this.renameValue = photo.fileName;
    this.renamingId.set(photo.photoId);
  }

  confirmRename(photoId: string): void {
    if (!this.renameValue.trim()) return;
    this.rename.emit({ photoId, fileName: this.renameValue.trim() });
    this.renamingId.set(null);
  }
}
