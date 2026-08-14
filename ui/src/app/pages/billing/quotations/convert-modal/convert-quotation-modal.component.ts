import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Invoice, Quotation, QuotationService } from '../../../../core/billing.service';

@Component({
  selector: 'app-convert-quotation-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-sm" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">Convert to invoice</h2>
        <p class="text-sm text-neutral-600 mt-xs">
          Quotation {{ quotation.number }} will become a new invoice with the same line items.
        </p>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Due date</label>
            <input class="input-field" type="date" name="dueDate" [(ngModel)]="dueDate" />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Discount amount</label>
            <input class="input-field" type="number" min="0" step="0.01" name="discount" [(ngModel)]="discountAmount" />
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading()">{{ loading() ? 'Converting…' : 'Convert' }}</button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class ConvertQuotationModalComponent {
  @Input() workspaceId = '';
  @Input() quotation!: Quotation;
  @Output() cancel = new EventEmitter<void>();
  @Output() converted = new EventEmitter<Invoice>();

  dueDate = '';
  discountAmount = 0;
  loading = signal(false);
  error = signal('');

  constructor(private quotationService: QuotationService) {}

  submit(): void {
    this.error.set('');
    this.loading.set(true);
    this.quotationService.convertToInvoice(this.workspaceId, this.quotation.quotationId, { dueDate: this.dueDate || undefined, discountAmount: this.discountAmount }).subscribe({
      next: invoice => {
        this.loading.set(false);
        this.converted.emit(invoice);
      },
      error: err => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not convert quotation.');
      }
    });
  }
}
