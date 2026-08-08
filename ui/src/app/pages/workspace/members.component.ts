import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { WorkspaceService, Member } from '../../core/workspace.service';
import { InvitationService, Invitation } from '../../core/invitation.service';
import { AuthService } from '../../core/auth.service';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';
import { InviteModalComponent } from './invite-modal/invite-modal.component';

interface MemberRow {
  key: string;
  email: string;
  displayName: string;
  role: string;
  pendingRole?: string;
  dateLabel: string;
  isPending: boolean;
  isOwner: boolean;
  isSelf: boolean;
  emailFailed: boolean;
}

@Component({
  selector: 'app-members',
  standalone: true,
  imports: [CommonModule, InviteModalComponent],
  template: `
    <div class="p-lg max-w-4xl mx-auto">
      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Members</h1>
        @if (isAdmin()) {
          <button class="btn-primary" (click)="modalOpen.set(true)">Invite member</button>
        }
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (error()) {
        <div class="card text-sm text-primary-500">
          {{ error() }}
          <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
        </div>
      } @else {
        <div class="card p-0 overflow-x-auto">
          <table class="w-full text-sm">
            <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
              <tr>
                <th class="text-left px-lg py-sm font-medium">Member</th>
                <th class="text-left px-lg py-sm font-medium">Role</th>
                <th class="text-left px-lg py-sm font-medium">Since</th>
                <th class="px-lg py-sm"></th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.key) {
                <tr class="border-t border-neutral-200">
                  <td class="px-lg py-sm text-neutral-900">
                    {{ row.displayName }}
                    @if (row.isPending) {
                      <span class="text-neutral-500">· Pending</span>
                    }
                    @if (row.emailFailed) {
                      <span class="block text-xs text-primary-500">Email delivery failed</span>
                    }
                  </td>
                  <td class="px-lg py-sm">
                    @if (isAdmin() && !row.isPending && !row.isOwner && row.pendingRole) {
                      <span class="text-xs">Change to <strong>{{ row.pendingRole }}</strong>?
                        <button class="text-primary-500 font-medium" (click)="confirmRoleChange(row)">Yes</button>
                        <button class="text-neutral-500" (click)="cancelRoleChange(row)">No</button>
                      </span>
                    } @else if (isAdmin() && !row.isPending && !row.isOwner) {
                      <select
                        class="input-field py-xs"
                        [value]="row.role"
                        (change)="onRoleSelect(row, $any($event.target).value)"
                      >
                        <option value="Admin">Admin</option>
                        <option value="Manager">Manager</option>
                        <option value="Surveyor">Surveyor</option>
                        <option value="Client">Client</option>
                      </select>
                    } @else {
                      <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ row.role }}</span>
                    }
                  </td>
                  <td class="px-lg py-sm text-neutral-600">{{ row.dateLabel }}</td>
                  <td class="px-lg py-sm text-right whitespace-nowrap">
                    @if (row.isOwner) {
                      <!-- no actions -->
                    } @else if (row.isPending) {
                      @if (isAdmin()) {
                        <button class="text-xs text-neutral-600 hover:text-neutral-900 mr-md" (click)="resend(row)">Resend</button>
                        @if (confirming().has(row.key)) {
                          <span class="text-xs">Sure?
                            <button class="text-primary-500 font-medium" (click)="revoke(row)">Yes</button>
                            <button class="text-neutral-500" (click)="cancelConfirm(row.key)">No</button>
                          </span>
                        } @else {
                          <button class="text-xs text-primary-500 hover:text-primary-600" (click)="askConfirm(row.key)">Revoke</button>
                        }
                      }
                    } @else if (isAdmin() || row.isSelf) {
                      @if (confirming().has(row.key)) {
                        <span class="text-xs">Sure?
                          <button class="text-primary-500 font-medium" (click)="remove(row)">Yes</button>
                          <button class="text-neutral-500" (click)="cancelConfirm(row.key)">No</button>
                        </span>
                      } @else {
                        <button class="text-xs text-primary-500 hover:text-primary-600" (click)="askConfirm(row.key)">
                          {{ row.isSelf ? 'Leave' : 'Remove' }}
                        </button>
                      }
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>

    @if (modalOpen()) {
      <app-invite-modal [workspaceId]="workspaceId" (cancel)="modalOpen.set(false)" (created)="onInvited()" />
    }
  `
})
export class MembersComponent implements OnInit {
  workspaceId = '';
  rows = signal<MemberRow[]>([]);
  loading = signal(true);
  error = signal('');
  modalOpen = signal(false);
  confirming = signal<Set<string>>(new Set());

