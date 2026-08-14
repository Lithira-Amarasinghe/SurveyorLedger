import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Invoice, InvoiceService, Payment, PaymentMethod } from '../../../../core/billing.service';

@Component({
  selector: 'app-record-payment-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-sm" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">Record payment</h2>
        <p class="text-sm text-neutral-600 mt-xs">Outstanding balance: {{ invoice.balance | number: '1.2-2' }}</p>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Amount</label>
            <input class="input-field" type="number" min="0.01" step="0.01" name="amount" [(ngModel)]="amount" required autofocus />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Method</label>
            <select class="input-field" name="method" [(ngModel)]="method">
              <option value="Cash">Cash</option>
              <option value="BankTransfer">Bank transfer</option>
              <option value="Cheque">Cheque</option>
            </select>
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Received date</label>
            <input class="input-field" type="date" name="receivedAt" [(ngModel)]="receivedAt" required />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Reference number</label>
            <input class="input-field" type="text" name="referenceNumber" [(ngModel)]="referenceNumber" placeholder="Cheque #, transaction ref…" />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Proof of payment (optional)</label>
            <input class="input-field" type="file" (change)="onFileChange($event)" />
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading() || amount <= 0 || !receivedAt">
              {{ loading() ? 'Recording…' : 'Record payment' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class RecordPaymentModalComponent {
  @Input() workspaceId = '';
  @Input() invoice!: Invoice;
  @Output() cancel = new EventEmitter<void>();
  @Output() recorded = new EventEmitter<Payment>();

  amount = 0;
  method: PaymentMethod = 'Cash';
  receivedAt = new Date().toISOString().substring(0, 10);
  referenceNumber = '';
  proofFile: File | undefined;
  loading = signal(false);
  error = signal('');

  constructor(private invoiceService: InvoiceService) {}

  onFileChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.proofFile = input.files?.[0];
  }

  submit(): void {
    if (this.amount <= 0 || !this.receivedAt) return;
    this.error.set('');
    this.loading.set(true);

    this.invoiceService
      .recordPayment(
        this.workspaceId,
        this.invoice.invoiceId,
        { amount: this.amount, method: this.method, receivedAt: this.receivedAt, referenceNumber: this.referenceNumber.trim() || undefined },
        this.proofFile
      )
      .subscribe({
        next: payment => {
          this.loading.set(false);
          this.recorded.emit(payment);
        },
        error: err => {
          this.loading.set(false);
          this.error.set(err.error?.message ?? 'Could not record payment.');
        }
      });
  }
}
