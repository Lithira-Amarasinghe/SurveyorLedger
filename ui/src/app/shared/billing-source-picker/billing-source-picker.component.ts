import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LineItem } from '../../core/billing.service';
import { Milestone } from '../../core/milestone.service';
import { QuotationLineSource } from '../line-item-editor/line-item-editor.component';

/**
 * "+ Add from..." modal - replaces the old per-line milestone/quotation dropdowns with an
 * explicit two-tab picker (Milestones / Quotations). Remaining amounts are adjusted against
 * lines already present in the in-progress form (existingItems), not just what's already
 * saved server-side, so a second bulk-add never double-adds an already-fully-added source.
 */
@Component({
  selector: 'app-billing-source-picker',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-lg max-h-[80vh] flex flex-col" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900 mb-md">Add from…</h2>

        <div class="flex gap-sm border-b border-neutral-200 mb-md">
          <button
            type="button"
            class="px-md py-sm text-sm font-medium border-b-2"
            [class.border-primary-500]="tab() === 'milestones'"
            [class.text-primary-600]="tab() === 'milestones'"
            [class.border-transparent]="tab() !== 'milestones'"
            [class.text-neutral-600]="tab() !== 'milestones'"
            (click)="tab.set('milestones')"
          >Milestones</button>
          <button
            type="button"
            class="px-md py-sm text-sm font-medium border-b-2"
            [class.border-primary-500]="tab() === 'quotations'"
            [class.text-primary-600]="tab() === 'quotations'"
            [class.border-transparent]="tab() !== 'quotations'"
            [class.text-neutral-600]="tab() !== 'quotations'"
            (click)="tab.set('quotations')"
          >Quotations</button>
        </div>

        <div class="flex-1 overflow-y-auto space-y-xs">
          @if (tab() === 'milestones') {
            @if (availableMilestones().length === 0) {
              <p class="text-sm text-neutral-500">No milestones with remaining fee.</p>
            } @else {
              <button type="button" class="text-xs text-primary-500 hover:text-primary-600 mb-sm" (click)="addAllMilestones()">
                + Add all milestones
              </button>
              @for (m of availableMilestones(); track m.milestoneId) {
                <button
                  type="button"
                  class="w-full flex items-center justify-between px-md py-sm rounded bg-neutral-50 hover:bg-neutral-100 text-left"
                  (click)="addMilestone(m)"
                >
                  <span class="text-sm text-neutral-900">{{ m.title }}</span>
                  <span class="text-xs text-neutral-600">{{ remainingFor(m) | number: '1.2-2' }} remaining</span>
                </button>
              }
            }
          } @else {
            @if (!selectedQuotationId()) {
              @if (quotationGroups().length === 0) {
                <p class="text-sm text-neutral-500">No quotations with remaining lines.</p>
              } @else {
                @for (group of quotationGroups(); track group.quotationId) {
                  <button
                    type="button"
                    class="w-full flex items-center justify-between px-md py-sm rounded bg-neutral-50 hover:bg-neutral-100 text-left"
                    (click)="selectedQuotationId.set(group.quotationId)"
                  >
                    <span class="text-sm text-neutral-900">{{ group.quotationNumber }}</span>
                    <span class="text-xs text-neutral-600">{{ group.lines.length }} line(s)</span>
                  </button>
                }
              }
            } @else {
              <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700 mb-sm" (click)="selectedQuotationId.set(null)">← Back to quotations</button>
              <button type="button" class="text-xs text-primary-500 hover:text-primary-600 mb-sm block" (click)="addAllFromSelectedQuotation()">
                + Add all lines from this quotation
              </button>
              @for (source of selectedQuotationLines(); track source.id) {
                <button
                  type="button"
                  class="w-full flex items-center justify-between px-md py-sm rounded bg-neutral-50 hover:bg-neutral-100 text-left"
                  (click)="addQuotationLine(source)"
                >
                  <span class="text-sm text-neutral-900">{{ source.description }}</span>
                  <span class="text-xs text-neutral-600">{{ remainingFor(source) | number: '1.2-2' }} remaining</span>
                </button>
              }
            }
          }
        </div>

        <div class="flex justify-end pt-md">
          <button type="button" class="btn-secondary text-xs" (click)="cancel.emit()">Close</button>
        </div>
      </div>
    </div>
  `
})
export class BillingSourcePickerComponent {
  @Input() milestones: Milestone[] = [];
  @Input() quotationLines: QuotationLineSource[] = [];
  @Input() existingItems: LineItem[] = [];
  @Output() cancel = new EventEmitter<void>();
  @Output() addLines = new EventEmitter<LineItem[]>();

  tab = signal<'milestones' | 'quotations'>('milestones');
  selectedQuotationId = signal<string | null>(null);

  private committedForMilestone(milestoneId: string): number {
    return this.existingItems
      .filter(i => i.milestoneId === milestoneId && !i.quotationLineId)
      .reduce((sum, i) => sum + i.quantity * i.unitPrice, 0);
  }

  private committedForSource(sourceId: string): number {
    return this.existingItems
      .filter(i => i.quotationLineId === sourceId)
      .reduce((sum, i) => sum + i.quantity * i.unitPrice, 0);
  }

  remainingFor(entity: Milestone | QuotationLineSource): number {
    if ('quotationId' in entity) {
      return entity.remainingAmount - this.committedForSource(entity.id);
    }
    const cap = entity.remainingAmount ?? entity.amount ?? 0;
    return cap - this.committedForMilestone(entity.milestoneId);
  }

  availableMilestones(): Milestone[] {
    return this.milestones.filter(m => this.remainingFor(m) > 0);
  }

  quotationGroups(): { quotationId: string; quotationNumber: string; lines: QuotationLineSource[] }[] {
    const byQuotation = new Map<string, { quotationId: string; quotationNumber: string; lines: QuotationLineSource[] }>();
    for (const source of this.quotationLines) {
      if (this.remainingFor(source) <= 0) continue;
      if (!byQuotation.has(source.quotationId)) {
        byQuotation.set(source.quotationId, { quotationId: source.quotationId, quotationNumber: source.quotationNumber, lines: [] });
      }
      byQuotation.get(source.quotationId)!.lines.push(source);
    }
    return [...byQuotation.values()];
  }

  selectedQuotationLines(): QuotationLineSource[] {
    return this.quotationLines.filter(s => s.quotationId === this.selectedQuotationId() && this.remainingFor(s) > 0);
  }

  addMilestone(m: Milestone): void {
    this.addLines.emit([{ description: m.title, quantity: 1, unitPrice: this.remainingFor(m), milestoneId: m.milestoneId }]);
  }

  addAllMilestones(): void {
    const lines = this.availableMilestones().map(m => ({ description: m.title, quantity: 1, unitPrice: this.remainingFor(m), milestoneId: m.milestoneId }));
    if (lines.length > 0) this.addLines.emit(lines);
  }

  addQuotationLine(source: QuotationLineSource): void {
    this.addLines.emit([{ description: source.description, quantity: 1, unitPrice: this.remainingFor(source), quotationLineId: source.id, milestoneId: source.milestoneId }]);
  }

  addAllFromSelectedQuotation(): void {
    const lines = this.selectedQuotationLines().map(source => ({
      description: source.description, quantity: 1, unitPrice: this.remainingFor(source), quotationLineId: source.id, milestoneId: source.milestoneId
    }));
    if (lines.length > 0) this.addLines.emit(lines);
  }
}
