import { Component, EventEmitter, Input, OnChanges, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { JobService, JobParticipant } from '../../core/job.service';

@Component({
  selector: 'app-billing-recipient-picker',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div>
      <label class="block text-xs font-medium text-neutral-700 mb-xs">Client</label>
      @if (!jobId) {
        <p class="text-xs text-neutral-500">Select a job first.</p>
      } @else if (loading()) {
        <p class="text-xs text-neutral-500">Loading job participants…</p>
      } @else if (eligible().length === 0) {
        <p class="text-xs text-neutral-500">
          No Client or Finance participant on this job yet. Add one from the job's Participants tab first.
        </p>
      } @else {
        <div class="border border-neutral-200 rounded divide-y divide-neutral-200">
          @for (p of eligible(); track p.personId) {
            <button
              type="button"
              class="w-full text-left px-md py-sm hover:bg-neutral-50"
              [class.bg-primary-50]="value === p.personId"
              (click)="select(p)"
            >
              <span class="text-sm text-neutral-900">{{ p.firstName }} {{ p.lastName }}</span>
              <span class="block text-xs text-neutral-500">{{ p.role }}{{ p.email ? ' · ' + p.email : '' }}</span>
            </button>
          }
        </div>
      }
    </div>
  `
})
export class BillingRecipientPickerComponent implements OnChanges {
  @Input() workspaceId = '';
  @Input() jobId: string | null = null;
  @Input() value: string | null = null;
  @Output() valueChange = new EventEmitter<string | null>();

  eligible = signal<JobParticipant[]>([]);
  loading = signal(false);

  constructor(private jobService: JobService) {}

  ngOnChanges(): void {
    if (!this.jobId) {
      this.eligible.set([]);
      return;
    }
    this.loading.set(true);
    this.jobService.getParticipants(this.workspaceId, this.jobId).subscribe({
      next: participants => {
        this.eligible.set(participants.filter(p => p.role === 'Client' || p.role === 'Finance'));
        this.loading.set(false);
      },
      error: () => {
        this.eligible.set([]);
        this.loading.set(false);
      }
    });
  }

  select(p: JobParticipant): void {
    this.valueChange.emit(p.personId);
  }
}
