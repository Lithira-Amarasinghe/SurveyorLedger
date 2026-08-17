import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { WorkspaceService, Member, MemberFullAccessGrant, MemberScopeGrant } from '../../core/workspace.service';
import { InvitationService, Invitation } from '../../core/invitation.service';
import { AuthService } from '../../core/auth.service';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';
import { AddPersonModalComponent } from './add-person-modal/add-person-modal.component';

type AccessFilter = 'all' | 'direct' | 'child';

interface MemberRow {
  key: string;
  displayName: string;
  roles: string[];
  dateLabel: string;
  isPending: boolean;
  invitationStatus?: Invitation['status'];
  isOwner: boolean;
  isSelf: boolean;
  emailFailed: boolean;
  /** Blanket access this member's role(s) grant, e.g. Admin's job.view_all - with actions. */
  fullAccessGrants: MemberFullAccessGrant[];
  /** Individual jobs this member is explicitly assigned to, each with its own role. */
  jobGrants: MemberScopeGrant[];
  /** True when this member has an explicit Workspace-scope row (a "direct" member). */
  isDirect: boolean;
}

@Component({
  selector: 'app-members',
  standalone: true,
  imports: [CommonModule, AddPersonModalComponent],
  template: `
    <div class="p-lg max-w-4xl mx-auto">
      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Members</h1>
        @if (isAdmin()) {
          <button class="btn-primary" (click)="modalOpen.set(true)">Add member</button>
        }
      </div>

      @if (!loading() && !error()) {
        <div class="flex gap-xs mb-md">
          @for (f of accessFilters; track f.value) {
            <button
              type="button"
              class="text-xs px-md py-xs rounded"
              [class.bg-primary-500]="accessFilter() === f.value"
              [class.text-white]="accessFilter() === f.value"
              [class.bg-neutral-100]="accessFilter() !== f.value"
              [class.text-neutral-600]="accessFilter() !== f.value"
              (click)="accessFilter.set(f.value)"
            >
              {{ f.label }}
            </button>
          }
        </div>
      }

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
                <th class="text-left px-lg py-sm font-medium">Job access</th>
                <th class="text-left px-lg py-sm font-medium">Since</th>
                <th class="px-lg py-sm"></th>
              </tr>
            </thead>
            <tbody>
              @for (row of filteredRows(); track row.key) {
                <tr class="border-t border-neutral-200" [class.cursor-pointer]="row.jobGrants.length > 0" (click)="row.jobGrants.length > 0 && toggleExpand(row.key)">
                  <td class="px-lg py-sm text-neutral-900">
                    {{ row.displayName }}
                    @if (row.invitationStatus === 'Declined') {
                      <span class="text-primary-500">· Declined</span>
                    } @else if (row.invitationStatus === 'Expired') {
                      <span class="text-neutral-500">· Expired</span>
                    } @else if (row.invitationStatus === 'Revoked') {
                      <span class="text-neutral-500">· Revoked</span>
                    } @else if (row.isPending) {
                      <span class="text-neutral-500">· Pending</span>
                    }
                    @if (row.emailFailed) {
                      <span class="block text-xs text-primary-500">Email delivery failed</span>
                    }
                  </td>
                  <td class="px-lg py-sm" (click)="$event.stopPropagation()">
                    <span class="flex flex-wrap items-center gap-xs">
                      @for (r of row.roles; track r) {
                        <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">
                          {{ r }}
                          @if (isAdmin() && !row.isPending && !row.isOwner && row.roles.length > 1) {
                            @if (confirmingRemoveRole()?.key === row.key && confirmingRemoveRole()?.role === r) {
                              <span class="text-neutral-500 ml-xs text-xs">
                                <button class="text-primary-500 font-medium" (click)="doRemoveRole(row, r)">yes</button>
                                <span class="mx-xs">/</span>
                                <button class="text-neutral-500" (click)="confirmingRemoveRole.set(null)">no</button>
                              </span>
                            } @else {
                              <button class="text-neutral-400 hover:text-primary-500 ml-xs" (click)="confirmingRemoveRole.set({ key: row.key, role: r })">&times;</button>
                            }
                          }
                        </span>
                      }
                      @if (row.roles.length === 0 && !row.isPending) {
                        <span class="text-xs px-sm py-xs rounded bg-neutral-50 text-neutral-500" title="Assigned to a job only, not a workspace member. Use Add member to invite them to the workspace.">
                          Job only
                        </span>
                      }
                      @if (row.roles.length > 0 && isAdmin() && !row.isPending && !row.isOwner && addableRoles(row).length > 0) {
                        <select
                          class="input-field py-xs text-xs"
                          (change)="addRole(row, $any($event.target).value); $any($event.target).value = ''"
                        >
                          <option value="" disabled selected>+ Add role</option>
                          @for (r of addableRoles(row); track r) {
                            <option [value]="r">{{ r }}</option>
                          }
                        </select>
                      }
                    </span>
                  </td>
                  <td class="px-lg py-sm">
                    @if (row.isPending) {
                      <span class="text-xs text-neutral-500">—</span>
                    } @else {
                      <span class="flex flex-wrap gap-xs">
                        @for (grant of row.fullAccessGrants; track grant.scopeType) {
                          <span class="text-xs px-sm py-xs rounded bg-primary-50 text-primary-600">
                            All {{ grant.scopeType.toLowerCase() }}s · {{ grant.roleName }}{{ grant.actions.length > 0 ? ' (' + grant.actions.join(', ') + ')' : '' }}
                          </span>
                        }
                        @for (job of row.jobGrants; track job.scopeId) {
                          <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ job.label }} ({{ job.role }})</span>
                        }
                        @if (row.fullAccessGrants.length === 0 && row.jobGrants.length === 0) {
                          <span class="text-xs text-neutral-500">No job access</span>
                        }
                      </span>
                    }
                  </td>
                  <td class="px-lg py-sm text-neutral-600">{{ row.dateLabel }}</td>
                  <td class="px-lg py-sm text-right whitespace-nowrap" (click)="$event.stopPropagation()">
                    @if (row.isOwner) {
                      <!-- no actions -->
                    } @else if (row.isPending) {
                      @if (isAdmin()) {
                        <button class="text-xs text-neutral-600 hover:text-neutral-900 mr-md" (click)="resend(row)">Resend</button>
                        @if (row.invitationStatus === 'Pending') {
                          @if (confirming().has(row.key)) {
                            <span class="text-xs">Sure?
                              <button class="text-primary-500 font-medium" (click)="revoke(row)">Yes</button>
                              <button class="text-neutral-500" (click)="cancelConfirm(row.key)">No</button>
                            </span>
                          } @else {
                            <button class="text-xs text-primary-500 hover:text-primary-600" (click)="askConfirm(row.key)">Revoke</button>
                          }
                        }
                      }
                    } @else if (row.roles.length === 0) {
                      <!-- Job-only row: no workspace membership to remove from here - manage their job assignment from the job page instead. -->
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
                @if (expandedKey() === row.key && row.jobGrants.length > 0) {
                  <tr class="border-t border-neutral-100 bg-neutral-50">
                    <td colspan="5" class="px-lg py-sm">
                      <div class="text-xs text-neutral-500 font-medium mb-xs">Jobs</div>
                      <div class="flex flex-col gap-xs">
                        @for (job of row.jobGrants; track job.scopeId) {
                          <div class="text-xs text-neutral-700">{{ job.label }} — {{ job.role }}</div>
                        }
                      </div>
                    </td>
                  </tr>
                }
              }
            </tbody>
          </table>
        </div>
      }
    </div>

    @if (modalOpen()) {
      <app-add-person-modal [workspaceId]="workspaceId" (cancel)="modalOpen.set(false)" (created)="onAdded()" />
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
  confirmingRemoveRole = signal<{ key: string; role: string } | null>(null);
  successMessage = signal('');
  eligibleRoles = signal<string[]>(['Admin', 'Surveyor', 'Member', 'WorkspaceMember']);

  accessFilter = signal<AccessFilter>('all');
  accessFilters: { value: AccessFilter; label: string }[] = [
    { value: 'all', label: 'All' },
    { value: 'direct', label: 'Direct' },
    { value: 'child', label: 'Child' }
  ];
  expandedKey = signal<string | null>(null);
  filteredRows = computed(() => {
    const filter = this.accessFilter();
    const rows = this.rows();
    if (filter === 'all') return rows;
    if (filter === 'direct') return rows.filter(r => r.isDirect || r.isPending);
    return rows.filter(r => !r.isDirect && !r.isPending);
  });

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
    this.workspaceService.getEligibleRoles(this.workspaceId, 'Workspace').subscribe(roles => this.eligibleRoles.set(roles));
  }

  isAdmin(): boolean {
    return (this.currentWorkspace.current()?.roles ?? []).includes('Admin');
  }

  addableRoles(row: MemberRow): string[] {
    return this.eligibleRoles().filter(r => !row.roles.includes(r));
  }

  toggleExpand(key: string): void {
    this.expandedKey.update(current => (current === key ? null : key));
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    const currentUserId = this.authService.getCurrentUserId();

    // Pending invites are Admin-only server-side; skip the call entirely for non-Admins
    // instead of letting it 403 and failing the whole forkJoin.
    forkJoin({
      members: this.workspaceService.getMembers(this.workspaceId),
      invitations: this.isAdmin() ? this.invitationService.list(this.workspaceId) : of([])
    }).subscribe({
      next: ({ members, invitations }) => {
        this.rows.set(this.buildRows(members, invitations, currentUserId));
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err.error?.message ?? 'Could not load members.');
        this.loading.set(false);
      }
    });
  }

  private buildRows(members: Member[], invitations: Invitation[], currentUserId: string | null): MemberRow[] {
    const memberRows: MemberRow[] = members
      .slice()
      .sort((a, b) => (a.isOwner === b.isOwner ? 0 : a.isOwner ? -1 : 1))
      .map(m => ({
        key: m.userId,
        displayName: `${m.firstName} ${m.lastName}`,
        roles: m.roles,
        dateLabel: new Date(m.assignedAt).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' }),
        isPending: false,
        isOwner: m.isOwner,
        isSelf: m.userId === currentUserId,
        emailFailed: false,
        // Computed server-side so this page and the job screens can't disagree about
        // who can see what - see WorkspaceService.GetMembersAsync.
        fullAccessGrants: m.fullAccessGrants ?? [],
        jobGrants: (m.additionalScopes ?? []).filter(s => s.scopeType === 'Job'),
        // Direct = holds an explicit Workspace-scope row. A "Job only" member (roles.length
        // === 0) has no such row - their access is entirely via a job-scope grant (Child).
        isDirect: m.roles.length > 0
      }));

    const pendingRows: MemberRow[] = invitations.map(i => ({
      key: i.invitationId,
      displayName: i.email,
      roles: [i.role],
      dateLabel: `Invited ${new Date(i.createdAt).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}`,
      isPending: true,
      invitationStatus: i.status,
      isOwner: false,
      isSelf: false,
      emailFailed: i.emailFailed,
      // A pending invitee holds no UserAccess yet, so they have no job access to show.
      fullAccessGrants: [],
      jobGrants: [],
      isDirect: true
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

  addRole(row: MemberRow, role: string): void {
    if (!role || row.roles.includes(role)) return;
    const previousRoles = row.roles;
    this.rows.update(rows => rows.map(r => r.key === row.key ? { ...r, roles: [...r.roles, role] } : r));

    this.workspaceService.addMemberRole(this.workspaceId, row.key, role).subscribe({
      next: () => this.successMessage.set('Role added.'),
      error: (err) => {
        this.rows.update(rows => rows.map(r => r.key === row.key ? { ...r, roles: previousRoles } : r));
        this.error.set(err.error?.message ?? 'Could not add role.');
      }
    });
  }

  doRemoveRole(row: MemberRow, role: string): void {
    if (row.roles.length <= 1) return;
    this.confirmingRemoveRole.set(null);
    const previousRoles = row.roles;
    this.rows.update(rows => rows.map(r => r.key === row.key ? { ...r, roles: r.roles.filter(x => x !== role) } : r));

    this.workspaceService.removeMemberRole(this.workspaceId, row.key, role).subscribe({
      next: () => this.successMessage.set('Role removed.'),
      error: (err) => {
        this.rows.update(rows => rows.map(r => r.key === row.key ? { ...r, roles: previousRoles } : r));
        this.error.set(err.error?.message ?? 'Could not remove role.');
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

  onAdded(): void {
    this.modalOpen.set(false);
    this.fetch();
  }
}
