import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Installment, Invoice, InvoiceRequest, InvoiceService, LineItem } from '../../../../core/billing.service';
import { Job, JobService } from '../../../../core/job.service';
import { BillingRecipientPickerComponent } from '../../../../shared/billing-recipient-picker/billing-recipient-picker.component';
import { LineItemEditorComponent } from '../../../../shared/line-item-editor/line-item-editor.component';
import { InstallmentEditorComponent } from '../../../../shared/installment-editor/installment-editor.component';

@Component({
  selector: 'app-invoice-form-modal',
  standalone: true,
  imports: [CommonModule, FormsModule, BillingRecipientPickerComponent, LineItemEditorComponent, InstallmentEditorComponent],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-lg" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">{{ editing ? 'Edit invoice' : 'New invoice' }}</h2>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          @if (!fixedJobId) {
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Job</label>
              <select class="input-field" name="jobId" [(ngModel)]="jobId" (ngModelChange)="onJobChange()" [disabled]="!!editing">
                <option [ngValue]="null">Select a job…</option>
                @for (job of jobs(); track job.jobId) {
                  <option [ngValue]="job.jobId">{{ job.jobNumber }} · {{ job.title }}</option>
                }
              </select>
            </div>
          }

          @if (isLocked()) {
            <div class="rounded bg-amber-50 border border-amber-200 px-md py-sm text-xs text-amber-800">
              This invoice already has recorded payments - the amount is locked. Only the due date can be changed. Cancel and reissue if the amount is wrong.
            </div>
          }

          <fieldset [disabled]="isLocked()" class="space-y-md" [class.opacity-60]="isLocked()">
            <app-billing-recipient-picker [workspaceId]="workspaceId" [jobId]="jobId" [value]="clientId" (valueChange)="clientId = $event" />

            <app-line-item-editor [items]="lineItems" (itemsChange)="lineItems = $event" />

            <div class="grid grid-cols-2 gap-sm">
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Tax rate (%)</label>
                <input class="input-field" type="number" min="0" step="0.01" name="taxRate" [(ngModel)]="taxRatePercent" />
              </div>
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Discount</label>
                <input class="input-field" type="number" min="0" step="0.01" name="discount" [(ngModel)]="discountAmount" />
              </div>
            </div>

            <app-installment-editor [items]="installments" [invoiceTotal]="invoiceTotal()" (itemsChange)="installments = $event" />

            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Status</label>
              <select class="input-field" name="status" [(ngModel)]="status">
                <option value="Draft">Draft</option>
                <option value="Sent">Sent</option>
                <option value="Cancelled">Cancelled</option>
              </select>
              @if (editing && (editing.status === 'PartiallyPaid' || editing.status === 'Paid')) {
                <p class="text-xs text-neutral-500 mt-xs">
                  Current status is {{ editing.status }} - set automatically from payments and can't be changed here.
                </p>
              }
            </div>
          </fieldset>

          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Due date</label>
            <input class="input-field" type="date" name="dueDate" [(ngModel)]="dueDate" />
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading() || !jobId || !clientId || lineItems.length === 0 || !installmentsValid()">
              {{ loading() ? 'Saving…' : editing ? 'Save' : 'Create' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class InvoiceFormModalComponent implements OnInit {
  @Input() workspaceId = '';
  @Input() fixedJobId: string | null = null;
  @Input() editing: Invoice | null = null;
  @Output() cancel = new EventEmitter<void>();
  @Output() saved = new EventEmitter<Invoice>();

  jobs = signal<Job[]>([]);
  jobId: string | null = null;
  clientId: string | null = null;
  lineItems: LineItem[] = [{ description: '', quantity: 1, unitPrice: 0 }];
  taxRatePercent = 0;
  discountAmount = 0;
  dueDate = '';
  status: 'Draft' | 'Sent' | 'Cancelled' = 'Draft';
  installments: Installment[] = [];
  loading = signal(false);
  error = signal('');

  constructor(private invoiceService: InvoiceService, private jobService: JobService) {}

  ngOnInit(): void {
    if (this.fixedJobId) {
      this.jobId = this.fixedJobId;
    } else {
      this.jobService.list(this.workspaceId).subscribe({ next: jobs => this.jobs.set(jobs) });
    }

    if (this.editing) {
      this.jobId = this.editing.jobId;
      this.clientId = this.editing.clientId;
      this.lineItems = this.editing.lineItems.length > 0 ? [...this.editing.lineItems] : [{ description: '', quantity: 1, unitPrice: 0 }];
      this.taxRatePercent = this.editing.taxRatePercent;
      this.discountAmount = this.editing.discountAmount;
      this.dueDate = this.editing.dueDate ? this.editing.dueDate.substring(0, 10) : '';
      this.status = this.editing.status === 'Draft' || this.editing.status === 'Sent' || this.editing.status === 'Cancelled' ? this.editing.status : 'Sent';
      this.installments = this.editing.installments.map(i => ({ amount: i.amount, dueDate: i.dueDate.substring(0, 10) }));
    }
  }

  onJobChange(): void {
    this.clientId = null;
  }

  isLocked(): boolean {
    return !!this.editing && this.editing.amountPaid > 0;
  }

  invoiceTotal(): number {
    const subtotal = this.lineItems.reduce((sum, li) => sum + li.quantity * li.unitPrice, 0);
    return subtotal - this.discountAmount + (subtotal * this.taxRatePercent) / 100;
  }

  installmentsValid(): boolean {
    if (this.installments.length === 0) return true;
    return Math.round(this.installments.reduce((sum, i) => sum + i.amount, 0) * 100) === Math.round(this.invoiceTotal() * 100);
  }

  submit(): void {
    if (!this.jobId || !this.clientId || this.lineItems.length === 0 || !this.installmentsValid()) return;
    this.error.set('');
    this.loading.set(true);

    const request: InvoiceRequest = {
      clientId: this.clientId,
      jobId: this.jobId,
      lineItems: this.lineItems,
      taxRatePercent: this.taxRatePercent,
      discountAmount: this.discountAmount,
      dueDate: this.dueDate || undefined,
      status: this.status,
      installments: this.installments
    };

    const save$ = this.editing
      ? this.invoiceService.update(this.workspaceId, this.editing.invoiceId, request)
      : this.invoiceService.create(this.workspaceId, request);

    save$.subscribe({
      next: invoice => {
        this.loading.set(false);
        this.saved.emit(invoice);
      },
      error: err => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not save invoice.');
      }
    });
  }
}
