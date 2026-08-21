import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { EXPENSE_CATEGORIES, Expense, ExpenseService } from '../../../core/expense.service';
import { CurrentWorkspaceService } from '../../../core/current-workspace.service';
import { BillingTabsComponent } from '../billing-tabs.component';
import { ExpenseFormModalComponent } from '../../job/expense-form-modal/expense-form-modal.component';

@Component({
  selector: 'app-workspace-expense-list',
  standalone: true,
  imports: [CommonModule, FormsModule, BillingTabsComponent, ExpenseFormModalComponent],
  template: `
    <div class="p-lg max-w-4xl mx-auto">
      <app-billing-tabs [workspaceId]="workspaceId" />

      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Expenses</h1>
        <button class="btn-primary" (click)="openModal()">+ Expense</button>
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (error()) {
        <div class="card text-sm text-primary-500">
          {{ error() }}
          <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
        </div>
      } @else if (expenses().length === 0) {
        <div class="card text-center text-sm text-neutral-500">No workspace-level expenses yet.</div>
      } @else {
        <div class="flex items-center gap-sm mb-sm">
          <select class="input-field w-40 py-xs text-xs" [(ngModel)]="categoryFilter">
            <option value="">All categories</option>
            @for (c of categories; track c) {
              <option [value]="c">{{ c }}</option>
            }
          </select>
        </div>
        <div class="card p-0 overflow-x-auto">
          <table class="w-full text-sm">
            <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
              <tr>
                <th class="text-left px-lg py-sm font-medium">Date</th>
                <th class="text-left px-lg py-sm font-medium">Category</th>
                <th class="text-left px-lg py-sm font-medium">Payee</th>
                <th class="text-left px-lg py-sm font-medium">Amount</th>
                <th class="text-left px-lg py-sm font-medium"></th>
              </tr>
            </thead>
            <tbody>
              @for (expense of filteredExpenses(); track expense.expenseId) {
                <tr class="border-t border-neutral-200 hover:bg-neutral-50">
                  <td class="px-lg py-sm text-neutral-600">{{ expense.incurredDate | date: 'mediumDate' }}</td>
                  <td class="px-lg py-sm text-neutral-900">{{ expense.category }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ expense.payeeName ?? '—' }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ expense.amount | number: '1.2-2' }}</td>
                  <td class="px-lg py-sm text-right whitespace-nowrap">
                    <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700 mr-sm" (click)="openModal(expense)">Edit</button>
                    <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="confirmingDelete.set(expense)">Delete</button>
                  </td>
                </tr>
              }
            </tbody>
            <tfoot>
              <tr class="border-t border-neutral-300 bg-neutral-50 font-medium">
                <td class="px-lg py-sm text-neutral-600" colspan="3">Total</td>
                <td class="px-lg py-sm text-neutral-900">{{ filteredTotal() | number: '1.2-2' }}</td>
                <td></td>
              </tr>
            </tfoot>
          </table>
        </div>
      }
    </div>

    @if (showModal()) {
      <app-expense-form-modal
        [workspaceId]="workspaceId"
        [editing]="editingExpense()"
        (cancel)="showModal.set(false); editingExpense.set(null)"
        (saved)="onSaved()"
      />
    }

    @if (confirmingDelete(); as expense) {
      <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="confirmingDelete.set(null)">
        <div class="card w-full max-w-sm" (click)="$event.stopPropagation()">
          <h2 class="text-base font-semibold text-neutral-900">Delete expense?</h2>
          <p class="text-sm text-neutral-600 mt-xs">{{ expense.category }} · {{ expense.amount | number: '1.2-2' }} will be deleted and cannot be undone.</p>
          <div class="flex justify-end gap-sm pt-md">
            <button type="button" class="btn-secondary flex-1 text-xs" (click)="confirmingDelete.set(null)">Cancel</button>
            <button type="button" class="btn-primary flex-1 text-xs" (click)="doDelete(expense)">Delete</button>
          </div>
        </div>
      </div>
    }
  `
})
export class WorkspaceExpenseListComponent implements OnInit {
  workspaceId = '';
  categories = EXPENSE_CATEGORIES;
  categoryFilter = '';
  expenses = signal<Expense[]>([]);
  loading = signal(true);
  error = signal('');
  showModal = signal(false);
  editingExpense = signal<Expense | null>(null);
  confirmingDelete = signal<Expense | null>(null);

  constructor(private expenseService: ExpenseService, private currentWorkspace: CurrentWorkspaceService) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    this.expenseService.getAllWorkspaceLevel(this.workspaceId).subscribe({
      next: expenses => {
        this.expenses.set(expenses);
        this.loading.set(false);
      },
      error: err => {
        this.error.set(err.error?.message ?? 'Could not load expenses.');
        this.loading.set(false);
      }
    });
  }

  filteredExpenses(): Expense[] {
    return this.expenses().filter(e => !this.categoryFilter || e.category === this.categoryFilter);
  }

  filteredTotal(): number {
    return this.filteredExpenses().reduce((sum, e) => sum + e.amount, 0);
  }

  openModal(expense: Expense | null = null): void {
    this.editingExpense.set(expense);
    this.showModal.set(true);
  }

  onSaved(): void {
    this.showModal.set(false);
    this.editingExpense.set(null);
    this.fetch();
  }

  doDelete(expense: Expense): void {
    this.expenseService.deleteWorkspaceLevel(this.workspaceId, expense.expenseId).subscribe({
      next: () => {
        this.confirmingDelete.set(null);
        this.fetch();
      },
      error: () => this.confirmingDelete.set(null)
    });
  }
}
