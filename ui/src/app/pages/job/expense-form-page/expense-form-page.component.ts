import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { EXPENSE_CATEGORIES, Expense, ExpenseCategory, ExpenseRequest, ExpenseService, PAYEE_TYPES, PayeeType } from '../../../core/expense.service';
import { JobParticipant, JobService } from '../../../core/job.service';
import { Milestone, MilestonePaymentStatus, MilestoneService } from '../../../core/milestone.service';
import { PayeePickerModalComponent, PayeeOption } from '../payee-picker-modal/payee-picker-modal.component';
import { MilestonePickerModalComponent } from '../milestone-picker-modal/milestone-picker-modal.component';
import { FilePickerFieldComponent } from '../../../shared/file-picker-field/file-picker-field.component';

@Component({
  selector: 'app-expense-form-page',
  standalone: true,
  imports: [CommonModule, FormsModule, PayeePickerModalComponent, MilestonePickerModalComponent, FilePickerFieldComponent],
  template: `
    <div class="p-lg max-w-2xl mx-auto space-y-lg">
      <div class="flex items-center justify-between">
        <h1 class="text-lg font-semibold text-neutral-900">{{ editingId ? 'Edit expense' : 'New expense' }}</h1>
        <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" (click)="goBack()">← Back</button>
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else {
        <div class="card">
          <form class="space-y-md" (ngSubmit)="submit()">
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Category</label>
              <select class="input-field" name="category" [(ngModel)]="category">
                @for (c of categories; track c) {
                  <option [value]="c">{{ c }}</option>
                }
              </select>
            </div>

            @if (milestones().length > 0) {
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Milestone (optional)</label>
                @if (selectedMilestone(); as sel) {
                  <button type="button" class="input-field w-full flex items-center justify-between text-left" (click)="showMilestonePicker.set(true)">
                    <span class="text-neutral-900">{{ sel.title }}</span>
                    <span class="text-xs text-neutral-500">{{ sel.amount != null ? (sel.amount | number: '1.2-2') + ' fee' : 'no fee set' }}</span>
                  </button>
                } @else {
                  <button type="button" class="input-field w-full text-left text-neutral-500" (click)="showMilestonePicker.set(true)">No milestone — job-level expense</button>
                }
                @if (selectedMilestone(); as m) {
                  <div class="mt-xs px-md py-sm rounded bg-neutral-50 text-xs text-neutral-600 space-y-xs">
                    <div class="flex items-center justify-between">
                      <span>Milestone fee</span>
                      <span class="text-neutral-900 font-medium">{{ m.amount != null ? (m.amount | number: '1.2-2') : 'not set' }}</span>
                    </div>
                    @if (selectedMilestoneStatus(); as pay) {
                      @if (m.amount) {
                        <div class="h-2 rounded-full bg-neutral-200 overflow-hidden flex">
                          <span class="h-full bg-green-500" [style.width.%]="feeBarPct(pay.paidAmount, m.amount)"></span>
                          <span class="h-full bg-blue-400" [style.width.%]="feeBarPct(pay.invoicedAmount - pay.paidAmount, m.amount)"></span>
                          <span class="h-full bg-amber-300" [style.width.%]="feeBarPct(pay.quotedAmount, m.amount)"></span>
                        </div>
                      }
                      <div class="flex flex-wrap gap-md text-xs">
                        <span class="flex items-center gap-2xs"><span class="w-2 h-2 rounded-full bg-green-500"></span>Paid {{ pay.paidAmount | number: '1.2-2' }}</span>
                        <span class="flex items-center gap-2xs"><span class="w-2 h-2 rounded-full bg-blue-400"></span>Invoiced, unpaid {{ (pay.invoicedAmount - pay.paidAmount) | number: '1.2-2' }}</span>
                        <span class="flex items-center gap-2xs"><span class="w-2 h-2 rounded-full bg-amber-300"></span>Quoted, not invoiced {{ pay.quotedAmount | number: '1.2-2' }}</span>
                      </div>
                    }
                  </div>
                  @if (feeWarning(); as warning) {
                    <p class="text-xs text-amber-600 mt-xs">⚠ {{ warning }}</p>
                  }
                }
              </div>
            }

            @if (category === 'StaffCost') {
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Payee</label>
                @if (selectedPayee(); as payee) {
                  <button type="button" class="input-field w-full flex items-center justify-between text-left" (click)="showPicker.set(true)">
                    <span class="text-neutral-900">{{ payee.name }}</span>
                    <div class="flex gap-2xs">
                      @for (role of payee.roles; track role) {
                        <span class="text-xs px-sm py-2xs rounded bg-neutral-100 text-neutral-600">{{ role }}</span>
                      }
                    </div>
                  </button>
                } @else {
                  <button type="button" class="input-field w-full text-left text-neutral-500" (click)="showPicker.set(true)">Search for a person…</button>
                }
              </div>
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Payment type</label>
                <select class="input-field" name="payeeType" [(ngModel)]="payeeType">
                  @for (t of payeeTypes; track t) {
                    <option [value]="t">{{ t }}</option>
                  }
                </select>
              </div>
            }

            <div class="grid grid-cols-2 gap-sm">
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Amount</label>
                <input class="input-field" type="number" min="0.01" step="1" name="amount" [(ngModel)]="amount" />
              </div>
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Incurred date</label>
                <input class="input-field" type="date" name="incurredDate" [(ngModel)]="incurredDate" />
              </div>
            </div>

            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Description (optional)</label>
              <textarea class="input-field" rows="2" name="description" [(ngModel)]="description"></textarea>
            </div>

            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Receipt (optional)</label>
              @if (editing()?.hasReceipt) {
                <p class="text-xs text-neutral-500 mb-xs">A receipt is already attached. Choosing a new file replaces it.</p>
              }
              <app-file-picker-field label="Attach receipt" accept=".pdf,.jpg,.jpeg,.png" (fileChange)="receiptFile = $event" />
            </div>

            @if (error()) {
              <p class="text-sm text-primary-500">{{ error() }}</p>
            }

            <div class="flex justify-end gap-sm pt-sm">
              <button type="button" class="btn-secondary" (click)="goBack()">Cancel</button>
              <button type="submit" class="btn-primary" [disabled]="saving() || amount <= 0 || !incurredDate || (category === 'StaffCost' && !payeeId)">
                {{ saving() ? 'Saving…' : editingId ? 'Save' : 'Create' }}
              </button>
            </div>
          </form>
        </div>
      }
    </div>

    @if (showPicker()) {
      <app-payee-picker-modal
        [participants]="participants()"
        (cancel)="showPicker.set(false)"
        (select)="onPayeeSelected($event)"
      />
    }

    @if (showMilestonePicker()) {
      <app-milestone-picker-modal
        [milestones]="milestones()"
        [paymentStatuses]="milestonePaymentStatuses()"
        (cancel)="showMilestonePicker.set(false)"
        (select)="onMilestonePicked($event)"
      />
    }
  `
})
export class ExpenseFormPageComponent implements OnInit {
  workspaceId = '';
  jobId = '';
  editingId: string | null = null;

