import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { LineItem } from '../../core/billing.service';
import { Milestone, MilestonePaymentStatus } from '../../core/milestone.service';
import { BillingSourcePickerComponent } from '../billing-source-picker/billing-source-picker.component';
import { ToastService } from '../../core/toast.service';

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
  imports: [CommonModule, FormsModule, DragDropModule, BillingSourcePickerComponent],
  template: `
    <div>
      <label class="block text-xs font-medium text-neutral-700 mb-xs">Line items</label>
      <div cdkDropList class="space-y-sm" (cdkDropListDropped)="onDropped($event)">
        @for (item of items; track $index; let i = $index) {
          <div cdkDrag class="bg-white">
            <div class="flex gap-sm items-center">
              <span cdkDragHandle class="cursor-grab text-neutral-400 select-none flex-shrink-0 px-2xs">⠿</span>
              <div class="flex-1">
                <input
                  class="input-field w-full"
                  placeholder="Description"
                  [ngModel]="item.description"
                  (ngModelChange)="updateItem(i, 'description', $event)"
                  [name]="'desc-' + i"
                />
              </div>
              @if (!item.milestoneId && !item.quotationLineId) {
                <input
                  class="input-field w-20"
                  type="number"
                  min="0"
                  step="1"
                  placeholder="Qty"
                  [ngModel]="item.quantity"
                  (ngModelChange)="updateItem(i, 'quantity', $event)"
                  [name]="'qty-' + i"
                />
              } @else {
                <span class="w-20 flex-shrink-0"></span>
              }
              <input
                class="input-field w-28"
                type="number"
                min="0"
                step="1"
                placeholder="Unit price"
                [ngModel]="item.unitPrice"
                (ngModelChange)="updateItem(i, 'unitPrice', $event)"
                [name]="'price-' + i"
              />
              <button type="button" class="text-primary-500 hover:text-primary-600 px-sm py-sm flex-shrink-0" (click)="removeItem(i)" title="Remove line">✕</button>
            </div>
            @if (sourceLabel(item); as label) {
              <span class="block text-xs text-neutral-500 mt-2xs pl-lg">{{ label }}@if (item.quotationLineId) { &middot; max {{ sourceRemainingFor(item) | number: '1.2-2' }} remaining }</span>
            }
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
        [paymentStatuses]="milestonePaymentStatuses"
        [quotationLines]="quotationLines"
        [existingItems]="items"
        [showQuotationsTab]="allowQuotationsTab"
        (cancel)="showPicker.set(false)"
        (addLines)="onAddLines($event)"
      />
    }
  `
})
export class LineItemEditorComponent {
  @Input() items: LineItem[] = [];
  @Input() milestones: Milestone[] = [];
  @Input() milestonePaymentStatuses: Record<string, MilestonePaymentStatus> = {};
  @Input() quotationLines: QuotationLineSource[] = [];
  @Input() allowQuotationsTab = true;
  @Output() itemsChange = new EventEmitter<LineItem[]>();

  showPicker = signal(false);

  constructor(private toast: ToastService) {}

  addItem(): void {
    if (this.items.some(i => i.description.trim() === '' && !i.milestoneId && !i.quotationLineId)) {
      this.toast.error('Fill in the blank line item before adding another.');
      return;
    }
    this.itemsChange.emit([...this.items, { description: '', quantity: 1, unitPrice: 0 }]);
  }

  onDropped(event: CdkDragDrop<LineItem[]>): void {
    const reordered = [...this.items];
    moveItemInArray(reordered, event.previousIndex, event.currentIndex);
    this.itemsChange.emit(reordered);
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
    const kept = this.items.filter(i => i.description.trim() !== '' || i.milestoneId || i.quotationLineId);
    this.itemsChange.emit([...kept, ...newLines]);
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
