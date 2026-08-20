import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Invoice, InvoiceService } from '../../../core/billing.service';
import { CurrentWorkspaceService } from '../../../core/current-workspace.service';
import { BillingTabsComponent } from '../billing-tabs.component';
import { RecordPaymentModalComponent } from './record-payment-modal/record-payment-modal.component';
import { SendDocumentModalComponent } from '../../../shared/send-document-modal/send-document-modal.component';

@Component({
  selector: 'app-invoice-list',
  standalone: true,
  imports: [CommonModule, RouterLink, BillingTabsComponent, RecordPaymentModalComponent, SendDocumentModalComponent],
  template: `
    <div class="p-lg max-w-5xl mx-auto">
      <app-billing-tabs [workspaceId]="workspaceId" active="invoices" />

      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Invoices</h1>
        <button class="btn-primary" [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices', 'new']">New invoice</button>
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (error()) {
        <div class="card text-sm text-primary-500">
          {{ error() }}
          <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
        </div>
      } @else if (invoices().length === 0) {
        <div class="card text-center text-sm text-neutral-500">No invoices yet. Create one to get started.</div>
      } @else {
        <div class="card p-0 overflow-x-auto">
          <table class="w-full text-sm">
            <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
              <tr>
                <th class="text-left px-lg py-sm font-medium">Number</th>
                <th class="text-left px-lg py-sm font-medium">Total</th>
                <th class="text-left px-lg py-sm font-medium">Balance</th>
                <th class="text-left px-lg py-sm font-medium">Status</th>
                <th class="text-left px-lg py-sm font-medium">Due date</th>
                <th class="px-lg py-sm"></th>
              </tr>
            </thead>
            <tbody>
              @for (invoice of invoices(); track invoice.invoiceId) {
                <tr class="border-t border-neutral-200 hover:bg-neutral-50">
                  <td class="px-lg py-sm text-neutral-900">
                    <a [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices', invoice.invoiceId, 'edit']">{{ invoice.number }}</a>
                  </td>
                  <td class="px-lg py-sm text-neutral-600">{{ invoice.total | number: '1.2-2' }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ invoice.balance | number: '1.2-2' }}</td>
                  <td class="px-lg py-sm">
                    <span class="text-xs px-sm py-xs rounded" [class]="statusClass(invoice)">
                      {{ invoice.isOverdue ? 'Overdue (' + invoice.daysOverdue + 'd)' : invoice.status }}
                    </span>
                  </td>
                  <td class="px-lg py-sm text-neutral-600">{{ invoice.dueDate ? (invoice.dueDate | date: 'mediumDate') : '—' }}</td>
                  <td class="px-lg py-sm text-right">
                    <a
                      class="text-xs text-neutral-500 hover:text-neutral-700 mr-md"
                      [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices', invoice.invoiceId, 'print']"
                    >Print</a>
                    <button class="text-xs text-primary-500 hover:text-primary-600 mr-md" (click)="openSend(invoice)">Send</button>
                    @if (invoice.balance > 0 && invoice.status !== 'Cancelled') {
                      <button class="text-xs text-primary-500 hover:text-primary-600" (click)="openPayment(invoice)">Record payment</button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>

    @if (payingInvoice(); as invoice) {
      <app-record-payment-modal [workspaceId]="workspaceId" [invoice]="invoice" (cancel)="payingInvoice.set(null)" (recorded)="onPaymentRecorded()" />
    }
    @if (sendingInvoice(); as invoice) {
      <app-send-document-modal
        [workspaceId]="workspaceId"
        [jobId]="invoice.jobId"
        documentLabel="invoice"
        [documentNumber]="invoice.number"
        (cancel)="sendingInvoice.set(null)"
        (send)="onSend(invoice, $event)"
      />
    }
  `
})
export class InvoiceListComponent implements OnInit {
  workspaceId = '';
  invoices = signal<Invoice[]>([]);
  loading = signal(true);
  error = signal('');
  payingInvoice = signal<Invoice | null>(null);
  sendingInvoice = signal<Invoice | null>(null);

  constructor(private invoiceService: InvoiceService, private currentWorkspace: CurrentWorkspaceService) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    this.invoiceService.search(this.workspaceId).subscribe({
      next: invoices => {
        this.invoices.set(invoices);
        this.loading.set(false);
      },
      error: err => {
        this.error.set(err.error?.message ?? 'Could not load invoices.');
        this.loading.set(false);
      }
    });
  }

  statusClass(invoice: Invoice): string {
    if (invoice.isOverdue) return 'bg-primary-50 text-primary-600';
    if (invoice.status === 'Paid') return 'bg-green-50 text-green-700';
    if (invoice.status === 'PartiallyPaid') return 'bg-amber-50 text-amber-700';
    return 'bg-neutral-100 text-neutral-700';
  }

  openPayment(invoice: Invoice): void {
    this.payingInvoice.set(invoice);
  }

  onPaymentRecorded(): void {
    this.payingInvoice.set(null);
    this.fetch();
  }

  openSend(invoice: Invoice): void {
    this.sendingInvoice.set(invoice);
  }

  onSend(invoice: Invoice, recipientPersonIds: string[]): void {
    this.invoiceService.send(this.workspaceId, invoice.invoiceId, recipientPersonIds).subscribe({
      next: () => {
        this.sendingInvoice.set(null);
        this.fetch();
      },
      error: () => this.sendingInvoice.set(null)
    });
  }
}
