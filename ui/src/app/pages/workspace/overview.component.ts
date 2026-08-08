import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';

@Component({
  selector: 'app-workspace-overview',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="p-lg max-w-2xl mx-auto">
      @if (workspace(); as ws) {
        <div class="card space-y-md">
          <div class="flex items-center justify-between">
            <h1 class="text-lg font-semibold text-neutral-900">{{ ws.name }}</h1>
            <span class="text-xs px-sm py-xs rounded bg-primary-50 text-primary-600 font-medium">{{ ws.role }}</span>
          </div>
          <div>
            <p class="text-xs text-neutral-500">Description</p>
            <p class="text-sm text-neutral-900">{{ ws.description || '—' }}</p>
          </div>
          <div>
            <p class="text-xs text-neutral-500">Subscription tier</p>
            <p class="text-sm text-neutral-900">{{ ws.tier }}</p>
          </div>
          <div>
            <p class="text-xs text-neutral-500">Created</p>
            <p class="text-sm text-neutral-900">{{ ws.createdAt | date: 'mediumDate' }}</p>
          </div>
        </div>
      }
    </div>
  `
})
export class WorkspaceOverviewComponent {
  private currentWorkspace = inject(CurrentWorkspaceService);
  workspace = this.currentWorkspace.current;
}
