import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { Invoice, InvoiceService, Payment } from '../../../core/billing.service';

@Component({
  selector: 'app-receipt-print',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (loading()) {
      <p class="p-lg text-sm text-neutral-500">Loading…</p>
    } @else if (payment(); as payment) {
      <div class="max-w-md mx-auto p-lg">
        <div class="flex justify-between items-start mb-lg print:hidden">
          <h1 class="text-lg font-semibold">Receipt {{ payment.receiptNumber }}</h1>
          <button type="button" class="btn-primary" (click)="print()">Print / Save as PDF</button>
        </div>

        <h1 class="text-xl font-semibold text-neutral-900">Receipt {{ payment.receiptNumber }}</h1>
        <p class="text-sm text-neutral-600">For invoice {{ invoice()?.number }}</p>

        <div class="mt-lg space-y-sm text-sm">
          <div class="flex justify-between"><span class="text-neutral-500">Amount</span><span class="font-semibold">{{ payment.amount | number: '1.2-2' }}</span></div>
          <div class="flex justify-between"><span class="text-neutral-500">Method</span><span>{{ payment.method }}</span></div>
          <div class="flex justify-between"><span class="text-neutral-500">Received</span><span>{{ payment.receivedAt | date: 'mediumDate' }}</span></div>
          @if (payment.referenceNumber) {
            <div class="flex justify-between"><span class="text-neutral-500">Reference</span><span>{{ payment.referenceNumber }}</span></div>
          }
        </div>
      </div>
    } @else if (error()) {
      <p class="p-lg text-sm text-primary-500">{{ error() }}</p>
    }
  `
})
export class ReceiptPrintComponent implements OnInit {
  invoice = signal<Invoice | null>(null);
  payment = signal<Payment | null>(null);
  loading = signal(true);
  error = signal('');

  constructor(private invoiceService: InvoiceService, private route: ActivatedRoute) {}

  ngOnInit(): void {
    const workspaceId = this.route.snapshot.paramMap.get('id') ?? '';
    const invoiceId = this.route.snapshot.paramMap.get('invoiceId') ?? '';
    const paymentId = this.route.snapshot.paramMap.get('paymentId') ?? '';

    this.invoiceService.getById(workspaceId, invoiceId).subscribe({
      next: invoice => {
        this.invoice.set(invoice);
        this.invoiceService.getPayments(workspaceId, invoiceId).subscribe({
          next: payments => {
            const match = payments.find(p => p.paymentId === paymentId) ?? null;
            this.payment.set(match);
            if (!match) this.error.set('Payment not found.');
            this.loading.set(false);
          },
          error: err => {
            this.error.set(err.error?.message ?? 'Could not load payment.');
            this.loading.set(false);
          }
        });
      },
      error: err => {
        this.error.set(err.error?.message ?? 'Could not load invoice.');
        this.loading.set(false);
      }
    });
  }

  print(): void {
    window.print();
  }
}
