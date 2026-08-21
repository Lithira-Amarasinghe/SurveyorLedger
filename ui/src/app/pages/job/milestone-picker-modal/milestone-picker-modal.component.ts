import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Milestone, MilestonePaymentStatus } from '../../../core/milestone.service';
import { MilestoneFeeRowComponent } from '../../../shared/milestone-fee-row/milestone-fee-row.component';

/** Same list-row pattern as the milestone tab in billing-source-picker (invoice/quotation
 * line items) - both reuse MilestoneFeeRowComponent for the title + fee bar, so picking a
 * milestone for an expense feels the same as picking one for a billing line. */
@Component({
  selector: 'app-milestone-picker-modal',
  standalone: true,
  imports: [CommonModule, MilestoneFeeRowComponent],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-md max-h-[80vh] flex flex-col" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900 mb-md">Select milestone</h2>

        <div class="flex-1 overflow-y-auto space-y-xs">
          <button
            type="button"
            class="w-full flex items-center justify-between px-md py-sm rounded bg-neutral-50 hover:bg-neutral-100 text-left"
            (click)="select.emit(null)"
          >
            <span class="text-sm text-neutral-600">No milestone — job-level expense</span>
          </button>
          @for (m of milestones; track m.milestoneId) {
            <button
              type="button"
              class="w-full flex items-center px-md py-sm rounded bg-neutral-50 hover:bg-neutral-100 text-left"
              (click)="select.emit(m)"
            >
              <app-milestone-fee-row [milestone]="m" [status]="paymentStatuses[m.milestoneId] ?? null" />
            </button>
          }
        </div>

        <div class="flex justify-end pt-md">
          <button type="button" class="btn-secondary text-xs" (click)="cancel.emit()">Close</button>
        </div>
      </div>
    </div>
  `
})
export class MilestonePickerModalComponent {
  @Input() milestones: Milestone[] = [];
  @Input() paymentStatuses: Record<string, MilestonePaymentStatus> = {};
  @Output() cancel = new EventEmitter<void>();
  @Output() select = new EventEmitter<Milestone | null>();
}
