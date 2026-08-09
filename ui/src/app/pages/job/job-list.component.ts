import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Job, JobService } from '../../core/job.service';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';
import { CreateJobModalComponent } from './create-job-modal/create-job-modal.component';

const STATUS_STYLES: Record<string, string> = {
  Draft: 'bg-neutral-100 text-neutral-600',
  Scheduled: 'bg-blue-100 text-blue-700',
  InProgress: 'bg-amber-100 text-amber-700',
  Completed: 'bg-green-100 text-green-700',
  Cancelled: 'bg-neutral-200 text-neutral-500'
};

@Component({
  selector: 'app-job-list',
  standalone: true,
  imports: [CommonModule, CreateJobModalComponent],
  template: `
    <div class="p-lg max-w-4xl mx-auto">
      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Jobs</h1>
        <button class="btn-primary" (click)="modalOpen.set(true)">New job</button>
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (error()) {
        <div class="card text-sm text-primary-500">
          {{ error() }}
          <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
        </div>
      } @else if (jobs().length === 0) {
        <div class="card text-center text-sm text-neutral-500">No jobs yet. Create one to get started.</div>
      } @else {
        <div class="card p-0 overflow-x-auto">
          <table class="w-full text-sm">
            <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
              <tr>
                <th class="text-left px-lg py-sm font-medium">Job #</th>
                <th class="text-left px-lg py-sm font-medium">Title</th>
                <th class="text-left px-lg py-sm font-medium">Status</th>
                <th class="text-left px-lg py-sm font-medium">Created</th>
              </tr>
            </thead>
            <tbody>
              @for (job of jobs(); track job.jobId) {
                <tr class="border-t border-neutral-200 cursor-pointer hover:bg-neutral-50" (click)="open(job)">
                  <td class="px-lg py-sm text-neutral-900 font-mono text-xs">{{ job.jobNumber }}</td>
                  <td class="px-lg py-sm text-neutral-900">{{ job.title }}</td>
                  <td class="px-lg py-sm">
                    <span class="text-xs px-sm py-xs rounded" [class]="statusClass(job.status)">{{ job.status }}</span>
                  </td>
                  <td class="px-lg py-sm text-neutral-600">{{ job.createdAt | date: 'mediumDate' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>

    @if (modalOpen()) {
      <app-create-job-modal [workspaceId]="workspaceId" (cancel)="modalOpen.set(false)" (created)="onCreated($event)" />
    }
  `
})
export class JobListComponent implements OnInit {
  workspaceId = '';
  jobs = signal<Job[]>([]);
  loading = signal(true);
  error = signal('');
  modalOpen = signal(false);

  constructor(
    private jobService: JobService,
    private currentWorkspace: CurrentWorkspaceService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    this.jobService.list(this.workspaceId).subscribe({
      next: (jobs) => {
        this.jobs.set(jobs);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err.error?.message ?? 'Could not load jobs.');
        this.loading.set(false);
      }
    });
  }

  statusClass(status: string): string {
    return STATUS_STYLES[status] ?? STATUS_STYLES['Draft'];
  }

  open(job: Job): void {
    this.router.navigate(['/app/workspace', this.workspaceId, 'jobs', job.jobId]);
  }

  onCreated(job: Job): void {
    this.modalOpen.set(false);
    this.router.navigate(['/app/workspace', this.workspaceId, 'jobs', job.jobId]);
  }
}
