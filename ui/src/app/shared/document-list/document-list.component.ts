import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule, NgTemplateOutlet } from '@angular/common';
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
  ownerKind: 'job' | 'land' | 'landSurvey' | 'landDeed' | 'landPhoto';
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
  /** Rows sharing the same non-null batchId render as one collapsible group instead of separate rows - set from Document.uploadBatchId or a request's fulfilledBatchId. */
  batchId?: string | null;
}

@Component({
  selector: 'app-document-list',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent, NgTemplateOutlet],
  template: `
    <div class="space-y-xs">
      @for (group of groups; track group.batchId ?? group.rows[0].key) {
        @if (group.batchId) {
          <div class="rounded bg-neutral-50 text-sm" data-testid="group-header">
            <div class="flex items-center gap-sm px-md py-sm cursor-pointer" (click)="toggleGroup(group.batchId)">
              <div class="w-14 h-14 rounded-md bg-neutral-200 flex items-center justify-center flex-shrink-0 text-neutral-500 text-xs font-medium">
                {{ group.rows.length }} files
              </div>
              <div class="min-w-0 flex-1">
                @if (renamingGroupId() === group.batchId) {
                  <input class="input-field text-xs px-xs py-xs" [(ngModel)]="renameValue" (keydown.enter)="confirmRenameGroup(group.rows[0])" (click)="$event.stopPropagation()" />
                } @else {
                  <span class="text-neutral-900">{{ groupName(group) }}</span>
                }
                @if (group.rows[0].requestStatus) {
                  <span class="text-xs px-sm py-xs rounded bg-amber-100 text-amber-700 ml-xs">{{ group.rows[0].requestStatus }}</span>
                }
                <span class="text-neutral-500 block text-xs">{{ group.rows[0].uploadedByName }} · {{ group.rows[0].createdAt | date: 'mediumDate' }}</span>
              </div>
              <div class="flex items-center gap-xs flex-shrink-0" (click)="$event.stopPropagation()">
                @if (!group.rows[0].readonly) {
                  @if (renamingGroupId() === group.batchId) {
                    <button type="button" class="text-xs text-primary-600 font-medium" (click)="confirmRenameGroup(group.rows[0])">Save</button>
                    <button type="button" class="text-xs text-neutral-500" (click)="renamingGroupId.set(null)">Cancel</button>
                  } @else if (allowRename && !group.rows[0].requestId) {
                    <!-- Naming the group renames its first file - the group has no name of its own to store, this is the one that shows in the header. -->
                    <button type="button" class="icon-btn" title="Rename" (click)="startRenameGroup(group)"><app-icon name="rename" /></button>
                  }
                  @if (renamingGroupId() !== group.batchId) {
                    @if (group.rows[0].requestId) {
                      <!-- A request-derived group is reopened, not deleted - matches the existing single-row rule (row.requestId shows Reopen instead of Delete), just applied to the whole group instead of one doc. -->
                      <button type="button" class="icon-btn" title="Reopen request" (click)="requestReopen.emit(group.rows[0])"><app-icon name="reopen" /></button>
                    } @else if (confirmingRemoveGroupId() === group.batchId) {
                      <span class="text-xs text-neutral-600 whitespace-nowrap">
                        Remove all?
                        <button type="button" class="text-primary-500 font-medium ml-xs" (click)="confirmRemoveGroup(group.batchId)">Yes</button>
                        <button type="button" class="text-neutral-500 ml-xs" (click)="confirmingRemoveGroupId.set(null)">No</button>
                      </span>
                    } @else {
                      <button type="button" class="icon-btn text-primary-500" title="Remove all" (click)="confirmingRemoveGroupId.set(group.batchId)"><app-icon name="delete" /></button>
                    }
                  }
                }
                <button type="button" class="icon-btn" title="Expand" (click)="toggleGroup(group.batchId)">
                  <app-icon [name]="isExpanded(group.batchId) ? 'chevronUp' : 'chevronDown'" />
                </button>
              </div>
            </div>
            @if (isExpanded(group.batchId)) {
              <div class="pl-md pb-sm space-y-xs">
                @for (row of group.rows; track row.key) {
                  <ng-container *ngTemplateOutlet="rowTpl; context: { $implicit: row }"></ng-container>
                }
              </div>
            }
          </div>
        } @else {
          <ng-container *ngTemplateOutlet="rowTpl; context: { $implicit: group.rows[0] }"></ng-container>
        }
      }
    </div>
    <ng-template #rowTpl let-row>
      <div class="px-md py-sm rounded bg-neutral-50 text-sm">
        <div class="flex items-center gap-sm">
          @if (row.documentId && isPreviewable(row.contentType) && previewUrls[row.documentId]) {
            <button type="button" class="flex-shrink-0" title="Preview" (click)="view.emit(row)">
              <img [src]="previewUrls[row.documentId]" class="w-14 h-14 rounded-md object-cover border border-neutral-200" [alt]="row.fileName" />
            </button>
          } @else if (row.documentId) {
            <button type="button" class="w-14 h-14 rounded-md bg-neutral-200 flex items-center justify-center flex-shrink-0 text-neutral-500 border border-neutral-200" title="Preview" (click)="view.emit(row)">
              <app-icon name="view" />
            </button>
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
                  <input type="file" multiple class="hidden" (change)="onFulfillFilesSelected(row, $event)" />
                </label>
                <button type="button" class="icon-btn" title="Copy share link" (click)="requestCopyShareLink.emit(row)"><app-icon name="link" /></button>
                <button type="button" class="icon-btn text-primary-500" title="Cancel request" (click)="requestCancel.emit(row)"><app-icon name="delete" /></button>
              }
            }
          </div>
        </div>
      </div>
    </ng-template>
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
  /** Every member row of a group, emitted together so the caller can loop its existing single-remove call - no new bulk-delete endpoint. */
  @Output() removeGroup = new EventEmitter<DocRow[]>();
  @Output() toggleVisibility = new EventEmitter<DocRow>();
  @Output() requestFulfill = new EventEmitter<{ row: DocRow; files: File[] }>();
  @Output() requestReopen = new EventEmitter<DocRow>();
  @Output() requestCancel = new EventEmitter<DocRow>();
  @Output() requestCopyShareLink = new EventEmitter<DocRow>();

  confirmingRemoveKey = signal<string | null>(null);
  renamingKey = signal<string | null>(null);
  renameValue = '';

  confirmingRemoveGroupId = signal<string | null>(null);
  renamingGroupId = signal<string | null>(null);
  /** Groups default expanded (multi-file uploads should be clearly visible right away) - this tracks the exceptions the user collapsed, not the exceptions expanded. */
  collapsedGroupIds = signal<Set<string>>(new Set());

  isExpanded(batchId: string): boolean {
    return !this.collapsedGroupIds().has(batchId);
  }

  /** The group has no name of its own to store - its header shows the request's title when it came from a request, otherwise its first file's name, falling back to a plain file count. */
  groupName(group: { batchId: string | null; rows: DocRow[] }): string {
    return group.rows[0].requestTitle ?? group.rows[0].fileName ?? `${group.rows.length} files`;
  }

  startRenameGroup(group: { batchId: string | null; rows: DocRow[] }): void {
    this.renameValue = this.groupName(group);
    this.renamingGroupId.set(group.batchId);
  }

  confirmRenameGroup(firstRow: DocRow): void {
    if (!this.renameValue.trim()) return;
    this.rename.emit({ row: firstRow, fileName: this.renameValue.trim() });
    this.renamingGroupId.set(null);
  }

  /** Groups rows sharing a non-null batchId; a batch of exactly one member renders as a plain row (no group chrome) by reporting batchId: null for it. */
  get groups(): { batchId: string | null; rows: DocRow[] }[] {
    const order: (string | null)[] = [];
    const byBatch = new Map<string | null, DocRow[]>();
    for (const row of this.rows) {
      const key = row.batchId ?? row.key; // ungrouped rows are their own singleton group, keyed uniquely so they never merge with each other
      if (!byBatch.has(key)) {
        byBatch.set(key, []);
        order.push(key);
      }
      byBatch.get(key)!.push(row);
    }
    return order.map(key => ({ batchId: byBatch.get(key)!.length > 1 ? (key as string) : null, rows: byBatch.get(key)! }));
  }

  toggleGroup(batchId: string): void {
    this.collapsedGroupIds.update(current => {
      const next = new Set(current);
      if (next.has(batchId)) next.delete(batchId);
      else next.add(batchId);
      return next;
    });
  }

  confirmRemoveGroup(batchId: string): void {
    const members = this.rows.filter(r => r.batchId === batchId);
    this.removeGroup.emit(members);
    this.confirmingRemoveGroupId.set(null);
  }

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

  onFulfillFilesSelected(row: DocRow, event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = input.files ? Array.from(input.files) : [];
    input.value = '';
    if (files.length > 0) this.requestFulfill.emit({ row, files });
  }
}
