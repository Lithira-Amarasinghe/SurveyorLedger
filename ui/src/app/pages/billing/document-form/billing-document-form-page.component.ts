import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import {
  Installment, Invoice, InvoiceRequest, InvoiceService, LineItem,
  Quotation, QuotationRequest, QuotationService, QuotationStatus
} from '../../../core/billing.service';
import { Job, JobService } from '../../../core/job.service';
import { Milestone, MilestoneService } from '../../../core/milestone.service';
import { BillingRecipientPickerComponent } from '../../../shared/billing-recipient-picker/billing-recipient-picker.component';
import { LineItemEditorComponent, QuotationLineSource } from '../../../shared/line-item-editor/line-item-editor.component';
import { InstallmentEditorComponent } from '../../../shared/installment-editor/installment-editor.component';

type DocumentType = 'invoice' | 'quotation';

@Component({
  selector: 'app-billing-document-form-page',
  standalone: true,
  imports: [CommonModule, FormsModule, BillingRecipientPickerComponent, LineItemEditorComponent, InstallmentEditorComponent],
  template: `
    <div class="p-lg max-w-2xl mx-auto space-y-lg">
      <div class="flex items-center justify-between">
        <h1 class="text-lg font-semibold text-neutral-900">
          {{ editingId ? 'Edit ' + documentType : 'New ' + documentType }}
        </h1>
        <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" (click)="goBack()">← Back</button>
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else {
        <div class="card">
          <form class="space-y-md" (ngSubmit)="submit()">
            @if (!jobId) {
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Job</label>
                <select class="input-field" name="jobId" [(ngModel)]="jobId" (ngModelChange)="onJobChange()">
                  <option [ngValue]="null">Select a job…</option>
                  @for (job of jobs(); track job.jobId) {
                    <option [ngValue]="job.jobId">{{ job.jobNumber }} · {{ job.title }}</option>
                  }
                </select>
              </div>
            }

            @if (isLocked()) {
              <div class="rounded bg-amber-50 border border-amber-200 px-md py-sm text-xs text-amber-800">
                This invoice already has recorded payments - the amount is locked. Only the due date can be changed.
              </div>
            }

            <fieldset [disabled]="isLocked()" class="space-y-md" [class.opacity-60]="isLocked()">
              <app-billing-recipient-picker [workspaceId]="workspaceId" [jobId]="jobId" [value]="clientId" (valueChange)="clientId = $event" />

              <app-line-item-editor
                [items]="lineItems"
                [milestones]="milestones()"
                [quotationLines]="documentType === 'invoice' ? quotationLines() : []"
                (itemsChange)="lineItems = $event"
              />

              <div class="grid grid-cols-2 gap-sm">
                <div>
                  <label class="block text-xs font-medium text-neutral-700 mb-xs">Tax rate (%)</label>
                  <input class="input-field" type="number" min="0" step="0.01" name="taxRate" [(ngModel)]="taxRatePercent" />
                </div>
                @if (documentType === 'invoice') {
                  <div>
                    <label class="block text-xs font-medium text-neutral-700 mb-xs">Discount</label>
                    <input class="input-field" type="number" min="0" step="0.01" name="discount" [(ngModel)]="discountAmount" />
                  </div>
                } @else {
                  <div>
                    <label class="block text-xs font-medium text-neutral-700 mb-xs">Valid until</label>
                    <input class="input-field" type="date" name="validUntil" [(ngModel)]="validUntil" />
                  </div>
                }
              </div>

              @if (documentType === 'invoice') {
                <app-installment-editor [items]="installments" [invoiceTotal]="documentTotal()" (itemsChange)="installments = $event" />
              }

              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Status</label>
                <select class="input-field" name="status" [(ngModel)]="status">
                  @if (documentType === 'invoice') {
                    <option value="Draft">Draft</option>
                    <option value="Sent">Sent</option>
                    <option value="Cancelled">Cancelled</option>
                  } @else {
                    <option value="Draft">Draft</option>
                    <option value="Sent">Sent</option>
                    <option value="Accepted">Accepted</option>
                    <option value="Rejected">Rejected</option>
                    <option value="Expired">Expired</option>
                  }
                </select>
              </div>
            </fieldset>

            @if (documentType === 'invoice') {
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Due date</label>
                <input class="input-field" type="date" name="dueDate" [(ngModel)]="dueDate" />
              </div>
            }

            @if (error()) {
              <p class="text-sm text-primary-500">{{ error() }}</p>
            }

            <div class="flex justify-end gap-sm pt-sm">
              <button type="button" class="btn-secondary" (click)="goBack()">Cancel</button>
              <button type="submit" class="btn-primary" [disabled]="saving() || !jobId || !clientId || lineItems.length === 0">
                {{ saving() ? 'Saving…' : editingId ? 'Save' : 'Create' }}
              </button>
            </div>
          </form>
        </div>
      }
    </div>
  `
})
export class BillingDocumentFormPageComponent implements OnInit {
  documentType!: DocumentType;
  workspaceId = '';
  jobId: string | null = null;
  editingId: string | null = null;

