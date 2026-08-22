import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '../icon/icon.component';

/** A single not-yet-uploaded file, staged in a form (payment proof, expense receipt).
 * Same icon-btn row language as the persisted document list (rename/replace/remove),
 * so picking a file to attach reads the same way everywhere it happens. Renaming
 * constructs a new File with the new name so the display name is also what gets
 * uploaded, not just a cosmetic label. */
@Component({
  selector: 'app-file-picker-field',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    @if (!file()) {
      <label class="inline-flex items-center gap-xs text-sm text-primary-600 hover:text-primary-700 cursor-pointer">
        <app-icon name="upload" />
        {{ label }}
        <input type="file" [accept]="accept" class="hidden" (change)="onFileSelected($event)" />
      </label>
    } @else {
      <div class="flex items-center gap-sm px-md py-sm rounded bg-neutral-50 text-sm">
        @if (renaming()) {
          <input class="input-field text-xs px-xs py-xs flex-1" [(ngModel)]="renameValue" (keydown.enter)="confirmRename()" autofocus />
          <button type="button" class="text-xs text-primary-600 font-medium" (click)="confirmRename()">Save</button>
          <button type="button" class="text-xs text-neutral-500" (click)="renaming.set(false)">Cancel</button>
        } @else {
          <span class="truncate flex-1 text-neutral-900" [title]="file()!.name">{{ file()!.name }}</span>
          <button type="button" class="icon-btn" title="Rename" (click)="startRename()"><app-icon name="rename" /></button>
          <label class="icon-btn cursor-pointer" title="Replace">
            <app-icon name="upload" />
            <input type="file" [accept]="accept" class="hidden" (change)="onFileSelected($event)" />
          </label>
          <button type="button" class="icon-btn text-primary-500" title="Remove" (click)="clear()"><app-icon name="delete" /></button>
        }
      </div>
    }
  `,
  styles: [`.icon-btn { display: flex; align-items: center; justify-content: center; width: 1.75rem; height: 1.75rem; border-radius: 0.25rem; color: var(--color-neutral-500, #737373); } .icon-btn:hover { background: var(--color-neutral-100, #f5f5f5); color: var(--color-primary-600, #0284c7); }`]
})
export class FilePickerFieldComponent {
  @Input() label = 'Upload file';
  @Input() accept = '';
  @Output() fileChange = new EventEmitter<File | null>();

  file = signal<File | null>(null);
  renaming = signal(false);
  renameValue = '';

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const selected = input.files?.[0] ?? null;
    if (selected) {
      this.file.set(selected);
      this.fileChange.emit(selected);
    }
    input.value = '';
  }

  startRename(): void {
    this.renameValue = this.file()?.name ?? '';
    this.renaming.set(true);
  }

  confirmRename(): void {
    const current = this.file();
    if (!current || !this.renameValue.trim()) return;
    const renamed = new File([current], this.renameValue.trim(), { type: current.type });
    this.file.set(renamed);
    this.fileChange.emit(renamed);
    this.renaming.set(false);
  }

  clear(): void {
    this.file.set(null);
    this.fileChange.emit(null);
  }
}
