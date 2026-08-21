import { Component, Input, OnChanges, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Letterhead, WorkspaceService } from '../../core/workspace.service';

/** Company logo + name/address/contact block for the top of a print/PDF-style document.
 * Shared by invoice-print and quotation-print so both "legal" documents carry the same
 * identity. Renders nothing when the workspace has no letterhead set, so unconfigured
 * workspaces see the same bare title they always have. */
@Component({
  selector: 'app-letterhead-header',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (letterhead(); as lh) {
      <div class="flex items-start justify-between gap-lg pb-md mb-md border-b border-neutral-200">
        <div class="flex items-start gap-md min-w-0">
          @if (logoUrl(); as url) {
            <img [src]="url" alt="Company logo" class="h-14 w-14 object-contain flex-shrink-0" />
          }
          <div class="min-w-0">
            @if (lh.companyName) {
              <p class="text-base font-semibold text-neutral-900">{{ lh.companyName }}</p>
            }
            @if (lh.address) {
              <p class="text-xs text-neutral-600 whitespace-pre-line">{{ lh.address }}</p>
            }
            <p class="text-xs text-neutral-600">
              @if (lh.phone) { {{ lh.phone }} }
              @if (lh.phone && lh.email) { &middot; }
              @if (lh.email) { {{ lh.email }} }
            </p>
            @if (lh.registrationNumber) {
              <p class="text-xs text-neutral-500">Reg. {{ lh.registrationNumber }}</p>
            }
          </div>
        </div>
      </div>
    }
  `
})
export class LetterheadHeaderComponent implements OnChanges {
  @Input({ required: true }) workspaceId = '';

  letterhead = signal<Letterhead | null>(null);
  logoUrl = signal<string | null>(null);

  constructor(private workspaceService: WorkspaceService) {}

  ngOnChanges(): void {
    if (!this.workspaceId) return;
    this.workspaceService.getLetterhead(this.workspaceId).subscribe({
      next: lh => {
        const hasContent = !!(lh.companyName || lh.address || lh.phone || lh.email || lh.registrationNumber || lh.hasLogo);
        this.letterhead.set(hasContent ? lh : null);
        if (lh.hasLogo) {
          this.workspaceService.getLetterheadLogoBlob(this.workspaceId).subscribe({
            next: blob => this.logoUrl.set(URL.createObjectURL(blob)),
            error: () => this.logoUrl.set(null)
          });
        }
      },
      error: () => this.letterhead.set(null)
    });
  }
}
