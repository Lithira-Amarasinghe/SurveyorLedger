import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LineItem } from '../../core/billing.service';
import { Milestone } from '../../core/milestone.service';
import { BillingSourcePickerComponent } from '../billing-source-picker/billing-source-picker.component';

export interface QuotationLineSource {
  id: string;
  quotationId: string;
  quotationNumber: string;
  description: string;
  milestoneId?: string;
  remainingAmount: number;
}

@Component({
  selector: 'app-line-item-editor',
  standalone: true,
  imports: [CommonModule, FormsModule, BillingSourcePickerComponent],
  template: `
    <div>
      <label class="block text-xs font-medium text-neutral-700 mb-xs">Line items</label>
      <div class="space-y-sm">
        @for (item of items; track $index; let i = $index) {
          <div class="flex gap-sm items-start">
            <div class="flex-1">
              <input
                class="input-field w-full"
                placeholder="Description"
                [ngModel]="item.description"
                (ngModelChange)="updateItem(i, 'description', $event)"
                [name]="'desc-' + i"
              />
              @if (sourceLabel(item); as label) {
                <span class="block text-xs text-neutral-500 mt-2xs">{{ label }}</span>
              }
            </div>
            @if (!item.milestoneId && !item.quotationLineId) {
              <input
                class="input-field w-20"
                type="number"
                min="0"
                step="0.01"
                placeholder="Qty"
                [ngModel]="item.quantity"
                (ngModelChange)="updateItem(i, 'quantity', $event)"
                [name]="'qty-' + i"
              />
            } @else {
              <span class="input-field w-20 flex items-center justify-center text-neutral-500">×1</span>
            }
            <div>
              <input
                class="input-field w-28"
                type="number"
                min="0"
                step="0.01"
                placeholder="Unit price"
                [ngModel]="item.unitPrice"
                (ngModelChange)="updateItem(i, 'unitPrice', $event)"
                [name]="'price-' + i"
              />
              @if (item.quotationLineId) {
                <span class="block text-xs text-neutral-500 mt-2xs">max {{ sourceRemainingFor(item) | number: '1.2-2' }} remaining</span>
              }
            </div>
            <button type="button" class="text-primary-500 hover:text-primary-600 px-sm py-sm" (click)="removeItem(i)" title="Remove line">✕</button>
          </div>
        }
      </div>
      <div class="flex gap-md mt-sm">
        <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="addItem()">+ Add line item</button>
        @if (milestones.length > 0 || quotationLines.length > 0) {
          <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="showPicker.set(true)">+ Add from…</button>
        }
      </div>

      <div class="mt-sm text-sm text-neutral-700 text-right">
        Subtotal: {{ subtotal() | number: '1.2-2' }}
      </div>
    </div>

    @if (showPicker()) {
      <app-billing-source-picker
        [milestones]="milestones"
        [quotationLines]="quotationLines"
        [existingItems]="items"
        (cancel)="showPicker.set(false)"
        (addLines)="onAddLines($event)"
      />
    }
  `
})
export class LineItemEditorComponent {
  @Input() items: LineItem[] = [];
  @Input() milestones: Milestone[] = [];
  @Input() quotationLines: QuotationLineSource[] = [];
  @Output() itemsChange = new EventEmitter<LineItem[]>();

  showPicker = signal(false);

  addItem(): void {
    this.itemsChange.emit([...this.items, { description: '', quantity: 1, unitPrice: 0 }]);
  }

  removeItem(index: number): void {
    this.itemsChange.emit(this.items.filter((_, i) => i !== index));
  }

  updateItem(index: number, field: keyof LineItem, value: string | number | undefined): void {
    const updated = this.items.map((item, i) => {
      if (i !== index) return item;
      if (field === 'description' || field === 'milestoneId') return { ...item, [field]: value };
      return { ...item, [field]: Number(value) };
    });
    this.itemsChange.emit(updated);
  }

  onAddLines(newLines: LineItem[]): void {
    this.itemsChange.emit([...this.items, ...newLines]);
    this.showPicker.set(false);
  }

  sourceLabel(item: LineItem): string | null {
    if (item.quotationLineId) {
      const source = this.quotationLines.find(s => s.id === item.quotationLineId);
      return source ? `From ${source.quotationNumber}` : 'From a quotation line';
    }
    if (item.milestoneId) {
      const m = this.milestones.find(m => m.milestoneId === item.milestoneId);
      return m ? `Milestone: ${m.title}` : null;
    }
    return null;
  }

  sourceRemainingFor(item: LineItem): number {
    return this.quotationLines.find(s => s.id === item.quotationLineId)?.remainingAmount ?? 0;
  }

  subtotal(): number {
    return this.items.reduce((sum, item) => sum + item.quantity * item.unitPrice, 0);
  }
}
