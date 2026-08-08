import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { WorkspaceService, Role } from '../../core/workspace.service';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';

@Component({
  selector: 'app-roles',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="p-lg max-w-4xl mx-auto">
      <h1 class="text-lg font-semibold text-neutral-900 mb-lg">Roles</h1>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (error()) {
        <div class="card text-sm text-primary-500">
          {{ error() }}
          <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
        </div>
      } @else {
        <div class="space-y-md">
          @for (role of roles(); track role.id) {
            <div class="card">
              <div class="flex items-center justify-between">
                <h2 class="text-sm font-semibold text-neutral-900">{{ role.name }}</h2>
                <span class="text-xs text-neutral-500">{{ role.permissions.length }} permission{{ role.permissions.length === 1 ? '' : 's' }}</span>
              </div>
              @if (role.description) {
                <p class="text-xs text-neutral-500 mt-xs">{{ role.description }}</p>
              }
              @if (role.permissions.length) {
                <ul class="mt-sm flex flex-wrap gap-xs">
                  @for (p of role.permissions; track p.name) {
                    <li class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600" [title]="p.description">
                      {{ p.name }}
                    </li>
                  }
                </ul>
              } @else {
                <p class="text-xs text-neutral-500 mt-sm">No permissions assigned.</p>
              }
            </div>
          }
        </div>
      }
    </div>
  `
})
export class RolesComponent implements OnInit {
  workspaceId = '';
  roles = signal<Role[]>([]);
  loading = signal(true);
  error = signal('');

  constructor(
    private workspaceService: WorkspaceService,
    private currentWorkspace: CurrentWorkspaceService
  ) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    this.workspaceService.getRoles(this.workspaceId).subscribe({
      next: (roles) => {
        this.roles.set(roles);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err.error?.message ?? 'Could not load roles.');
        this.loading.set(false);
      }
    });
  }
}
