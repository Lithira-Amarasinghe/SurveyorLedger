import { Component, EventEmitter, Input, OnChanges, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Invoice, InvoiceService, Payment } from '../../core/billing.service';
import { StatusBadgeComponent } from '../status-badge/status-badge.component';
import { RecordPaymentModalComponent } from '../../pages/billing/invoices/record-payment-modal/record-payment-modal.component';

const SEGMENT_COLORS = ['bg-green-500', 'bg-teal-500', 'bg-blue-500', 'bg-indigo-500', 'bg-purple-500'];
const METHOD_LABELS: Record<string, string> = { Cash: 'Cash', BankTransfer: 'Bank transfer', Cheque: 'Cheque' };

/** Segmented payment bar + chronological ledger + record action, shared between the
 * invoice edit page and a job's financial summary so both read the same paid history
 * the same way instead of each re-deriving it. */
@Component({
  selector: 'app-payments-panel',
  standalone: true,
  imports: [CommonModule, StatusBadgeComponent, RecordPaymentModalComponent],
  template: `
    <div class="card">
      <div class="flex items-center justify-between mb-md">
        <div class="flex items-center gap-sm">
          <h2 class="text-sm font-semibold text-neutral-900">Payments</h2>
          <app-status-badge [status]="invoice.status" />
        </div>
        @if (invoice.status !== 'Cancelled' && invoice.balance > 0) {
          <button type="button" class="btn-primary text-xs" (click)="showRecordModal.set(true)">Record payment</button>
        }
      </div>

      <div class="h-2 rounded-full bg-neutral-100 overflow-hidden flex" [title]="barTitle()">
        @for (p of payments(); track p.paymentId) {
          <span
            class="h-full"
            [class]="segmentColor($index)"
            [style.width.%]="segmentWidth(p)"
          ></span>
        }
        @if (invoice.balance > 0) {
          <span class="h-full bg-neutral-200" [style.width.%]="(invoice.balance / invoice.total) * 100"></span>
        }
      </div>

      <div class="flex items-baseline justify-between mt-sm text-sm">
        <span class="text-neutral-600">Paid <span class="font-semibold text-neutral-900">{{ invoice.amountPaid | number: '1.2-2' }}</span> of {{ invoice.total | number: '1.2-2' }}</span>
        @if (invoice.balance > 0) {
          <span class="text-xs" [class.text-red-600]="invoice.isOverdue" [class.text-neutral-500]="!invoice.isOverdue">
            Balance {{ invoice.balance | number: '1.2-2' }}@if (invoice.isOverdue) { &middot; {{ invoice.daysOverdue }}d overdue }
          </span>
        } @else {
          <span class="text-xs text-green-700">Fully paid</span>
        }
      </div>

      @if (loading()) {
        <p class="text-xs text-neutral-500 mt-md">Loading payment history…</p>
      } @else if (payments().length === 0) {
        <p class="text-xs text-neutral-500 mt-md">No payments recorded yet.</p>
      } @else {
        <div class="mt-md space-y-xs">
          @for (p of payments(); track p.paymentId; let i = $index) {
            <div class="flex items-center justify-between gap-sm px-md py-sm rounded bg-neutral-50 text-sm">
              <div class="flex items-center gap-sm min-w-0">
                <span class="w-2 h-2 rounded-full flex-shrink-0" [class]="segmentColor(i)"></span>
                <div class="min-w-0">
                  <span class="text-neutral-900">{{ p.receivedAt | date: 'mediumDate' }}</span>
                  <span class="text-xs text-neutral-500 ml-xs">{{ methodLabel(p.method) }}@if (p.referenceNumber) { &middot; ref {{ p.referenceNumber }} }</span>
                </div>
              </div>
              <div class="flex items-center gap-sm flex-shrink-0">
                @if (p.hasProofFile) {
                  <span class="text-xs px-sm py-2xs rounded bg-neutral-100 text-neutral-500">Proof attached</span>
                }
                <span class="font-medium text-neutral-900">{{ p.amount | number: '1.2-2' }}</span>
              </div>
            </div>
          }
        </div>
      }
    </div>

    @if (showRecordModal()) {
      <app-record-payment-modal
        [workspaceId]="workspaceId"
        [invoice]="invoice"
        (cancel)="showRecordModal.set(false)"
        (recorded)="onRecorded()"
      />
    }
  `
})
export class PaymentsPanelComponent implements OnChanges {
  @Input({ required: true }) workspaceId = '';
  @Input({ required: true }) invoice!: Invoice;
  @Output() invoiceUpdated = new EventEmitter<void>();

  payments = signal<Payment[]>([]);
  loading = signal(false);
  showRecordModal = signal(false);

  constructor(private invoiceService: InvoiceService) {}

  ngOnChanges(): void {
    if (this.invoice) this.fetch();
  }

  private fetch(): void {
    this.loading.set(true);
    this.invoiceService.getPayments(this.workspaceId, this.invoice.invoiceId).subscribe({
      next: payments => {
        this.payments.set([...payments].sort((a, b) => a.receivedAt.localeCompare(b.receivedAt)));
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  segmentWidth(p: Payment): number {
    return this.invoice.total > 0 ? (p.amount / this.invoice.total) * 100 : 0;
  }

  segmentColor(index: number): string {
    return SEGMENT_COLORS[index % SEGMENT_COLORS.length];
  }

  methodLabel(method: string): string {
    return METHOD_LABELS[method] ?? method;
  }

  barTitle(): string {
    return `${this.payments().length} payment(s) recorded`;
  }

  onRecorded(): void {
    this.showRecordModal.set(false);
    this.fetch();
    this.invoiceUpdated.emit();
  }
}
