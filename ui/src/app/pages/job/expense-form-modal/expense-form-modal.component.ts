import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { EXPENSE_CATEGORIES, Expense, ExpenseCategory, ExpenseRequest, ExpenseService, PAYEE_TYPES, PayeeType } from '../../../core/expense.service';
import { JobParticipant } from '../../../core/job.service';
import { Milestone } from '../../../core/milestone.service';

@Component({
  selector: 'app-expense-form-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-md" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">{{ editing ? 'Edit expense' : 'New expense' }}</h2>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Category</label>
            <select class="input-field" name="category" [(ngModel)]="category">
              @for (c of categories; track c) {
                <option [value]="c">{{ c }}</option>
              }
            </select>
          </div>

          @if (milestones.length > 0) {
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Milestone (optional)</label>
              <select class="input-field" name="milestoneId" [(ngModel)]="milestoneId">
                <option [ngValue]="null">No milestone</option>
                @for (m of milestones; track m.milestoneId) {
                  <option [ngValue]="m.milestoneId">{{ m.title }}</option>
                }
              </select>
            </div>
          }

          @if (category === 'StaffCost') {
            <div class="grid grid-cols-2 gap-sm">
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Payee</label>
                <select class="input-field" name="payeeId" [(ngModel)]="payeeId">
                  <option [ngValue]="null">Select a person…</option>
                  @for (p of participants; track p.personId) {
                    <option [ngValue]="p.personId">{{ p.firstName }} {{ p.lastName }}</option>
                  }
                </select>
              </div>
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Type</label>
                <select class="input-field" name="payeeType" [(ngModel)]="payeeType">
                  @for (t of payeeTypes; track t) {
                    <option [value]="t">{{ t }}</option>
                  }
                </select>
              </div>
            </div>
          }

          <div class="grid grid-cols-2 gap-sm">
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Amount</label>
              <input class="input-field" type="number" min="0.01" step="0.01" name="amount" [(ngModel)]="amount" />
            </div>
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Incurred date</label>
              <input class="input-field" type="date" name="incurredDate" [(ngModel)]="incurredDate" />
            </div>
          </div>

          @if (feeWarning(); as warning) {
            <p class="text-xs text-amber-600">⚠ {{ warning }}</p>
          }

          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Description (optional)</label>
            <textarea class="input-field" rows="2" name="description" [(ngModel)]="description"></textarea>
          </div>

          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Receipt (optional)</label>
            <input type="file" accept=".pdf,.jpg,.jpeg,.png" (change)="onFileChange($event)" />
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading() || amount <= 0 || !incurredDate || (category === 'StaffCost' && !payeeId)">
              {{ loading() ? 'Saving…' : editing ? 'Save' : 'Create' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class ExpenseFormModalComponent implements OnInit {
  @Input() workspaceId = '';
  @Input() jobId: string | null = null;
  @Input() participants: JobParticipant[] = [];
  @Input() milestones: Milestone[] = [];
  @Input() editing: Expense | null = null;
  @Input() presetMilestoneId: string | null = null;
  @Output() cancel = new EventEmitter<void>();
  @Output() saved = new EventEmitter<Expense>();

  categories = EXPENSE_CATEGORIES;
  payeeTypes = PAYEE_TYPES;
  category: ExpenseCategory = 'Other';
  payeeId: string | null = null;
  payeeType: PayeeType = 'Salary';
  milestoneId: string | null = null;
  amount = 0;
  incurredDate = '';
  description = '';
  receiptFile: File | null = null;
  loading = signal(false);
  error = signal('');

  constructor(private expenseService: ExpenseService) {}

  ngOnInit(): void {
    if (this.editing) {
      this.category = this.editing.category;
      this.amount = this.editing.amount;
      this.incurredDate = this.editing.incurredDate.substring(0, 10);
      this.description = this.editing.description ?? '';
      this.payeeId = this.editing.payeeId;
      this.payeeType = this.editing.payeeType ?? 'Salary';
      this.milestoneId = this.editing.milestoneId;
    } else {
      this.incurredDate = new Date().toISOString().substring(0, 10);
      this.milestoneId = this.presetMilestoneId;
    }
  }

  feeWarning(): string | null {
    if (!this.milestoneId || this.amount <= 0) return null;
    const m = this.milestones.find(x => x.milestoneId === this.milestoneId);
    if (!m?.amount) return null;
    if (this.amount > m.amount) return `This expense (${this.amount.toFixed(2)}) exceeds the milestone fee (${m.amount.toFixed(2)}).`;
    return null;
  }

  onFileChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.receiptFile = input.files?.[0] ?? null;
  }

  submit(): void {
    if (this.amount <= 0 || !this.incurredDate || (this.category === 'StaffCost' && !this.payeeId)) return;
    this.error.set('');
    this.loading.set(true);

    const request: ExpenseRequest = {
      category: this.category,
      amount: this.amount,
      description: this.description || undefined,
      incurredDate: this.incurredDate,
      payeeId: this.category === 'StaffCost' ? this.payeeId! : undefined,
      payeeType: this.category === 'StaffCost' ? this.payeeType : undefined,
      milestoneId: this.milestoneId ?? undefined
    };

    const save$ = this.jobId
      ? this.editing
        ? this.expenseService.update(this.workspaceId, this.jobId, this.editing.expenseId, request)
        : this.expenseService.create(this.workspaceId, this.jobId, request)
      : this.editing
        ? this.expenseService.updateWorkspaceLevel(this.workspaceId, this.editing.expenseId, request)
        : this.expenseService.createWorkspaceLevel(this.workspaceId, request);

    save$.subscribe({
      next: expense => {
        if (this.receiptFile) {
          const upload$ = this.jobId
            ? this.expenseService.uploadReceipt(this.workspaceId, this.jobId, expense.expenseId, this.receiptFile)
            : this.expenseService.uploadWorkspaceLevelReceipt(this.workspaceId, expense.expenseId, this.receiptFile);
          upload$.subscribe({
            next: updated => { this.loading.set(false); this.saved.emit(updated); },
            error: () => { this.loading.set(false); this.saved.emit(expense); }
          });
        } else {
          this.loading.set(false);
          this.saved.emit(expense);
        }
      },
      error: err => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not save expense.');
      }
    });
  }
}
