import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LineItem } from '../../core/billing.service';
import { Milestone } from '../../core/milestone.service';

export interface QuotationLineSource {
  id: string;
  quotationNumber: string;
  description: string;
  milestoneId?: string;
  remainingAmount: number;
}

@Component({
  selector: 'app-line-item-editor',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div>
      <label class="block text-xs font-medium text-neutral-700 mb-xs">Line items</label>
      <div class="space-y-sm">
        @for (item of items; track $index; let i = $index) {
          <div class="flex gap-sm items-start">
            <input
              class="input-field flex-1"
              placeholder="Description"
              [ngModel]="item.description"
              (ngModelChange)="updateItem(i, 'description', $event)"
              [name]="'desc-' + i"
            />
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
            @if (quotationLines.length > 0) {
              <select
                class="input-field w-48"
                [ngModel]="item.quotationLineId ?? ''"
                (ngModelChange)="onSourceChange(i, $event)"
                [name]="'source-' + i"
              >
                <option value="">No quotation (direct)</option>
                @for (source of quotationLines; track source.id) {
                  <option [value]="source.id">{{ source.quotationNumber }}: {{ source.description }} — {{ source.remainingAmount | number: '1.2-2' }} remaining</option>
                }
              </select>
            }
            @if (milestones.length > 0) {
              <select
                class="input-field w-40"
                [ngModel]="item.milestoneId ?? ''"
                (ngModelChange)="updateItem(i, 'milestoneId', $event || undefined)"
                [name]="'milestone-' + i"
                [disabled]="!!item.quotationLineId"
              >
                <option value="">No milestone (other fee)</option>
                @for (m of milestones; track m.milestoneId) {
                  <option [value]="m.milestoneId">{{ m.title }}</option>
                }
              </select>
            }
            <button type="button" class="text-primary-500 hover:text-primary-600 px-sm py-sm" (click)="removeItem(i)" title="Remove line">✕</button>
          </div>
        }
      </div>
      <button type="button" class="text-xs text-primary-500 hover:text-primary-600 mt-sm" (click)="addItem()">+ Add line item</button>

      <div class="mt-sm text-sm text-neutral-700 text-right">
        Subtotal: {{ subtotal() | number: '1.2-2' }}
      </div>
    </div>
  `
})
export class LineItemEditorComponent {
  @Input() items: LineItem[] = [];
  @Input() milestones: Milestone[] = [];
  @Input() quotationLines: QuotationLineSource[] = [];
  @Output() itemsChange = new EventEmitter<LineItem[]>();

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

  onSourceChange(index: number, quotationLineId: string): void {
    const updated = this.items.map((item, i) => {
      if (i !== index) return item;
      if (!quotationLineId) {
        const { quotationLineId: _drop, ...rest } = item;
        return rest;
      }
      const source = this.quotationLines.find(s => s.id === quotationLineId);
      if (!source) return item;
      return {
        ...item,
        quotationLineId: source.id,
        description: source.description,
        milestoneId: source.milestoneId
      };
    });
    this.itemsChange.emit(updated);
  }

  sourceRemainingFor(item: LineItem): number {
    return this.quotationLines.find(s => s.id === item.quotationLineId)?.remainingAmount ?? 0;
  }

  subtotal(): number {
    return this.items.reduce((sum, item) => sum + item.quantity * item.unitPrice, 0);
  }
}
