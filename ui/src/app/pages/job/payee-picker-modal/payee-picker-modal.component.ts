import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { JobParticipant } from '../../../core/job.service';

export interface PayeeOption {
  personId: string;
  name: string;
  email: string | null;
  roles: string[];
}

/** Search-and-pick modal for choosing who an expense was paid to. Participants come in
 * one row per job role (a person with Surveyor + Finance appears twice) - this groups
 * by person so each name is listed once with all their roles shown as badges, same line-item
 * list pattern as the milestone/quotation source picker. */
@Component({
  selector: 'app-payee-picker-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-md max-h-[80vh] flex flex-col" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900 mb-md">Select payee</h2>

        <input class="input-field mb-md" type="text" placeholder="Search by name…" [(ngModel)]="query" autofocus />

        <div class="flex-1 overflow-y-auto space-y-xs">
          @if (filteredOptions().length === 0) {
            <p class="text-sm text-neutral-500">No matching participants.</p>
          } @else {
            @for (p of filteredOptions(); track p.personId) {
              <button
                type="button"
                class="w-full flex items-center justify-between gap-sm px-md py-sm rounded bg-neutral-50 hover:bg-neutral-100 text-left"
                (click)="select.emit(p)"
              >
                <div class="min-w-0">
                  <span class="text-sm text-neutral-900 block truncate">{{ p.name }}</span>
                  @if (p.email) {
                    <span class="text-xs text-neutral-500 block truncate">{{ p.email }}</span>
                  }
                </div>
                <div class="flex gap-2xs flex-shrink-0">
                  @for (role of p.roles; track role) {
                    <span class="text-xs px-sm py-2xs rounded bg-neutral-200 text-neutral-700">{{ role }}</span>
                  }
                </div>
              </button>
            }
          }
        </div>

        <div class="flex justify-end pt-md">
          <button type="button" class="btn-secondary text-xs" (click)="cancel.emit()">Close</button>
        </div>
      </div>
    </div>
  `
})
export class PayeePickerModalComponent {
  @Input() set participants(value: JobParticipant[]) {
    const byPerson = new Map<string, PayeeOption>();
    for (const p of value) {
      const existing = byPerson.get(p.personId);
      if (existing) {
        if (!existing.roles.includes(p.role)) existing.roles.push(p.role);
      } else {
        byPerson.set(p.personId, { personId: p.personId, name: `${p.firstName} ${p.lastName}`, email: p.email, roles: [p.role] });
      }
    }
    this.options = [...byPerson.values()].sort((a, b) => a.name.localeCompare(b.name));
  }
  @Output() cancel = new EventEmitter<void>();
  @Output() select = new EventEmitter<PayeeOption>();

  options: PayeeOption[] = [];
  query = '';

  filteredOptions(): PayeeOption[] {
    const q = this.query.trim().toLowerCase();
    if (!q) return this.options;
    return this.options.filter(p => p.name.toLowerCase().includes(q) || p.email?.toLowerCase().includes(q));
  }
}
