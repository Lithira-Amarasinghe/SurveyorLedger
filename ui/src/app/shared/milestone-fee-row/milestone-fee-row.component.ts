import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Milestone, MilestonePaymentStatus } from '../../core/milestone.service';

/** Title + segmented fee bar (paid green / invoiced-unpaid blue / quoted amber / unbilled gray)
 * for one milestone row. Shared across every place a milestone gets picked from a list -
 * the expense milestone picker and the invoice/quotation line-item source picker - so the
 * same milestone reads the same way everywhere instead of each screen inventing its own
 * summary line. Purely presentational; the parent list owns the click/select behavior. */
@Component({
  selector: 'app-milestone-fee-row',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="min-w-0 w-full">
      <div class="flex items-center justify-between gap-sm">
        <span class="text-sm text-neutral-900 truncate">{{ milestone.title }}</span>
        <span class="text-xs text-neutral-500 flex-shrink-0">{{ rightLabel() }}</span>
      </div>
      @if (milestone.amount) {
        <div class="h-1.5 rounded-full bg-neutral-200 overflow-hidden flex mt-2xs">
          <span class="h-full bg-green-500" [style.width.%]="pct(status?.paidAmount ?? 0)"></span>
          <span class="h-full bg-blue-400" [style.width.%]="pct((status?.invoicedAmount ?? 0) - (status?.paidAmount ?? 0))"></span>
          <span class="h-full bg-amber-300" [style.width.%]="pct(status?.quotedAmount ?? 0)"></span>
        </div>
      }
    </div>
  `
})
export class MilestoneFeeRowComponent {
  @Input({ required: true }) milestone!: Milestone;
  @Input() status: MilestonePaymentStatus | null = null;
  /** Overrides the right-side label - e.g. a line-item picker showing "remaining after
   * in-progress lines" instead of the raw fee. */
  @Input() rightLabelOverride: string | null = null;

  pct(amount: number): number {
    const fee = this.milestone.amount;
    if (!fee) return 0;
    return Math.max(0, Math.min(100, (amount / fee) * 100));
  }

  rightLabel(): string {
    if (this.rightLabelOverride !== null) return this.rightLabelOverride;
    if (this.milestone.amount == null) return 'no fee set';
    return `${this.milestone.amount.toFixed(2)} fee`;
  }
}
