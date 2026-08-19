import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '../icon/icon.component';

/**
 * One row, owner-agnostic - a plain uploaded document or a document request (pending,
 * reopened, or fulfilled-and-merged-with-its-document). `ownerKind`/`ownerId`/`subId`
 * are enough for the caller to know which of its own service methods to call when an
 * output fires; this component owns no HTTP itself (same "picker owns no save logic"
 * pattern as every other shared component here).
 */
export interface DocRow {
  key: string;
  ownerKind: 'job' | 'land' | 'landSurvey' | 'landDeed';
  ownerId: string;
  subId?: string;
  documentId: string | null;
  fileName: string | null;
  contentType: string | null;
  uploadedByName: string | null;
  createdAt: string | null;
  category?: string;
  visibility?: string;
  requestId?: string | null;
  requestTitle?: string | null;
  requestStatus?: string | null;
  requestDescription?: string | null;
  hasActiveShareLink?: boolean;
  /** e.g. a linked land's address line - lets Job's Documents card mix in read-only rows sourced from an attached Land. */
  sourceLabel?: string | null;
  /** Per-row override - true for rows sourced from elsewhere (a linked Land inside Job's card): view/download stay, rename/remove/fulfill hide. */
  readonly?: boolean;
}

@Component({
  selector: 'app-document-list',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    <div class="space-y-xs">
      @for (row of rows; track row.key) {
        <div class="px-md py-sm rounded bg-neutral-50 text-sm">
          <div class="flex items-center gap-sm">
            @if (row.documentId && isPreviewable(row.contentType) && previewUrls[row.documentId]) {
              <img [src]="previewUrls[row.documentId]" class="w-8 h-8 rounded object-cover flex-shrink-0" [alt]="row.fileName" />
            } @else if (row.documentId) {
              <span class="w-8 h-8 rounded bg-neutral-200 flex items-center justify-center flex-shrink-0 text-neutral-500">
                <app-icon name="view" />
              </span>
            }
            <div class="min-w-0 flex-1">
              @if (row.fileName) {
                @if (renamingKey() === row.key) {
                  <input class="input-field text-xs px-xs py-xs" [(ngModel)]="renameValue" (keydown.enter)="confirmRename(row)" (click)="$event.stopPropagation()" />
                } @else {
                  <button type="button" class="text-primary-600 hover:text-primary-700 truncate text-left block w-full" [title]="row.fileName" (click)="view.emit(row)">
                    {{ row.fileName }}
                  </button>
                }
                <span class="text-neutral-500 block text-xs truncate">
                  @if (row.sourceLabel) {
                    <span class="text-neutral-400">{{ row.sourceLabel }} · </span>
                  }
                  {{ row.uploadedByName }} · {{ row.createdAt | date: 'mediumDate' }}
                  @if (row.requestTitle) {
                    <span class="text-neutral-400"> · via request: {{ row.requestTitle }}</span>
                  }
                </span>
                @if (row.visibility && !row.readonly) {
                  <button type="button" class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600 hover:bg-neutral-200 mt-xs" (click)="toggleVisibility.emit(row)">
                    {{ row.visibility }}
                  </button>
                }
              } @else {
                <span class="text-neutral-900">{{ row.requestTitle }}</span>
                <span class="text-xs px-sm py-xs rounded bg-amber-100 text-amber-700 ml-xs">{{ row.requestStatus }}</span>
                @if (row.requestDescription) {
                  <span class="text-neutral-500 block text-xs">{{ row.requestDescription }}</span>
                }
              }
            </div>
            <div class="flex items-center gap-xs flex-shrink-0">
              @if (confirmingRemoveKey() === row.key) {
                <span class="text-xs text-neutral-600 whitespace-nowrap">
                  Remove?
                  <button type="button" class="text-primary-500 font-medium ml-xs" (click)="confirmRemove(row)">Yes</button>
                  <button type="button" class="text-neutral-500 ml-xs" (click)="confirmingRemoveKey.set(null)">No</button>
                </span>
              } @else if (renamingKey() === row.key) {
                <button type="button" class="text-xs text-primary-600 font-medium" (click)="confirmRename(row)">Save</button>
                <button type="button" class="text-xs text-neutral-500" (click)="renamingKey.set(null)">Cancel</button>
              } @else {
                @if (row.fileName) {
                  <button type="button" class="icon-btn" title="Download" (click)="download.emit(row)"><app-icon name="download" /></button>
                  @if (!row.readonly) {
                    @if (allowRename) {
                      <button type="button" class="icon-btn" title="Rename" (click)="startRename(row)"><app-icon name="rename" /></button>
                    }
                    @if (row.requestId) {
                      <button type="button" class="icon-btn" title="Reopen request" (click)="requestReopen.emit(row)"><app-icon name="reopen" /></button>
                    } @else {
                      <button type="button" class="icon-btn text-primary-500" title="Remove" (click)="confirmingRemoveKey.set(row.key)"><app-icon name="delete" /></button>
                    }
                  }
                } @else if (!row.readonly) {
                  <label class="icon-btn cursor-pointer" title="Upload">
                    <app-icon name="upload" />
                    <input type="file" class="hidden" (change)="onFulfillFileSelected(row, $event)" />
                  </label>
                  <button type="button" class="icon-btn" title="Copy share link" (click)="requestCopyShareLink.emit(row)"><app-icon name="link" /></button>
                  <button type="button" class="icon-btn text-primary-500" title="Cancel request" (click)="requestCancel.emit(row)"><app-icon name="delete" /></button>
                }
              }
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`.icon-btn { display: flex; align-items: center; justify-content: center; width: 1.75rem; height: 1.75rem; border-radius: 0.25rem; color: var(--color-neutral-500, #737373); } .icon-btn:hover { background: var(--color-neutral-100, #f5f5f5); color: var(--color-primary-600, #0284c7); }`]
})
export class DocumentListComponent {
  @Input() rows: DocRow[] = [];
  /** Object URLs for inline preview thumbnails, keyed by documentId - caller fetches/caches blobs, this component only renders what it's given. */
  @Input() previewUrls: Record<string, string> = {};
  /** Job documents have no rename endpoint (unlike Land's) - hide the action rather than wire a no-op. */
  @Input() allowRename = true;

  @Output() view = new EventEmitter<DocRow>();
  @Output() download = new EventEmitter<DocRow>();
  @Output() rename = new EventEmitter<{ row: DocRow; fileName: string }>();
  @Output() remove = new EventEmitter<DocRow>();
  @Output() toggleVisibility = new EventEmitter<DocRow>();
  @Output() requestFulfill = new EventEmitter<{ row: DocRow; file: File }>();
  @Output() requestReopen = new EventEmitter<DocRow>();
  @Output() requestCancel = new EventEmitter<DocRow>();
  @Output() requestCopyShareLink = new EventEmitter<DocRow>();

  confirmingRemoveKey = signal<string | null>(null);
  renamingKey = signal<string | null>(null);
  renameValue = '';

  isPreviewable(contentType: string | null | undefined): boolean {
    return !!contentType && contentType.startsWith('image/');
  }

  startRename(row: DocRow): void {
    this.renameValue = row.fileName ?? '';
    this.renamingKey.set(row.key);
  }

  confirmRename(row: DocRow): void {
    if (!this.renameValue.trim()) return;
    this.rename.emit({ row, fileName: this.renameValue.trim() });
    this.renamingKey.set(null);
  }

  confirmRemove(row: DocRow): void {
    this.remove.emit(row);
    this.confirmingRemoveKey.set(null);
  }

  onFulfillFileSelected(row: DocRow, event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) this.requestFulfill.emit({ row, file });
  }
}
