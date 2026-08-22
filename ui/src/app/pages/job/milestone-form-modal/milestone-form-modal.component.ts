import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Milestone, MilestoneService } from '../../../core/milestone.service';

@Component({
  selector: 'app-milestone-form-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-md" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">{{ editing ? 'Edit milestone' : 'New milestone' }}</h2>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Title</label>
            <input class="input-field" type="text" name="title" [(ngModel)]="title" autofocus />
          </div>

          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Description (optional)</label>
            <textarea class="input-field" rows="2" name="description" [(ngModel)]="description"></textarea>
          </div>

          <div class="grid grid-cols-2 gap-sm">
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Due date (optional)</label>
              <input class="input-field" type="date" name="dueDate" [(ngModel)]="dueDate" />
            </div>
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Fee amount (optional)</label>
              <input class="input-field" type="number" min="0" step="1" placeholder="0.00" name="amount" [(ngModel)]="amount" />
              <span class="block text-xs text-neutral-500 mt-2xs">What this milestone is worth to bill against, if any.</span>
            </div>
          </div>

          @if (editing && editing.committedAmount > 0) {
            <p class="text-xs rounded bg-neutral-50 px-md py-sm" [class.text-primary-500]="amount !== null && amount < editing.committedAmount" [class.text-neutral-600]="amount === null || amount >= editing.committedAmount">
              {{ editing.committedAmount | number: '1.2-2' }} already quoted and/or invoiced against this milestone.
              @if (amount !== null && amount < editing.committedAmount) {
                Lowering the fee below that will make it inconsistent with what's already quoted or invoiced.
              }
            </p>
          }

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading() || !title.trim()">
              {{ loading() ? 'Saving…' : editing ? 'Save' : 'Add milestone' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class MilestoneFormModalComponent implements OnInit {
  @Input() workspaceId = '';
  @Input() jobId = '';
  @Input() editing: Milestone | null = null;
  @Output() cancel = new EventEmitter<void>();
  @Output() saved = new EventEmitter<Milestone>();

  title = '';
  description = '';
  dueDate = '';
  amount: number | null = null;
  loading = signal(false);
  error = signal('');

  constructor(private milestoneService: MilestoneService) {}

  ngOnInit(): void {
    if (this.editing) {
      this.title = this.editing.title;
      this.description = this.editing.description ?? '';
      this.dueDate = this.editing.dueDate ? this.editing.dueDate.substring(0, 10) : '';
      this.amount = this.editing.amount;
    }
  }

  submit(): void {
    if (!this.title.trim()) {
      this.error.set('Title is required.');
      return;
    }
    this.error.set('');
    this.loading.set(true);

    const request = {
      title: this.title.trim(),
      description: this.description.trim() || null,
      dueDate: this.dueDate || null,
      amount: this.amount
    };

    const save$ = this.editing
      ? this.milestoneService.update(this.workspaceId, this.jobId, this.editing.milestoneId, request)
      : this.milestoneService.create(this.workspaceId, this.jobId, request);

    save$.subscribe({
      next: milestone => {
        this.loading.set(false);
        this.saved.emit(milestone);
      },
      error: err => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not save milestone.');
      }
    });
  }
}
