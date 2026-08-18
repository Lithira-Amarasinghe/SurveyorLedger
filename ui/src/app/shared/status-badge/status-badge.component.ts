import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

/** Status -> Tailwind color classes. Same palette convention across every billing
 * status this app has (Invoice/Quotation/Installment) - green for a settled/good
 * state, amber for in-progress/pending, red for something needing attention,
 * neutral for a terminal non-financial state (Draft/Cancelled/Rejected). Matches
 * how Stripe/QuickBooks/FreshBooks color invoice status pills. */
const STATUS_COLORS: Record<string, string> = {
  Draft: 'bg-neutral-100 text-neutral-600',
  Sent: 'bg-blue-50 text-blue-700',
  PartiallyPaid: 'bg-amber-50 text-amber-700',
  Pending: 'bg-amber-50 text-amber-700',
  Paid: 'bg-green-50 text-green-700',
  Accepted: 'bg-green-50 text-green-700',
  Overdue: 'bg-red-50 text-red-700',
  Cancelled: 'bg-neutral-100 text-neutral-500',
  Rejected: 'bg-red-50 text-red-700',
  Expired: 'bg-neutral-100 text-neutral-500'
};

@Component({
  selector: 'app-status-badge',
  standalone: true,
  imports: [CommonModule],
  template: `<span class="inline-block px-sm py-xs rounded text-xs font-medium" [class]="colorClass()">{{ status }}</span>`
})
export class StatusBadgeComponent {
  @Input({ required: true }) status = '';

  colorClass(): string {
    return STATUS_COLORS[this.status] ?? 'bg-neutral-100 text-neutral-600';
  }
}
