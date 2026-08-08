import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { WorkspaceService, Workspace } from '../../core/workspace.service';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';
import { CreateWorkspaceModalComponent } from '../workspace/create-modal/create-modal.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, CreateWorkspaceModalComponent],
  template: `
    <div class="p-lg max-w-4xl mx-auto">
      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Your workspaces</h1>
        <button class="btn-primary" (click)="modalOpen.set(true)">New workspace</button>
      </div>

      @if (notFoundError()) {
        <div class="mb-lg text-sm text-primary-600 bg-primary-50 border border-primary-100 rounded px-md py-sm">
          That workspace couldn't be found, or you don't have access to it.
        </div>
      }

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (workspaces().length === 0) {
        <div class="card text-center text-sm text-neutral-500">No workspaces yet. Create one to get started.</div>
      } @else {
        <div class="grid gap-md sm:grid-cols-2">
          @for (workspace of workspaces(); track workspace.workspaceId) {
            <button
              type="button"
              class="card text-left hover:border-primary-300 transition-colors"
              (click)="open(workspace)"
            >
              <div class="flex items-center justify-between">
                <span class="font-medium text-neutral-900">{{ workspace.name }}</span>
                <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ workspace.tier }}</span>
              </div>
              @if (workspace.description) {
                <p class="text-sm text-neutral-600 mt-xs">{{ workspace.description }}</p>
              }
              <p class="text-xs text-neutral-500 mt-sm">Role: {{ workspace.role }}</p>
            </button>
          }
        </div>
      }
    </div>

    @if (modalOpen()) {
      <app-create-workspace-modal (cancel)="modalOpen.set(false)" (created)="onCreated($event)" />
    }
  `
})
export class DashboardComponent implements OnInit {
  workspaces = signal<Workspace[]>([]);
  loading = signal(true);
  modalOpen = signal(false);
  notFoundError = signal(false);

  constructor(
    private workspaceService: WorkspaceService,
    private currentWorkspace: CurrentWorkspaceService,
    private route: ActivatedRoute,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.currentWorkspace.clear();
    this.notFoundError.set(this.route.snapshot.queryParamMap.get('error') === 'workspace-not-found');
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.workspaceService.list().subscribe({
      next: (workspaces) => {
        this.workspaces.set(workspaces);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  open(workspace: Workspace): void {
    this.router.navigate(['/app/workspace', workspace.workspaceId]);
  }

  onCreated(workspace: Workspace): void {
    this.modalOpen.set(false);
    this.router.navigate(['/app/workspace', workspace.workspaceId]);
  }
}
