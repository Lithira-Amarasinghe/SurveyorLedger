import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  ExpenseHistoryRow,
  FinancialSummary,
  OutstandingInvoiceRow,
  PaymentHistoryRow,
  ReportService
} from '../../core/report.service';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';
import { downloadCsv } from '../../core/csv-export';

function firstOfMonth(): string {
  const d = new Date();
  return new Date(d.getFullYear(), d.getMonth(), 1).toISOString().substring(0, 10);
}

function today(): string {
  return new Date().toISOString().substring(0, 10);
}

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="p-lg max-w-5xl mx-auto space-y-lg">
      <div class="flex items-end justify-between gap-md flex-wrap">
        <h1 class="text-lg font-semibold text-neutral-900">Reports</h1>
        <div class="flex items-end gap-sm">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">From</label>
            <input class="input-field" type="date" [(ngModel)]="from" />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">To</label>
            <input class="input-field" type="date" [(ngModel)]="to" />
          </div>
          <button type="button" class="btn-primary" [disabled]="loading()" (click)="fetch()">Apply</button>
        </div>
      </div>

      <div class="card">
        <h2 class="text-sm font-semibold text-neutral-900 mb-md">Financial summary</h2>
        @if (summaryError()) {
          <p class="text-sm text-primary-500">{{ summaryError() }}</p>
        } @else if (summary(); as s) {
          <div class="grid grid-cols-2 sm:grid-cols-3 gap-md text-sm">
            <div><span class="block text-xs text-neutral-500">Invoiced</span><span class="font-semibold text-neutral-900">{{ s.totalInvoiced | number: '1.2-2' }}</span></div>
            <div><span class="block text-xs text-neutral-500">Paid</span><span class="font-semibold text-neutral-900">{{ s.totalPaid | number: '1.2-2' }}</span></div>
            <div><span class="block text-xs text-neutral-500">Outstanding</span><span class="font-semibold text-neutral-900">{{ s.totalOutstanding | number: '1.2-2' }}</span></div>
            <div><span class="block text-xs text-neutral-500">Expenses</span><span class="font-semibold text-neutral-900">{{ s.totalExpenses | number: '1.2-2' }}</span></div>
            <div><span class="block text-xs text-neutral-500">Gross profit</span><span class="font-semibold" [class.text-primary-500]="s.grossProfit < 0">{{ s.grossProfit | number: '1.2-2' }}</span></div>
            <div><span class="block text-xs text-neutral-500">Profit margin</span><span class="font-semibold text-neutral-900">{{ s.profitMarginPercent | number: '1.1-1' }}%</span></div>
          </div>
        } @else if (summaryLoading()) {
          <p class="text-sm text-neutral-500">Loading…</p>
        }
      </div>

      <div class="card">
        <div class="flex items-center justify-between mb-md">
          <h2 class="text-sm font-semibold text-neutral-900">Outstanding invoices</h2>
          @if (outstanding().length > 0) {
            <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="exportOutstanding()">Export CSV</button>
          }
        </div>
        @if (outstandingError()) {
          <p class="text-sm text-primary-500">{{ outstandingError() }}</p>
        } @else if (outstandingLoading()) {
          <p class="text-sm text-neutral-500">Loading…</p>
        } @else if (outstanding().length === 0) {
          <p class="text-sm text-neutral-500">Nothing outstanding.</p>
        } @else {
          <div class="card p-0 overflow-x-auto">
            <table class="w-full text-sm">
              <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
                <tr>
                  <th class="text-left px-lg py-sm font-medium">Job</th>
                  <th class="text-left px-lg py-sm font-medium">Invoice</th>
                  <th class="text-left px-lg py-sm font-medium">Client</th>
                  <th class="text-left px-lg py-sm font-medium">Total</th>
                  <th class="text-left px-lg py-sm font-medium">Paid</th>
                  <th class="text-left px-lg py-sm font-medium">Balance</th>
                  <th class="text-left px-lg py-sm font-medium">Overdue</th>
                </tr>
              </thead>
              <tbody>
                @for (row of outstanding(); track row.invoiceId) {
                  <tr class="border-t border-neutral-200">
                    <td class="px-lg py-sm text-neutral-900">{{ row.jobNumber }} · {{ row.jobTitle }}</td>
                    <td class="px-lg py-sm text-neutral-600">{{ row.invoiceNumber }}</td>
                    <td class="px-lg py-sm text-neutral-600">{{ row.clientName }}</td>
                    <td class="px-lg py-sm text-neutral-600">{{ row.total | number: '1.2-2' }}</td>
                    <td class="px-lg py-sm text-neutral-600">{{ row.amountPaid | number: '1.2-2' }}</td>
                    <td class="px-lg py-sm text-neutral-900 font-medium">{{ row.balance | number: '1.2-2' }}</td>
                    <td class="px-lg py-sm" [class.text-primary-500]="row.isOverdue">{{ row.isOverdue ? row.daysOverdue + 'd overdue' : '—' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>

      <div class="card">
        <div class="flex items-center justify-between mb-md">
          <h2 class="text-sm font-semibold text-neutral-900">Payment history</h2>
          @if (payments().length > 0) {
            <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="exportPayments()">Export CSV</button>
          }
        </div>
        @if (paymentsError()) {
          <p class="text-sm text-primary-500">{{ paymentsError() }}</p>
        } @else if (paymentsLoading()) {
          <p class="text-sm text-neutral-500">Loading…</p>
        } @else if (payments().length === 0) {
          <p class="text-sm text-neutral-500">No payments in this range.</p>
        } @else {
          <div class="card p-0 overflow-x-auto">
            <table class="w-full text-sm">
              <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
                <tr>
                  <th class="text-left px-lg py-sm font-medium">Date</th>
                  <th class="text-left px-lg py-sm font-medium">Job</th>
                  <th class="text-left px-lg py-sm font-medium">Invoice</th>
                  <th class="text-left px-lg py-sm font-medium">Client</th>
                  <th class="text-left px-lg py-sm font-medium">Amount</th>
                  <th class="text-left px-lg py-sm font-medium">Method</th>
                </tr>
              </thead>
              <tbody>
                @for (row of payments(); track row.paymentId) {
                  <tr class="border-t border-neutral-200">
                    <td class="px-lg py-sm text-neutral-600">{{ row.receivedAt | date: 'mediumDate' }}</td>
                    <td class="px-lg py-sm text-neutral-900">{{ row.jobNumber }} · {{ row.jobTitle }}</td>
                    <td class="px-lg py-sm text-neutral-600">{{ row.invoiceNumber }}</td>
                    <td class="px-lg py-sm text-neutral-600">{{ row.clientName }}</td>
                    <td class="px-lg py-sm text-neutral-600">{{ row.amount | number: '1.2-2' }}</td>
                    <td class="px-lg py-sm text-neutral-600">{{ row.method }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (paymentsTotalCount() > payments().length) {
            <p class="text-xs text-neutral-500 mt-sm">Showing {{ payments().length }} of {{ paymentsTotalCount() }}. Narrow the date range to see fewer, or load more below.</p>
            <button type="button" class="text-xs text-primary-500 hover:text-primary-600 mt-xs" (click)="loadMorePayments()">Load more</button>
          }
        }
      </div>

      <div class="card">
        <div class="flex items-center justify-between mb-md">
          <h2 class="text-sm font-semibold text-neutral-900">Expense history</h2>
          @if (expenses().length > 0) {
            <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="exportExpenses()">Export CSV</button>
          }
        </div>
        @if (expensesError()) {
          <p class="text-sm text-primary-500">{{ expensesError() }}</p>
        } @else if (expensesLoading()) {
          <p class="text-sm text-neutral-500">Loading…</p>
        } @else if (expenses().length === 0) {
          <p class="text-sm text-neutral-500">No expenses in this range.</p>
        } @else {
          <div class="card p-0 overflow-x-auto">
            <table class="w-full text-sm">
              <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
                <tr>
                  <th class="text-left px-lg py-sm font-medium">Date</th>
                  <th class="text-left px-lg py-sm font-medium">Job</th>
                  <th class="text-left px-lg py-sm font-medium">Category</th>
                  <th class="text-left px-lg py-sm font-medium">Payee</th>
                  <th class="text-left px-lg py-sm font-medium">Amount</th>
                </tr>
              </thead>
              <tbody>
                @for (row of expenses(); track row.expenseId) {
                  <tr class="border-t border-neutral-200">
                    <td class="px-lg py-sm text-neutral-600">{{ row.incurredDate | date: 'mediumDate' }}</td>
                    <td class="px-lg py-sm text-neutral-900">{{ row.jobNumber }} · {{ row.jobTitle }}</td>
                    <td class="px-lg py-sm text-neutral-600">{{ row.category }}</td>
                    <td class="px-lg py-sm text-neutral-600">{{ row.payeeName ?? '—' }}</td>
                    <td class="px-lg py-sm text-neutral-600">{{ row.amount | number: '1.2-2' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (expensesTotalCount() > expenses().length) {
            <p class="text-xs text-neutral-500 mt-sm">Showing {{ expenses().length }} of {{ expensesTotalCount() }}. Narrow the date range to see fewer, or load more below.</p>
            <button type="button" class="text-xs text-primary-500 hover:text-primary-600 mt-xs" (click)="loadMoreExpenses()">Load more</button>
          }
        }
      </div>
    </div>
  `
})
export class ReportsComponent implements OnInit {
  private reportService = inject(ReportService);
  private currentWorkspace = inject(CurrentWorkspaceService);

  workspaceId = '';
  from = firstOfMonth();
  to = today();

  // Each section loads independently - a slow query in one never blocks the others,
  // and a failure in one doesn't take down the whole page (matches the design spec's
  // intent: "load/refresh them independently").
  summaryLoading = signal(false);
  summaryError = signal('');
  outstandingLoading = signal(false);
  outstandingError = signal('');
  paymentsLoading = signal(false);
  paymentsError = signal('');
  expensesLoading = signal(false);
  expensesError = signal('');

  summary = signal<FinancialSummary | null>(null);
  outstanding = signal<OutstandingInvoiceRow[]>([]);
  payments = signal<PaymentHistoryRow[]>([]);
  paymentsTotalCount = signal(0);
  paymentsPage = 1;
  expenses = signal<ExpenseHistoryRow[]>([]);
  expensesTotalCount = signal(0);
  expensesPage = 1;

  loading(): boolean {
    return this.summaryLoading() || this.outstandingLoading() || this.paymentsLoading() || this.expensesLoading();
  }

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.fetch();
  }

  fetch(): void {
    this.paymentsPage = 1;
    this.expensesPage = 1;

    this.summaryLoading.set(true);
    this.summaryError.set('');
    this.reportService.getSummary(this.workspaceId, this.from, this.to).subscribe({
      next: s => { this.summary.set(s); this.summaryLoading.set(false); },
      error: err => { this.summaryError.set(err.error?.message ?? 'Could not load summary.'); this.summaryLoading.set(false); }
    });

    this.outstandingLoading.set(true);
    this.outstandingError.set('');
    this.reportService.getOutstandingInvoices(this.workspaceId).subscribe({
      next: rows => { this.outstanding.set(rows); this.outstandingLoading.set(false); },
      error: err => { this.outstandingError.set(err.error?.message ?? 'Could not load outstanding invoices.'); this.outstandingLoading.set(false); }
    });

    this.paymentsLoading.set(true);
    this.paymentsError.set('');
    this.reportService.getPayments(this.workspaceId, this.from, this.to, 1).subscribe({
      next: result => { this.payments.set(result.items); this.paymentsTotalCount.set(result.totalCount); this.paymentsLoading.set(false); },
      error: err => { this.paymentsError.set(err.error?.message ?? 'Could not load payment history.'); this.paymentsLoading.set(false); }
    });

    this.expensesLoading.set(true);
    this.expensesError.set('');
    this.reportService.getExpenses(this.workspaceId, this.from, this.to, 1).subscribe({
      next: result => { this.expenses.set(result.items); this.expensesTotalCount.set(result.totalCount); this.expensesLoading.set(false); },
      error: err => { this.expensesError.set(err.error?.message ?? 'Could not load expense history.'); this.expensesLoading.set(false); }
    });
  }

  loadMorePayments(): void {
    this.paymentsPage++;
    this.reportService.getPayments(this.workspaceId, this.from, this.to, this.paymentsPage).subscribe(result => {
      this.payments.update(list => [...list, ...result.items]);
    });
  }

  loadMoreExpenses(): void {
    this.expensesPage++;
    this.reportService.getExpenses(this.workspaceId, this.from, this.to, this.expensesPage).subscribe(result => {
      this.expenses.update(list => [...list, ...result.items]);
    });
  }

  exportOutstanding(): void {
    downloadCsv('outstanding-invoices.csv', [
      { key: 'jobNumber', header: 'Job Number' },
      { key: 'jobTitle', header: 'Job Title' },
      { key: 'invoiceNumber', header: 'Invoice' },
      { key: 'clientName', header: 'Client' },
      { key: 'total', header: 'Total' },
      { key: 'amountPaid', header: 'Paid' },
      { key: 'balance', header: 'Balance' },
      { key: 'daysOverdue', header: 'Days Overdue' }
    ], this.outstanding());
  }

  exportPayments(): void {
    downloadCsv('payment-history.csv', [
      { key: 'receivedAt', header: 'Date' },
      { key: 'jobNumber', header: 'Job Number' },
      { key: 'jobTitle', header: 'Job Title' },
      { key: 'invoiceNumber', header: 'Invoice' },
      { key: 'clientName', header: 'Client' },
      { key: 'amount', header: 'Amount' },
      { key: 'method', header: 'Method' }
    ], this.payments());
  }

  exportExpenses(): void {
    downloadCsv('expense-history.csv', [
      { key: 'incurredDate', header: 'Date' },
      { key: 'jobNumber', header: 'Job Number' },
      { key: 'jobTitle', header: 'Job Title' },
      { key: 'category', header: 'Category' },
      { key: 'payeeName', header: 'Payee' },
      { key: 'amount', header: 'Amount' }
    ], this.expenses());
  }
}
