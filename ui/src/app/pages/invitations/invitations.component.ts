import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { InvitationService, MyInvitation } from '../../core/invitation.service';

const STATUS_STYLES: Record<string, string> = {
  Pending: 'bg-amber-100 text-amber-700',
  Accepted: 'bg-green-100 text-green-700',
  Declined: 'bg-neutral-200 text-neutral-500',
  Expired: 'bg-neutral-200 text-neutral-500',
  Revoked: 'bg-neutral-200 text-neutral-500'
};

@Component({
  selector: 'app-invitations',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="p-lg max-w-2xl mx-auto">
      <h1 class="text-lg font-semibold text-neutral-900 mb-lg">Invitations</h1>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (error()) {
        <div class="card text-sm text-primary-500">
          {{ error() }}
          <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
        </div>
      } @else if (invitations().length === 0) {
        <div class="card text-center text-sm text-neutral-500">No invitations.</div>
      } @else {
        <div class="space-y-sm">
          @for (inv of invitations(); track inv.invitationId) {
            <div class="card flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-neutral-900">{{ inv.workspaceName }}</p>
                <p class="text-xs text-neutral-500">Role: {{ inv.role }}</p>
                @if (inv.jobLabel) {
                  <p class="text-xs text-neutral-500">Also assigned to: {{ inv.jobLabel }}</p>
                }
              </div>
              <div class="flex items-center gap-sm">
                <span class="text-xs px-sm py-xs rounded" [class]="statusStyle(inv.status)">{{ inv.status }}</span>
                @if (inv.status === 'Pending') {
                  <button class="btn-secondary py-xs" [disabled]="busy().has(inv.invitationId)" (click)="decline(inv)">Decline</button>
                  <button class="btn-primary py-xs" [disabled]="busy().has(inv.invitationId)" (click)="accept(inv)">Accept</button>
                }
              </div>
            </div>
          }
        </div>
      }
    </div>
  `
})
export class InvitationsComponent implements OnInit {
  invitations = signal<MyInvitation[]>([]);
  loading = signal(true);
  error = signal('');
  busy = signal<Set<string>>(new Set());

  constructor(private invitationService: InvitationService, private router: Router) {}

  ngOnInit(): void {
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    this.invitationService.mine().subscribe({
      next: (invitations) => {
        this.invitations.set(invitations);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err.error?.message ?? 'Could not load invitations.');
        this.loading.set(false);
      }
    });
  }

  statusStyle(status: string): string {
    return STATUS_STYLES[status] ?? 'bg-neutral-100 text-neutral-600';
  }

  accept(inv: MyInvitation): void {
    this.markBusy(inv.invitationId, true);
    this.invitationService.accept(inv.invitationId).subscribe({
      next: (result) => this.router.navigate(['/app/workspace', result.workspaceId]),
      error: (err) => {
        this.markBusy(inv.invitationId, false);
        this.error.set(err.error?.message ?? 'Could not accept invitation.');
      }
    });
  }

  decline(inv: MyInvitation): void {
    this.markBusy(inv.invitationId, true);
    this.invitationService.decline(inv.invitationId).subscribe({
      next: () => this.fetch(),
      error: (err) => {
        this.markBusy(inv.invitationId, false);
        this.error.set(err.error?.message ?? 'Could not decline invitation.');
      }
    });
  }

  private markBusy(id: string, isBusy: boolean): void {
    this.busy.update(s => {
      const next = new Set(s);
      isBusy ? next.add(id) : next.delete(id);
      return next;
    });
  }
}
