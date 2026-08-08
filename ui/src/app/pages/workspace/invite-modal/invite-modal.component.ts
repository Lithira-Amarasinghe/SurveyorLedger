import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { InvitationService } from '../../../core/invitation.service';

@Component({
  selector: 'app-invite-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-md" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">Invite member</h2>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Email</label>
            <input class="input-field" type="email" name="email" [(ngModel)]="email" required />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Role</label>
            <select class="input-field" name="role" [(ngModel)]="role">
              <option value="Admin">Admin</option>
              <option value="Manager">Manager</option>
              <option value="Surveyor">Surveyor</option>
              <option value="Client">Client</option>
            </select>
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading()">
              {{ loading() ? 'Sending…' : 'Send invite' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class InviteModalComponent {
  @Input({ required: true }) workspaceId!: string;
  @Output() cancel = new EventEmitter<void>();
  @Output() created = new EventEmitter<void>();

  email = '';
  role = 'Surveyor';
  loading = signal(false);
  error = signal('');

  constructor(private invitationService: InvitationService) {}

  submit(): void {
    this.error.set('');
    this.loading.set(true);
    this.invitationService.create(this.workspaceId, this.email, this.role).subscribe({
      next: () => {
        this.loading.set(false);
        this.created.emit();
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not send invite.');
      }
    });
  }
}
