import { Component, EventEmitter, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Workspace, WorkspaceService } from '../../../core/workspace.service';

@Component({
  selector: 'app-create-workspace-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-md" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">New workspace</h2>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Name</label>
            <input class="input-field" type="text" name="name" [(ngModel)]="name" required />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Description</label>
            <textarea class="input-field" name="description" rows="3" [(ngModel)]="description"></textarea>
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Subscription tier</label>
            <select class="input-field" name="tier" [(ngModel)]="tier">
              <option value="Free">Free</option>
              <option value="Pro">Pro</option>
            </select>
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading()">
              {{ loading() ? 'Creating…' : 'Create' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class CreateWorkspaceModalComponent {
  @Output() cancel = new EventEmitter<void>();
  @Output() created = new EventEmitter<Workspace>();

  name = '';
  description = '';
  tier = 'Free';
  loading = signal(false);
  error = signal('');

  constructor(private workspaceService: WorkspaceService) {}

  submit(): void {
    this.error.set('');
    this.loading.set(true);
    this.workspaceService.create(this.name, this.description, this.tier).subscribe({
      next: (workspace) => {
        this.loading.set(false);
        this.created.emit(workspace);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not create workspace.');
      }
    });
  }
}
