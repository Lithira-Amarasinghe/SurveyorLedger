import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Installment } from '../../core/billing.service';

@Component({
  selector: 'app-installment-editor',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div>
      <div class="flex items-center justify-between mb-xs">
        <label class="block text-xs font-medium text-neutral-700">Payment schedule (optional)</label>
        @if (items.length > 0) {
          <span class="text-xs" [class.text-primary-500]="scheduledTotal() !== invoiceTotal">
            Scheduled: {{ scheduledTotal() | number: '1.2-2' }} / {{ invoiceTotal | number: '1.2-2' }}
          </span>
        }
      </div>
      <div class="space-y-sm">
        @for (item of items; track $index; let i = $index) {
          <div class="flex gap-sm items-start">
            <input
              class="input-field w-28"
              type="number"
              min="0.01"
              step="0.01"
              placeholder="Amount"
              [ngModel]="item.amount"
              (ngModelChange)="updateItem(i, 'amount', $event)"
              [name]="'inst-amount-' + i"
            />
            <input
              class="input-field flex-1"
              type="date"
              [ngModel]="item.dueDate"
              (ngModelChange)="updateItem(i, 'dueDate', $event)"
              [name]="'inst-due-' + i"
            />
            <button type="button" class="text-primary-500 hover:text-primary-600 px-sm py-sm" (click)="removeItem(i)" title="Remove installment">✕</button>
          </div>
        }
      </div>
      <button type="button" class="text-xs text-primary-500 hover:text-primary-600 mt-sm" (click)="addItem()">+ Add installment</button>
    </div>
  `
})
export class InstallmentEditorComponent {
  @Input() items: Installment[] = [];
  @Input() invoiceTotal = 0;
  @Output() itemsChange = new EventEmitter<Installment[]>();

  addItem(): void {
    this.itemsChange.emit([...this.items, { amount: 0, dueDate: new Date().toISOString().substring(0, 10) }]);
  }

  removeItem(index: number): void {
    this.itemsChange.emit(this.items.filter((_, i) => i !== index));
  }

  updateItem(index: number, field: keyof Installment, value: string | number): void {
    const updated = this.items.map((item, i) => (i === index ? { ...item, [field]: field === 'amount' ? Number(value) : value } : item));
    this.itemsChange.emit(updated);
  }

  scheduledTotal(): number {
    return this.items.reduce((sum, item) => sum + item.amount, 0);
  }
}
