import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LineItem, Quotation, QuotationRequest, QuotationService, QuotationStatus } from '../../../../core/billing.service';
import { Job, JobService } from '../../../../core/job.service';
import { BillingRecipientPickerComponent } from '../../../../shared/billing-recipient-picker/billing-recipient-picker.component';
import { LineItemEditorComponent } from '../../../../shared/line-item-editor/line-item-editor.component';

@Component({
  selector: 'app-quotation-form-modal',
  standalone: true,
  imports: [CommonModule, FormsModule, BillingRecipientPickerComponent, LineItemEditorComponent],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-lg" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">{{ editing ? 'Edit quotation' : 'New quotation' }}</h2>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Job</label>
            <select class="input-field" name="jobId" [(ngModel)]="jobId" (ngModelChange)="onJobChange()" [disabled]="!!editing">
              <option [ngValue]="null">Select a job…</option>
              @for (job of jobs(); track job.jobId) {
                <option [ngValue]="job.jobId">{{ job.jobNumber }} · {{ job.title }}</option>
              }
            </select>
          </div>

          <app-billing-recipient-picker [workspaceId]="workspaceId" [jobId]="jobId" [value]="clientId" (valueChange)="clientId = $event" />

          <app-line-item-editor [items]="lineItems" (itemsChange)="lineItems = $event" />

          <div class="grid grid-cols-2 gap-sm">
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Tax rate (%)</label>
              <input class="input-field" type="number" min="0" step="0.01" name="taxRate" [(ngModel)]="taxRatePercent" />
            </div>
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Valid until</label>
              <input class="input-field" type="date" name="validUntil" [(ngModel)]="validUntil" />
            </div>
          </div>

          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Status</label>
            <select class="input-field" name="status" [(ngModel)]="status">
              <option value="Draft">Draft</option>
              <option value="Sent">Sent</option>
              <option value="Accepted">Accepted</option>
              <option value="Rejected">Rejected</option>
              <option value="Expired">Expired</option>
            </select>
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading() || !jobId || !clientId || lineItems.length === 0">
              {{ loading() ? 'Saving…' : editing ? 'Save' : 'Create' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class QuotationFormModalComponent implements OnInit {
  @Input() workspaceId = '';
  @Input() editing: Quotation | null = null;
  @Output() cancel = new EventEmitter<void>();
  @Output() saved = new EventEmitter<Quotation>();

  jobs = signal<Job[]>([]);
  jobId: string | null = null;
  clientId: string | null = null;
  lineItems: LineItem[] = [{ description: '', quantity: 1, unitPrice: 0 }];
  taxRatePercent = 0;
  validUntil = '';
  status: QuotationStatus = 'Draft';
  loading = signal(false);
  error = signal('');

  constructor(private quotationService: QuotationService, private jobService: JobService) {}

  ngOnInit(): void {
    this.jobService.list(this.workspaceId).subscribe({ next: jobs => this.jobs.set(jobs) });

    if (this.editing) {
      this.jobId = this.editing.jobId;
      this.clientId = this.editing.clientId;
      this.lineItems = this.editing.lineItems.length > 0 ? [...this.editing.lineItems] : [{ description: '', quantity: 1, unitPrice: 0 }];
      this.taxRatePercent = this.editing.taxRatePercent;
      this.validUntil = this.editing.validUntil ? this.editing.validUntil.substring(0, 10) : '';
      this.status = this.editing.status;
    }
  }

  onJobChange(): void {
    this.clientId = null;
  }

  submit(): void {
    if (!this.jobId || !this.clientId || this.lineItems.length === 0) return;
    this.error.set('');
    this.loading.set(true);

    const request: QuotationRequest = {
      clientId: this.clientId,
      jobId: this.jobId,
      lineItems: this.lineItems,
      taxRatePercent: this.taxRatePercent,
      validUntil: this.validUntil || undefined,
      status: this.status
    };

    const save$ = this.editing
      ? this.quotationService.update(this.workspaceId, this.editing.quotationId, request)
      : this.quotationService.create(this.workspaceId, request);

    save$.subscribe({
      next: quotation => {
        this.loading.set(false);
        this.saved.emit(quotation);
      },
      error: err => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not save quotation.');
      }
    });
  }
}