  jobs = signal<Job[]>([]);
  milestones = signal<Milestone[]>([]);
  quotationLines = signal<QuotationLineSource[]>([]);

  clientId: string | null = null;
  lineItems: LineItem[] = [{ description: '', quantity: 1, unitPrice: 0 }];
  taxRatePercent = 0;
  discountAmount = 0;
  validUntil = '';
  dueDate = '';
  status = 'Draft';
  installments: Installment[] = [];

  loading = signal(false);
  saving = signal(false);
  error = signal('');

  private editingInvoice: Invoice | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private invoiceService: InvoiceService,
    private quotationService: QuotationService,
    private jobService: JobService,
    private milestoneService: MilestoneService
  ) {}

  ngOnInit(): void {
    this.documentType = this.route.snapshot.data['documentType'];
    this.workspaceId = this.route.snapshot.paramMap.get('id') ?? '';
    this.editingId = this.route.snapshot.paramMap.get('invoiceId') ?? this.route.snapshot.paramMap.get('quotationId');
    this.jobId = this.route.snapshot.queryParamMap.get('jobId');

    if (!this.editingId) {
      this.jobs.set([]);
      if (!this.jobId) {
        this.jobService.list(this.workspaceId).subscribe({ next: jobs => this.jobs.set(jobs) });
      } else {
        this.loadMilestones();
        this.loadQuotationLines();
      }
    }

    const fromQuotationId = this.route.snapshot.queryParamMap.get('fromQuotation');
    const milestoneId = this.route.snapshot.queryParamMap.get('milestoneId');

    if (this.editingId && this.documentType === 'invoice') {
      this.loading.set(true);
      this.invoiceService.getById(this.workspaceId, this.editingId).subscribe({
        next: invoice => {
          this.jobId = invoice.jobId;
          this.loadMilestones();
          this.loadQuotationLines();
          this.clientId = invoice.clientId;
          this.lineItems = invoice.lineItems.length > 0 ? [...invoice.lineItems] : [{ description: '', quantity: 1, unitPrice: 0 }];
          this.taxRatePercent = invoice.taxRatePercent;
          this.status = invoice.status;
          this.editingInvoice = invoice;
          this.discountAmount = invoice.discountAmount;
          this.dueDate = invoice.dueDate ? invoice.dueDate.substring(0, 10) : '';
          this.installments = invoice.installments.map(i => ({ amount: i.amount, dueDate: i.dueDate.substring(0, 10) }));
          this.loading.set(false);
        },
        error: err => {
          this.error.set(err.error?.message ?? 'Could not load document.');
          this.loading.set(false);
        }
      });
    } else if (this.editingId && this.documentType === 'quotation') {
      this.loading.set(true);
      this.quotationService.getById(this.workspaceId, this.editingId).subscribe({
        next: quotation => {
          this.jobId = quotation.jobId;
          this.loadMilestones();
          this.clientId = quotation.clientId;
          this.lineItems = quotation.lineItems.length > 0 ? [...quotation.lineItems] : [{ description: '', quantity: 1, unitPrice: 0 }];
          this.taxRatePercent = quotation.taxRatePercent;
          this.status = quotation.status;
          this.validUntil = quotation.validUntil ? quotation.validUntil.substring(0, 10) : '';
          this.loading.set(false);
        },
        error: err => {
          this.error.set(err.error?.message ?? 'Could not load document.');
          this.loading.set(false);
        }
      });
    } else if (fromQuotationId && this.documentType === 'invoice') {
      this.loading.set(true);
      this.quotationService.getById(this.workspaceId, fromQuotationId).subscribe({
        next: quotation => {
          this.clientId = quotation.clientId;
          this.jobId = quotation.jobId;
          this.loadMilestones();
          this.loadQuotationLines();
          this.lineItems = [{ description: '', quantity: 1, unitPrice: 0 }];
          this.loading.set(false);
        },
        error: err => {
          this.error.set(err.error?.message ?? 'Could not load quotation.');
          this.loading.set(false);
        }
      });
    } else if (milestoneId && this.jobId) {
      this.loading.set(true);
      this.milestoneService.getById(this.workspaceId, this.jobId, milestoneId).subscribe({
        next: milestone => {
          const amount = milestone.remainingAmount ?? milestone.amount ?? 0;
          this.lineItems = [{ description: milestone.title, quantity: 1, unitPrice: amount, milestoneId: milestone.milestoneId }];
          if (this.documentType === 'invoice') this.loadQuotationLines();
          this.loading.set(false);
        },
        error: () => this.loading.set(false)
      });
    }
  }

  private loadMilestones(): void {
    if (!this.jobId) return;
    this.milestoneService.list(this.workspaceId, this.jobId).subscribe({ next: milestones => this.milestones.set(milestones) });
  }

  private loadQuotationLines(): void {
    if (!this.jobId || this.documentType !== 'invoice') return;
    this.quotationService.search(this.workspaceId, undefined, this.jobId).subscribe({
      next: quotations => {
        const sources: QuotationLineSource[] = [];
        for (const q of quotations) {
          if (q.status === 'Rejected' || q.status === 'Expired') continue;
          for (const li of q.lineItems) {
            const remaining = li.remainingAmount ?? 0;
            if (!li.id || remaining <= 0) continue;
            sources.push({ id: li.id, quotationNumber: q.number, description: li.description, milestoneId: li.milestoneId, remainingAmount: remaining });
          }
        }
        this.quotationLines.set(sources);
      }
    });
  }

  onJobChange(): void {
    this.clientId = null;
    this.loadMilestones();
    this.loadQuotationLines();
  }

  isLocked(): boolean {
    return this.documentType === 'invoice' && !!this.editingInvoice && this.editingInvoice.amountPaid > 0;
  }

  documentTotal(): number {
    const subtotal = this.lineItems.reduce((sum, li) => sum + li.quantity * li.unitPrice, 0);
    const discount = this.documentType === 'invoice' ? this.discountAmount : 0;
    return subtotal - discount + (subtotal * this.taxRatePercent) / 100;
  }

  goBack(): void {
    if (this.jobId) {
      this.router.navigate(['/app/workspace', this.workspaceId, 'jobs', this.jobId]);
    } else {
      this.router.navigate(['/app/workspace', this.workspaceId, 'billing', this.documentType === 'invoice' ? 'invoices' : 'quotations']);
    }
  }

  submit(): void {
    if (!this.jobId || !this.clientId || this.lineItems.length === 0) return;
    this.error.set('');
    this.saving.set(true);

    if (this.documentType === 'invoice') {
      const request: InvoiceRequest = {
        clientId: this.clientId,
        jobId: this.jobId,
        lineItems: this.lineItems,
        taxRatePercent: this.taxRatePercent,
        discountAmount: this.discountAmount,
        dueDate: this.dueDate || undefined,
        status: this.status as 'Draft' | 'Sent' | 'Cancelled',
        installments: this.installments
      };
      const save$ = this.editingId
        ? this.invoiceService.update(this.workspaceId, this.editingId, request)
        : this.invoiceService.create(this.workspaceId, request);
      save$.subscribe({
        next: () => { this.saving.set(false); this.goBack(); },
        error: err => { this.saving.set(false); this.error.set(err.error?.message ?? 'Could not save invoice.'); }
      });
    } else {
      const request: QuotationRequest = {
        clientId: this.clientId,
        jobId: this.jobId,
        lineItems: this.lineItems,
        taxRatePercent: this.taxRatePercent,
        validUntil: this.validUntil || undefined,
        status: this.status as QuotationStatus
      };
      const save$ = this.editingId
        ? this.quotationService.update(this.workspaceId, this.editingId, request)
        : this.quotationService.create(this.workspaceId, request);
      save$.subscribe({
        next: () => { this.saving.set(false); this.goBack(); },
        error: err => { this.saving.set(false); this.error.set(err.error?.message ?? 'Could not save quotation.'); }
      });
    }
  }
}
