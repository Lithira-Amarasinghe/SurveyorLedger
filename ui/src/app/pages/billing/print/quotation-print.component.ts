import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { Quotation, QuotationService } from '../../../core/billing.service';
import { LetterheadHeaderComponent } from '../../../shared/letterhead-header/letterhead-header.component';

@Component({
  selector: 'app-quotation-print',
  standalone: true,
  imports: [CommonModule, LetterheadHeaderComponent],
  template: `
    @if (loading()) {
      <p class="p-lg text-sm text-neutral-500">Loading…</p>
    } @else if (quotation(); as quotation) {
      <div class="max-w-2xl mx-auto p-lg">
        <div class="flex justify-between items-start mb-lg print:hidden">
          <h1 class="text-lg font-semibold">Quotation {{ quotation.number }}</h1>
          <button type="button" class="btn-primary" (click)="print()">Print / Save as PDF</button>
        </div>

        <app-letterhead-header [workspaceId]="workspaceId" />

        <h1 class="text-xl font-semibold text-neutral-900">Quotation {{ quotation.number }}</h1>
        <p class="text-sm text-neutral-600">
          Status: {{ quotation.status }}
          @if (quotation.validUntil) { · Valid until {{ quotation.validUntil | date: 'mediumDate' }} }
          @if (quotation.revisionNumber > 0) { · Revision {{ quotation.revisionNumber }} }
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
            @for (item of quotation.lineItems; track $index) {
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
          <div class="flex justify-between w-56"><span>Subtotal</span><span>{{ quotation.subtotal | number: '1.2-2' }}</span></div>
          <div class="flex justify-between w-56"><span>Tax ({{ quotation.taxRatePercent }}%)</span><span>{{ quotation.total - quotation.subtotal | number: '1.2-2' }}</span></div>
          <div class="flex justify-between w-56 font-semibold text-neutral-900 border-t border-neutral-200 mt-xs pt-xs"><span>Total</span><span>{{ quotation.total | number: '1.2-2' }}</span></div>
        </div>
      </div>
    } @else if (error()) {
      <p class="p-lg text-sm text-primary-500">{{ error() }}</p>
    }
  `
})
export class QuotationPrintComponent implements OnInit {
  quotation = signal<Quotation | null>(null);
  loading = signal(true);
  error = signal('');
  workspaceId = '';

  constructor(private quotationService: QuotationService, private route: ActivatedRoute) {}

  ngOnInit(): void {
    this.workspaceId = this.route.snapshot.paramMap.get('id') ?? '';
    const quotationId = this.route.snapshot.paramMap.get('quotationId') ?? '';
    this.quotationService.getById(this.workspaceId, quotationId).subscribe({
      next: quotation => {
        this.quotation.set(quotation);
        this.loading.set(false);
      },
      error: err => {
        this.error.set(err.error?.message ?? 'Could not load quotation.');
        this.loading.set(false);
      }
    });
  }

  print(): void {
    window.print();
  }
}