  milestones = signal<Milestone[]>([]);
  participants = signal<JobParticipant[]>([]);
  editing = signal<Expense | null>(null);

  categories = EXPENSE_CATEGORIES;
  payeeTypes = PAYEE_TYPES;
  category: ExpenseCategory = 'Other';
  payeeId: string | null = null;
  payeeName: string | null = null;
  payeeType: PayeeType = 'Salary';
  milestoneId: string | null = null;
  amount = 0;
  incurredDate = '';
  description = '';
  receiptFile: File | null = null;

  loading = signal(false);
  saving = signal(false);
  error = signal('');
  showPicker = signal(false);
  showMilestonePicker = signal(false);
  milestonePaymentStatuses = signal<Record<string, MilestonePaymentStatus>>({});

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private expenseService: ExpenseService,
    private jobService: JobService,
    private milestoneService: MilestoneService
  ) {}

  ngOnInit(): void {
    this.workspaceId = this.route.snapshot.paramMap.get('id') ?? '';
    this.jobId = this.route.snapshot.paramMap.get('jobId') ?? '';
    this.editingId = this.route.snapshot.paramMap.get('expenseId');
    const presetMilestoneId = this.route.snapshot.queryParamMap.get('milestoneId');

    this.milestoneService.list(this.workspaceId, this.jobId).subscribe({
      next: milestones => {
        this.milestones.set(milestones);
        milestones.forEach(m => {
          this.milestoneService.getPaymentStatus(this.workspaceId, this.jobId, m.milestoneId).subscribe({
            next: status => this.milestonePaymentStatuses.update(map => ({ ...map, [m.milestoneId]: status }))
          });
        });
      }
    });
    this.jobService.getEffectiveParticipants(this.workspaceId, this.jobId).subscribe({ next: p => this.participants.set(p) });

    if (this.editingId) {
      this.loading.set(true);
      this.expenseService.getAll(this.workspaceId, this.jobId).subscribe({
        next: expenses => {
          const found = expenses.find(e => e.expenseId === this.editingId) ?? null;
          this.editing.set(found);
          if (found) {
            this.category = found.category;
            this.amount = found.amount;
            this.incurredDate = found.incurredDate.substring(0, 10);
            this.description = found.description ?? '';
            this.payeeId = found.payeeId;
            this.payeeName = found.payeeName;
            this.payeeType = found.payeeType ?? 'Salary';
            this.milestoneId = found.milestoneId;
          } else {
            this.error.set('Expense not found.');
          }
          this.loading.set(false);
        },
        error: err => {
          this.error.set(err.error?.message ?? 'Could not load expense.');
          this.loading.set(false);
        }
      });
    } else {
      this.incurredDate = new Date().toISOString().substring(0, 10);
      this.milestoneId = presetMilestoneId;
    }
  }

  onMilestonePicked(m: Milestone | null): void {
    this.milestoneId = m?.milestoneId ?? null;
    this.showMilestonePicker.set(false);
  }

  feeBarPct(amount: number, fee: number): number {
    if (!fee) return 0;
    return Math.max(0, Math.min(100, (amount / fee) * 100));
  }

  selectedMilestoneStatus(): MilestonePaymentStatus | null {
    return this.milestoneId ? this.milestonePaymentStatuses()[this.milestoneId] ?? null : null;
  }

  selectedMilestone(): Milestone | null {
    return this.milestones().find(m => m.milestoneId === this.milestoneId) ?? null;
  }

  selectedPayee(): PayeeOption | null {
    if (!this.payeeId) return null;
    const match = this.participants().filter(p => p.personId === this.payeeId);
    if (match.length === 0) return this.payeeName ? { personId: this.payeeId, name: this.payeeName, email: null, roles: [] } : null;
    return { personId: this.payeeId, name: `${match[0].firstName} ${match[0].lastName}`, email: match[0].email, roles: [...new Set(match.map(m => m.role))] };
  }

  onPayeeSelected(payee: PayeeOption): void {
    this.payeeId = payee.personId;
    this.payeeName = payee.name;
    this.showPicker.set(false);
  }

  feeWarning(): string | null {
    const m = this.selectedMilestone();
    if (!m?.amount || this.amount <= 0) return null;
    if (this.amount > m.amount) return `This expense (${this.amount.toFixed(2)}) exceeds the milestone fee (${m.amount.toFixed(2)}).`;
    return null;
  }

  goBack(): void {
    this.router.navigate(['/app/workspace', this.workspaceId, 'jobs', this.jobId]);
  }

  submit(): void {
    if (this.amount <= 0 || !this.incurredDate || (this.category === 'StaffCost' && !this.payeeId)) return;
    this.error.set('');
    this.saving.set(true);

    const request: ExpenseRequest = {
      category: this.category,
      amount: this.amount,
      description: this.description || undefined,
      incurredDate: this.incurredDate,
      payeeId: this.category === 'StaffCost' ? this.payeeId! : undefined,
      payeeType: this.category === 'StaffCost' ? this.payeeType : undefined,
      milestoneId: this.milestoneId ?? undefined
    };

    const save$ = this.editingId
      ? this.expenseService.update(this.workspaceId, this.jobId, this.editingId, request)
      : this.expenseService.create(this.workspaceId, this.jobId, request);

    save$.subscribe({
      next: expense => {
        if (this.receiptFile) {
          this.expenseService.uploadReceipt(this.workspaceId, this.jobId, expense.expenseId, this.receiptFile).subscribe({
            next: () => { this.saving.set(false); this.goBack(); },
            error: () => { this.saving.set(false); this.goBack(); }
          });
        } else {
          this.saving.set(false);
          this.goBack();
        }
      },
      error: err => {
        this.saving.set(false);
        this.error.set(err.error?.message ?? 'Could not save expense.');
      }
    });
  }
}
