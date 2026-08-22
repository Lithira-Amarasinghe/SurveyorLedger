import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { WorkspaceService, Workspace } from '../../core/workspace.service';
import { JobService, AccessibleJob } from '../../core/job.service';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';
import { CurrentOrganizationService } from '../../core/current-organization.service';
import { CreateWorkspaceModalComponent } from '../workspace/create-modal/create-modal.component';

type ViewMode = 'both' | 'jobs' | 'workspace';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, CreateWorkspaceModalComponent],
  template: `
    <div class="p-lg max-w-4xl mx-auto">
      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Your workspaces</h1>
        <div class="flex items-center gap-sm">
          <div class="flex rounded border border-neutral-200 overflow-hidden text-xs">
            <button type="button" class="px-md py-xs" [class.bg-primary-50]="viewMode() === 'both'" [class.text-primary-600]="viewMode() === 'both'" (click)="viewMode.set('both')">Both</button>
            <button type="button" class="px-md py-xs border-l border-neutral-200" [class.bg-primary-50]="viewMode() === 'jobs'" [class.text-primary-600]="viewMode() === 'jobs'" (click)="viewMode.set('jobs')">Jobs</button>
            <button type="button" class="px-md py-xs border-l border-neutral-200" [class.bg-primary-50]="viewMode() === 'workspace'" [class.text-primary-600]="viewMode() === 'workspace'" (click)="viewMode.set('workspace')">Workspace</button>
          </div>
          <button class="btn-primary" (click)="modalOpen.set(true)">New workspace</button>
        </div>
      </div>

      @if (notFoundError()) {
        <div class="mb-lg text-sm text-primary-600 bg-primary-50 border border-primary-100 rounded px-md py-sm">
          That workspace couldn't be found, or you don't have access to it.
        </div>
      }

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else {
        @if (viewMode() !== 'jobs') {
          @if (workspaces().length === 0) {
            <div class="card text-center text-sm text-neutral-500">No workspaces yet. Create one to get started.</div>
          } @else {
            <div class="grid gap-md sm:grid-cols-2">
              @for (workspace of workspaces(); track workspace.workspaceId) {
                <button type="button" class="card text-left hover:border-primary-300 transition-colors" (click)="openWorkspace(workspace)">
                  <div class="flex items-center justify-between">
                    <span class="font-medium text-neutral-900">{{ workspace.name }}</span>
                    <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ workspace.tier }}</span>
                  </div>
                  @if (workspace.description) {
                    <p class="text-sm text-neutral-600 mt-xs">{{ workspace.description }}</p>
                  }
                  <p class="text-xs text-neutral-500 mt-sm">Role: {{ workspace.roles.join(', ') }}</p>
                </button>
              }
            </div>
          }
        }

        @if (viewMode() === 'both') {
          <h2 class="text-sm font-semibold text-neutral-900 mt-xl mb-md">Jobs (direct access)</h2>
          @if (directAccessJobs().length === 0) {
            <div class="card text-center text-sm text-neutral-500">No jobs outside your workspaces.</div>
          } @else {
            <div class="grid gap-sm">
              @for (job of directAccessJobs(); track job.jobId) {
                <button type="button" class="card text-left hover:border-primary-300 transition-colors flex items-center justify-between" (click)="openJob(job)">
                  <div>
                    <span class="font-medium text-neutral-900">{{ job.jobNumber }} · {{ job.title }}</span>
                    <p class="text-xs text-neutral-500">{{ job.workspaceName }}</p>
                  </div>
                  <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ job.status }}</span>
                </button>
              }
            </div>
          }
        }

        @if (viewMode() === 'jobs') {
          <div class="flex flex-wrap gap-sm mb-md">
            <select class="input-field py-xs text-xs w-40" [(ngModel)]="workspaceFilter">
              <option value="">All workspaces</option>
              @for (name of availableWorkspaceNames(); track name) {
                <option [value]="name">{{ name }}</option>
              }
            </select>
            <select class="input-field py-xs text-xs w-32" [(ngModel)]="statusFilter">
              <option value="">All statuses</option>
              @for (status of availableStatuses(); track status) {
                <option [value]="status">{{ status }}</option>
              }
            </select>
            <select class="input-field py-xs text-xs w-36" [(ngModel)]="accessTypeFilter">
              <option value="">All access types</option>
              @for (type of availableAccessTypes(); track type) {
                <option [value]="type">{{ type }}</option>
              }
            </select>
          </div>
          @if (filteredJobs().length === 0) {
            <div class="card text-center text-sm text-neutral-500">No jobs match these filters.</div>
          } @else {
            <div class="grid gap-sm">
              @for (job of filteredJobs(); track job.jobId) {
                <button type="button" class="card text-left hover:border-primary-300 transition-colors flex items-center justify-between" (click)="openJob(job)">
                  <div>
                    <span class="font-medium text-neutral-900">{{ job.jobNumber }} · {{ job.title }}</span>
                    <p class="text-xs text-neutral-500">{{ job.workspaceName }} · {{ job.accessScopeType }}</p>
                  </div>
                  <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ job.status }}</span>
                </button>
              }
            </div>
          }
        }
      }
    </div>

    @if (modalOpen()) {
      <app-create-workspace-modal (cancel)="modalOpen.set(false)" (created)="onCreated($event)" />
    }
  `
})
export class DashboardComponent implements OnInit {
  allWorkspaces = signal<Workspace[]>([]);
  allJobs = signal<AccessibleJob[]>([]);
  loading = signal(true);
  modalOpen = signal(false);
  notFoundError = signal(false);
  viewMode = signal<ViewMode>('both');