  constructor(
    private workspaceService: WorkspaceService,
    private invitationService: InvitationService,
    private authService: AuthService,
    private currentWorkspace: CurrentWorkspaceService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.fetch();
  }

  isAdmin(): boolean {
    return this.currentWorkspace.current()?.role === 'Admin';
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    const currentEmail = this.authService.getCurrentEmail();

    // Pending invites are Admin-only server-side; skip the call entirely for non-Admins
    // instead of letting it 403 and failing the whole forkJoin.
    forkJoin({
      members: this.workspaceService.getMembers(this.workspaceId),
      invitations: this.isAdmin() ? this.invitationService.list(this.workspaceId) : of([])
    }).subscribe({
      next: ({ members, invitations }) => {
        this.rows.set(this.buildRows(members, invitations, currentEmail));
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err.error?.message ?? 'Could not load members.');
        this.loading.set(false);
      }
    });
  }

  private buildRows(members: Member[], invitations: Invitation[], currentEmail: string | null): MemberRow[] {
    const memberRows: MemberRow[] = members
      .slice()
      .sort((a, b) => (a.isOwner === b.isOwner ? 0 : a.isOwner ? -1 : 1))
      .map(m => ({
        key: m.userId,
        email: m.email,
        displayName: `${m.firstName} ${m.lastName}`,
        role: m.role,
        dateLabel: new Date(m.assignedAt).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' }),
        isPending: false,
        isOwner: m.isOwner,
        isSelf: m.email === currentEmail,
        emailFailed: false
      }));

    const pendingRows: MemberRow[] = invitations.map(i => ({
      key: i.invitationId,
      email: i.email,
      displayName: i.email,
      role: i.role,
      dateLabel: `Invited ${new Date(i.createdAt).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}`,
      isPending: true,
      isOwner: false,
      isSelf: false,
      emailFailed: i.emailFailed
    }));

    return [...memberRows, ...pendingRows];
  }

  askConfirm(key: string): void {
    this.confirming.update(s => new Set(s).add(key));
  }

  cancelConfirm(key: string): void {
    this.confirming.update(s => {
      const next = new Set(s);
      next.delete(key);
      return next;
    });
  }

  onRoleSelect(row: MemberRow, newRole: string): void {
    if (newRole === row.role) {
      this.cancelRoleChange(row);
      return;
    }
    this.rows.update(rows => rows.map(r => r.key === row.key ? { ...r, pendingRole: newRole } : r));
  }

  confirmRoleChange(row: MemberRow): void {
    const newRole = row.pendingRole;
    if (!newRole) return;
    this.changeRole(row, newRole);
  }

  cancelRoleChange(row: MemberRow): void {
    this.rows.update(rows => rows.map(r => r.key === row.key ? { ...r, pendingRole: undefined } : r));
  }

  private changeRole(row: MemberRow, newRole: string): void {
    const previousRole = row.role;
    this.rows.update(rows => rows.map(r => r.key === row.key ? { ...r, role: newRole, pendingRole: undefined } : r));

    this.workspaceService.updateMemberRole(this.workspaceId, row.key, newRole).subscribe({
      error: (err) => {
        this.rows.update(rows => rows.map(r => r.key === row.key ? { ...r, role: previousRole } : r));
        this.error.set(err.error?.message ?? 'Could not change role.');
      }
    });
  }

  remove(row: MemberRow): void {
    this.cancelConfirm(row.key);
    this.workspaceService.removeMember(this.workspaceId, row.key).subscribe({
      next: () => {
        if (row.isSelf) {
          this.currentWorkspace.clear();
          this.router.navigate(['/app/dashboard']);
        } else {
          this.fetch();
        }
      },
      error: (err) => this.error.set(err.error?.message ?? 'Could not remove member.')
    });
  }

  resend(row: MemberRow): void {
    this.invitationService.resend(this.workspaceId, row.key).subscribe({
      next: () => this.fetch(),
      error: (err) => this.error.set(err.error?.message ?? 'Could not resend invite.')
    });
  }

  revoke(row: MemberRow): void {
    this.cancelConfirm(row.key);
    this.invitationService.revoke(this.workspaceId, row.key).subscribe({
      next: () => this.fetch(),
      error: (err) => this.error.set(err.error?.message ?? 'Could not revoke invite.')
    });
  }

  onInvited(): void {
    this.modalOpen.set(false);
    this.fetch();
  }
}
