import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { Invoice, InvoiceService } from '../../../core/billing.service';

@Component({
  selector: 'app-invoice-print',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (loading()) {
      <p class="p-lg text-sm text-neutral-500">Loading…</p>
    } @else if (invoice(); as invoice) {
      <div class="max-w-2xl mx-auto p-lg">
        <div class="flex justify-between items-start mb-lg print:hidden">
          <h1 class="text-lg font-semibold">Invoice {{ invoice.number }}</h1>
          <button type="button" class="btn-primary" (click)="print()">Print / Save as PDF</button>
        </div>

        <h1 class="text-xl font-semibold text-neutral-900">Invoice {{ invoice.number }}</h1>
        <p class="text-sm text-neutral-600">
          Status: {{ invoice.status }}
          @if (invoice.dueDate) { · Due {{ invoice.dueDate | date: 'mediumDate' }} }
        </p>

        <table class="w-full text-sm mt-lg">
          <thead>
            <tr class="border-b border-neutral-200 text-left text-neutral-500">
              <th class="py-xs">Description</th>
              <th class="py-xs text-right">Qty</th>
              <th class="py-xs text-right">Unit price</th>
              <th class="py-xs text-right">Amount</th>
            </tr>
          </thead>
          <tbody>
            @for (item of invoice.lineItems; track $index) {
              <tr class="border-b border-neutral-100">
                <td class="py-xs">{{ item.description }}</td>
                <td class="py-xs text-right">{{ item.quantity }}</td>
                <td class="py-xs text-right">{{ item.unitPrice | number: '1.2-2' }}</td>
                <td class="py-xs text-right">{{ item.quantity * item.unitPrice | number: '1.2-2' }}</td>
              </tr>
            }
          </tbody>
        </table>

        <div class="mt-md flex flex-col items-end text-sm">
          <div class="flex justify-between w-56"><span>Subtotal</span><span>{{ invoice.subtotal | number: '1.2-2' }}</span></div>
          <div class="flex justify-between w-56"><span>Discount</span><span>-{{ invoice.discountAmount | number: '1.2-2' }}</span></div>
          <div class="flex justify-between w-56"><span>Tax ({{ invoice.taxRatePercent }}%)</span><span>{{ invoice.total - invoice.subtotal + invoice.discountAmount | number: '1.2-2' }}</span></div>
          <div class="flex justify-between w-56 font-semibold text-neutral-900 border-t border-neutral-200 mt-xs pt-xs"><span>Total</span><span>{{ invoice.total | number: '1.2-2' }}</span></div>
          <div class="flex justify-between w-56 text-neutral-600"><span>Paid</span><span>{{ invoice.amountPaid | number: '1.2-2' }}</span></div>
          <div class="flex justify-between w-56 font-semibold"><span>Balance due</span><span>{{ invoice.balance | number: '1.2-2' }}</span></div>
        </div>
      </div>
    } @else if (error()) {
      <p class="p-lg text-sm text-primary-500">{{ error() }}</p>
    }
  `
})
export class InvoicePrintComponent implements OnInit {
  invoice = signal<Invoice | null>(null);
  loading = signal(true);
  error = signal('');

  constructor(private invoiceService: InvoiceService, private route: ActivatedRoute) {}

  ngOnInit(): void {
    const workspaceId = this.route.snapshot.paramMap.get('id') ?? '';
    const invoiceId = this.route.snapshot.paramMap.get('invoiceId') ?? '';
    this.invoiceService.getById(workspaceId, invoiceId).subscribe({
      next: invoice => {
        this.invoice.set(invoice);
        this.loading.set(false);
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