  workspaceFilter = '';
  statusFilter = '';
  accessTypeFilter = '';

  workspaces = computed(() => {
    const orgId = this.currentOrg.current()?.id;
    return orgId ? this.allWorkspaces().filter(w => w.organizationId === orgId) : this.allWorkspaces();
  });

  jobs = computed(() => {
    const orgId = this.currentOrg.current()?.id;
    return orgId ? this.allJobs().filter(j => j.organizationId === orgId) : this.allJobs();
  });

  /** Job-level-only jobs - no workspace access, shown separately below the Workspaces section. */
  directAccessJobs = computed(() => this.jobs().filter(j => j.accessScopeType === 'Job'));

  availableWorkspaceNames = computed(() => [...new Set(this.jobs().map(j => j.workspaceName))].sort());
  availableStatuses = computed(() => [...new Set(this.jobs().map(j => j.status))].sort());
  availableAccessTypes = computed(() => [...new Set(this.jobs().map(j => j.accessScopeType))].sort());

  filteredJobs = computed(() =>
    this.jobs().filter(j =>
      (!this.workspaceFilter || j.workspaceName === this.workspaceFilter) &&
      (!this.statusFilter || j.status === this.statusFilter) &&
      (!this.accessTypeFilter || j.accessScopeType === this.accessTypeFilter)
    )
  );

  constructor(
    private workspaceService: WorkspaceService,
    private jobService: JobService,
    private currentWorkspace: CurrentWorkspaceService,
    protected currentOrg: CurrentOrganizationService,
    private route: ActivatedRoute,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.currentWorkspace.clear();
    this.notFoundError.set(
      this.route.snapshot.queryParamMap.get('error') === 'workspace-not-found' ||
      this.route.snapshot.queryParamMap.get('error') === 'job-not-found'
    );
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    let remaining = 2;
    const done = () => { if (--remaining === 0) this.loading.set(false); };

    this.workspaceService.list().subscribe({
      next: (workspaces) => { this.allWorkspaces.set(workspaces); done(); },
      error: () => done()
    });
    this.jobService.getMine().subscribe({
      next: (jobs) => { this.allJobs.set(jobs); done(); },
      error: () => done()
    });
  }

  openWorkspace(workspace: Workspace): void {
    this.router.navigate(['/app/workspace', workspace.workspaceId]);
  }

  /** accessScopeType === 'Job' (leaf-level, nothing above confirmed) -> minimal job-only
   * route. Anything else ('Workspace' today, 'Organization' later) -> the full workspace
   * shell - same leaf-vs-not-leaf rule as the spec's "Scaling mechanism". */
  openJob(job: AccessibleJob): void {
    if (job.accessScopeType === 'Job') {
      this.router.navigate(['/app/job', job.workspaceId, job.jobId]);
    } else {
      this.router.navigate(['/app/workspace', job.workspaceId, 'jobs', job.jobId]);
    }
  }

  onCreated(workspace: Workspace): void {
    this.modalOpen.set(false);
    this.router.navigate(['/app/workspace', workspace.workspaceId]);
  }
}
