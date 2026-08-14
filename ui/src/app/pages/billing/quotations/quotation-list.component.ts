import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { Invoice, Quotation, QuotationService } from '../../../core/billing.service';
import { CurrentWorkspaceService } from '../../../core/current-workspace.service';
import { BillingTabsComponent } from '../billing-tabs.component';
import { QuotationFormModalComponent } from './quotation-form-modal/quotation-form-modal.component';
import { ConvertQuotationModalComponent } from './convert-modal/convert-quotation-modal.component';

@Component({
  selector: 'app-quotation-list',
  standalone: true,
  imports: [CommonModule, RouterLink, BillingTabsComponent, QuotationFormModalComponent, ConvertQuotationModalComponent],
  template: `
    <div class="p-lg max-w-5xl mx-auto">
      <app-billing-tabs [workspaceId]="workspaceId" active="quotations" />

      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Quotations</h1>
        <button class="btn-primary" (click)="openCreate()">New quotation</button>
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (error()) {
        <div class="card text-sm text-primary-500">
          {{ error() }}
          <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
        </div>
      } @else if (quotations().length === 0) {
        <div class="card text-center text-sm text-neutral-500">No quotations yet. Create one to get started.</div>
      } @else {
        <div class="card p-0 overflow-x-auto">
          <table class="w-full text-sm">
            <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
              <tr>
                <th class="text-left px-lg py-sm font-medium">Number</th>
                <th class="text-left px-lg py-sm font-medium">Total</th>
                <th class="text-left px-lg py-sm font-medium">Status</th>
                <th class="text-left px-lg py-sm font-medium">Valid until</th>
                <th class="px-lg py-sm"></th>
              </tr>
            </thead>
            <tbody>
              @for (quotation of quotations(); track quotation.quotationId) {
                <tr class="border-t border-neutral-200 hover:bg-neutral-50">
                  <td class="px-lg py-sm text-neutral-900 cursor-pointer" (click)="openEdit(quotation)">{{ quotation.number }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ quotation.total | number: '1.2-2' }}</td>
                  <td class="px-lg py-sm">
                    <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-700">{{ quotation.status }}</span>
                  </td>
                  <td class="px-lg py-sm text-neutral-600">{{ quotation.validUntil ? (quotation.validUntil | date: 'mediumDate') : '—' }}</td>
                  <td class="px-lg py-sm text-right">
                    <a
                      class="text-xs text-neutral-500 hover:text-neutral-700 mr-md"
                      [routerLink]="['/app/workspace', workspaceId, 'billing', 'quotations', quotation.quotationId, 'print']"
                      (click)="$event.stopPropagation()"
                    >Print</a>
                    @if (quotation.status === 'Draft' || quotation.status === 'Sent') {
                      <button class="text-xs text-primary-500 hover:text-primary-600" (click)="openConvert(quotation)">Convert to invoice</button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>

    @if (modalOpen()) {
      <app-quotation-form-modal [workspaceId]="workspaceId" [editing]="editingQuotation()" (cancel)="closeModal()" (saved)="onSaved()" />
    }
    @if (convertingQuotation(); as quotation) {
      <app-convert-quotation-modal [workspaceId]="workspaceId" [quotation]="quotation" (cancel)="convertingQuotation.set(null)" (converted)="onConverted($event)" />
    }
  `
})
export class QuotationListComponent implements OnInit {
  workspaceId = '';
  quotations = signal<Quotation[]>([]);
  loading = signal(true);
  error = signal('');
  modalOpen = signal(false);
  editingQuotation = signal<Quotation | null>(null);
  convertingQuotation = signal<Quotation | null>(null);

  constructor(private quotationService: QuotationService, private currentWorkspace: CurrentWorkspaceService, private router: Router) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    this.quotationService.search(this.workspaceId).subscribe({
      next: quotations => {
        this.quotations.set(quotations);
        this.loading.set(false);
      },
      error: err => {
        this.error.set(err.error?.message ?? 'Could not load quotations.');
        this.loading.set(false);
      }
    });
  }

  openCreate(): void {
    this.editingQuotation.set(null);
    this.modalOpen.set(true);
  }

  openEdit(quotation: Quotation): void {
    this.editingQuotation.set(quotation);
    this.modalOpen.set(true);
  }

  closeModal(): void {
    this.modalOpen.set(false);
  }

  onSaved(): void {
    this.modalOpen.set(false);
    this.fetch();
  }

  openConvert(quotation: Quotation): void {
    this.convertingQuotation.set(quotation);
  }

  onConverted(invoice: Invoice): void {
    this.convertingQuotation.set(null);
    this.router.navigate(['/app/workspace', this.workspaceId, 'billing', 'invoices']);
  }
}
