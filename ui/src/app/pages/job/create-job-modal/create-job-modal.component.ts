import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Job, JobService } from '../../../core/job.service';

@Component({
  selector: 'app-create-job-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-md" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">New job</h2>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Title</label>
            <input class="input-field" type="text" name="title" [(ngModel)]="title" required autofocus />
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading() || !title.trim()">
              {{ loading() ? 'Creating…' : 'Create' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class CreateJobModalComponent {
  @Input() workspaceId = '';
  @Output() cancel = new EventEmitter<void>();
  @Output() created = new EventEmitter<Job>();

  title = '';
  loading = signal(false);
  error = signal('');

  constructor(private jobService: JobService) {}

  submit(): void {
    if (!this.title.trim()) return;
    this.error.set('');
    this.loading.set(true);
    this.jobService.create(this.workspaceId, this.title.trim()).subscribe({
      next: (job) => {
        this.loading.set(false);
        this.created.emit(job);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not create job.');
      }
    });
  }
}
