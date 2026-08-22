import { Component, EventEmitter, Input, OnChanges, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Invoice, InvoiceService, Payment } from '../../core/billing.service';
import { StatusBadgeComponent } from '../status-badge/status-badge.component';
import { RecordPaymentModalComponent } from '../../pages/billing/invoices/record-payment-modal/record-payment-modal.component';

const SEGMENT_COLORS = ['bg-green-500', 'bg-teal-500', 'bg-blue-500', 'bg-indigo-500', 'bg-purple-500'];
const METHOD_LABELS: Record<string, string> = { Cash: 'Cash', BankTransfer: 'Bank transfer', Cheque: 'Cheque' };

/** Segmented payment bar + chronological ledger + record/refund/void actions, shared
 * between the invoice edit page and a job's financial summary so both read the same paid
 * history the same way instead of each re-deriving it. Voided rows and refunds stay
 * visible in the ledger (audit trail) but are excluded from the bar and from the "paid"
 * math, which the backend already computes net - this component just displays it. */
@Component({
  selector: 'app-payments-panel',
  standalone: true,
  imports: [CommonModule, FormsModule, StatusBadgeComponent, RecordPaymentModalComponent],
  template: `
    <div class="card">
      <div class="flex items-center justify-between mb-md">
        <div class="flex items-center gap-sm">
          <h2 class="text-sm font-semibold text-neutral-900">Payments</h2>
          <app-status-badge [status]="invoice.status" />
        </div>
        <div class="flex gap-sm">
          @if (invoice.amountPaid > 0) {
            <button type="button" class="btn-secondary text-xs" (click)="modalMode.set('refund'); showRecordModal.set(true)">Refund</button>
          }
          @if (invoice.status !== 'Cancelled' && invoice.balance > 0) {
            <button type="button" class="btn-primary text-xs" (click)="modalMode.set('payment'); showRecordModal.set(true)">Record payment</button>
          }
        </div>
      </div>

      <div class="h-2 rounded-full bg-neutral-100 overflow-hidden flex" [title]="barTitle()">
        @for (p of activePayments(); track p.paymentId) {
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
            <div class="flex items-center justify-between gap-sm px-md py-sm rounded bg-neutral-50 text-sm" [class.opacity-60]="p.isVoided">
              <div class="flex items-center gap-sm min-w-0">
                <span class="w-2 h-2 rounded-full flex-shrink-0" [class]="p.isVoided ? 'bg-neutral-300' : p.isRefund ? 'bg-red-400' : segmentColor(i)"></span>
                <div class="min-w-0">
                  <span class="text-neutral-900" [class.line-through]="p.isVoided">{{ p.receivedAt | date: 'mediumDate' }}</span>
                  <span class="text-xs text-neutral-500 ml-xs">
                    {{ methodLabel(p.method) }}@if (p.referenceNumber) { &middot; ref {{ p.referenceNumber }} }@if (p.recordedByName) { &middot; by {{ p.recordedByName }} }
                  </span>
                  @if (p.isVoided) {
                    <span class="block text-xs text-neutral-500">Voided {{ p.voidedAt | date: 'mediumDate' }}@if (p.voidReason) { &middot; {{ p.voidReason }} }</span>
                  }
                </div>
              </div>
              <div class="flex items-center gap-sm flex-shrink-0">
                @if (p.isRefund) {
                  <span class="text-xs px-sm py-2xs rounded bg-red-50 text-red-600">Refund</span>
                }
                @if (p.hasProofFile) {
                  <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="viewProof(p)">Proof</button>
                }
                <span class="font-medium" [class.line-through]="p.isVoided" [class.text-red-600]="p.isRefund && !p.isVoided" [class.text-neutral-900]="!p.isRefund || p.isVoided">
                  {{ p.isRefund ? '-' : '' }}{{ p.amount | number: '1.2-2' }}
                </span>
                @if (!p.isVoided) {
                  <button type="button" class="text-xs text-neutral-400 hover:text-primary-500" (click)="voidTarget.set(p)">Void</button>
                }
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
        [mode]="modalMode()"
        (cancel)="showRecordModal.set(false)"
        (recorded)="onRecorded()"
      />
    }

    @if (voidTarget(); as target) {
      <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="voidTarget.set(null)">
        <div class="card w-full max-w-sm" (click)="$event.stopPropagation()">
          <h2 class="text-base font-semibold text-neutral-900">Void this {{ target.isRefund ? 'refund' : 'payment' }}?</h2>
          <p class="text-sm text-neutral-600 mt-xs">{{ target.receiptNumber }} · {{ target.amount | number: '1.2-2' }} stays on record but no longer counts toward the invoice.</p>
          <div class="mt-md">
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Reason (optional)</label>
            <input class="input-field" type="text" [(ngModel)]="voidReason" placeholder="e.g. recorded in error" />
          </div>
          @if (voidError()) {
            <p class="text-sm text-primary-500 mt-sm">{{ voidError() }}</p>
          }
          <div class="flex justify-end gap-sm pt-md">
            <button type="button" class="btn-secondary flex-1 text-xs" (click)="voidTarget.set(null)">Cancel</button>
            <button type="button" class="btn-primary flex-1 text-xs" [disabled]="voiding()" (click)="confirmVoid(target)">{{ voiding() ? 'Voiding…' : 'Void' }}</button>
          </div>
        </div>
      </div>
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
  modalMode = signal<'payment' | 'refund'>('payment');
  voidTarget = signal<Payment | null>(null);
  voidReason = '';
  voiding = signal(false);
  voidError = signal('');

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

  activePayments(): Payment[] {
    return this.payments().filter(p => !p.isVoided && !p.isRefund);
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
    return `${this.activePayments().length} payment(s) recorded`;
  }

  onRecorded(): void {
    this.showRecordModal.set(false);
    this.fetch();
    this.invoiceUpdated.emit();
  }

  confirmVoid(payment: Payment): void {
    this.voiding.set(true);
    this.voidError.set('');
    this.invoiceService.voidPayment(this.workspaceId, this.invoice.invoiceId, payment.paymentId, this.voidReason.trim() || undefined).subscribe({
      next: () => {
        this.voiding.set(false);
        this.voidTarget.set(null);
        this.voidReason = '';
        this.fetch();
        this.invoiceUpdated.emit();
      },
      error: err => {
        this.voiding.set(false);
        this.voidError.set(err.error?.message ?? 'Could not void this entry.');
      }
    });
  }

  viewProof(p: Payment): void {
    this.invoiceService.getPaymentProofBlob(this.workspaceId, this.invoice.invoiceId, p.paymentId).subscribe({
      next: blob => window.open(URL.createObjectURL(blob), '_blank')
    });
  }
}
