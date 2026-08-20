import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LineItem } from '../../core/billing.service';
import { Milestone } from '../../core/milestone.service';

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
            @if (milestones.length > 0) {
              <select
                class="input-field w-40"
                [ngModel]="item.milestoneId ?? ''"
                (ngModelChange)="updateItem(i, 'milestoneId', $event || undefined)"
                [name]="'milestone-' + i"
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

  subtotal(): number {
    return this.items.reduce((sum, item) => sum + item.quantity * item.unitPrice, 0);
  }
}
