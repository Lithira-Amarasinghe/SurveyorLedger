import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';

/** Minimal shape needed to render a preview - both job Document and land OwnedDocument satisfy it. */
export interface PreviewableDocument {
  fileName: string;
  contentType: string;
}

@Component({
  selector: 'app-document-viewer-modal',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="close()">
      <div class="card w-full max-w-2xl" (click)="$event.stopPropagation()">
        <div class="flex items-center justify-between mb-md">
          <h2 class="text-sm font-semibold text-neutral-900 truncate">{{ document.fileName }}</h2>
          <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" (click)="close()">Close</button>
        </div>

        @if (isImage()) {
          <img [src]="safeUrl()" class="max-w-full max-h-[70vh] mx-auto" [alt]="document.fileName" />
        } @else if (isPdf()) {
          <iframe [src]="safeUrl()" class="w-full h-[70vh] border-0"></iframe>
        } @else {
          <div class="text-center py-lg">
            <p class="text-sm text-neutral-600 mb-md">Preview isn't available for this file type.</p>
            <a [href]="blobUrl" [download]="document.fileName" class="btn-primary text-xs">Download</a>
          </div>
        }
      </div>
    </div>
  `
})
export class DocumentViewerModalComponent {
  @Input() document!: PreviewableDocument;
  @Input() blobUrl!: string;
  @Output() closed = new EventEmitter<void>();

  constructor(private sanitizer: DomSanitizer) {}

  isImage(): boolean {
    return this.document.contentType.startsWith('image/');
  }

  isPdf(): boolean {
    return this.document.contentType === 'application/pdf';
  }

  safeUrl(): SafeResourceUrl {
    return this.sanitizer.bypassSecurityTrustResourceUrl(this.blobUrl);
  }

  close(): void {
    this.closed.emit();
  }
}
