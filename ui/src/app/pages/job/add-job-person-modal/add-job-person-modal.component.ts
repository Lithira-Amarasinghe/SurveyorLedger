import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap } from 'rxjs/operators';
import { Account, Person, PersonService } from '../../../core/person.service';
import { WorkspaceService } from '../../../core/workspace.service';

export interface PersonWithRole {
  person: Person;
  role: string;
}

/** Someone typed by email, not picked from the list - may or may not have an account yet. */
export interface InviteByEmail {
  email: string;
  firstName?: string;
  lastName?: string;
  phone?: string;
  role: string;
}

/**
 * Own mode signal, not tied to the search box - the previous inline widget hid the whole
 * invite-by-email form the moment the search query was cleared, silently dropping whatever
 * the admin had typed. 'search' and 'invite' are independent states here.
 */
type Mode = 'search' | 'invite';

@Component({
  selector: 'app-add-job-person-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-md" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">Add person</h2>
        <p class="text-xs text-neutral-500 mt-xs">
          @if (mode() === 'search') {
            Existing workspace members are added instantly. Not found? Invite by email instead.
          } @else {
            They'll get an email invite for this job - nothing is granted until they accept.
          }
        </p>

        <div class="mt-md">
          <label class="block text-xs font-medium text-neutral-700 mb-xs">Role</label>
          <select class="input-field" [(ngModel)]="role">
            @for (r of eligibleRoles(); track r) {
              <option [value]="r">{{ r }}</option>
            }
          </select>
        </div>

        @if (mode() === 'search') {
          <div class="mt-md">
            <input
              class="input-field"
              type="text"
              placeholder="Search by name or email…"
              [(ngModel)]="query"
              (ngModelChange)="onQueryChange($event)"
              autofocus
            />
          </div>

          @if (searching()) {
            <p class="text-xs text-neutral-500 mt-sm">Searching…</p>
          } @else if (query.trim().length > 0 && results().length > 0) {
            <div class="mt-sm space-y-xs">
              @for (person of results(); track person.userId) {
                <button
                  type="button"
                  class="w-full text-left px-md py-sm rounded hover:bg-neutral-100 flex items-center justify-between"
                  [disabled]="adding()"
                  (click)="choose(person)"
                >
                  <span class="text-sm text-neutral-900">{{ person.name }}</span>
                  <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ person.roleLabel }}</span>
                </button>
              }
            </div>
          } @else if (query.trim().length > 0 && !searching()) {
            <p class="text-xs text-neutral-500 mt-sm">Not found in this workspace.</p>
          }

          <button type="button" class="btn-secondary text-xs mt-md" (click)="switchToInvite()">
            Invite someone new by email
          </button>
        } @else {
          <div class="mt-md space-y-sm">
            <input
              class="input-field text-sm"
              type="email"
              placeholder="Their email"
              [(ngModel)]="inviteEmail"
              (ngModelChange)="onInviteEmailChange($event)"
            />
            @if (checkingAccount()) {
              <p class="text-xs text-neutral-500">Checking…</p>
            } @else if (matchedAccount()) {
              <p class="text-xs text-green-600">Existing account found - name is theirs, not yours to edit.</p>
            }
            <div class="grid grid-cols-2 gap-sm">
              <input class="input-field text-sm" placeholder="First name" [(ngModel)]="inviteFirstName" [disabled]="!!matchedAccount()" />
              <input class="input-field text-sm" placeholder="Last name" [(ngModel)]="inviteLastName" [disabled]="!!matchedAccount()" />
            </div>
            <div class="flex justify-between items-center pt-sm">
              <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" (click)="switchToSearch()">
                Back to search
              </button>
              <button
                type="button"
                class="btn-primary text-xs"
                [disabled]="adding() || !inviteEmail.trim() || (!matchedAccount() && (!inviteFirstName.trim() || !inviteLastName.trim()))"
                (click)="submitInvite()"
              >
                {{ adding() ? 'Sending…' : 'Send invite' }}
              </button>
            </div>
          </div>
        }

        @if (error()) {
          <p class="text-xs text-primary-500 mt-sm">{{ error() }}</p>
        }

        <div class="flex justify-end pt-lg">
          <button type="button" class="btn-secondary" (click)="cancel.emit()">Close</button>
        </div>
      </div>
    </div>
  `
})
export class AddJobPersonModalComponent implements OnInit {
  @Input() workspaceId = '';
  @Output() cancel = new EventEmitter<void>();
  @Output() added = new EventEmitter<PersonWithRole>();
  @Output() invited = new EventEmitter<InviteByEmail>();

  mode = signal<Mode>('search');

  query = '';
  role = 'Client';
  eligibleRoles = signal<string[]>(['Client', 'Surveyor']);
  results = signal<Person[]>([]);
  searching = signal(false);
  adding = signal(false);
  error = signal('');

  inviteEmail = '';
  inviteFirstName = '';
  inviteLastName = '';
  checkingAccount = signal(false);
  matchedAccount = signal<Account | null>(null);
  private draftFirstName = '';
  private draftLastName = '';

  private queryChanged = new Subject<string>();
  private inviteEmailChanged = new Subject<string>();

  constructor(private personService: PersonService, private workspaceService: WorkspaceService) {
    this.queryChanged
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        switchMap((q) => {
          if (!q.trim()) {
            this.searching.set(false);
            return [];
          }
          this.searching.set(true);
          return this.personService.searchPeople(this.workspaceId, q.trim());
        })
      )
      .subscribe({
        next: (people) => {
          this.results.set(people);
          this.searching.set(false);
        },
        error: () => this.searching.set(false)
      });

    this.inviteEmailChanged
      .pipe(
        debounceTime(400),
        distinctUntilChanged(),
        switchMap((email) => {
          if (!this.looksLikeEmail(email)) return [];
          this.checkingAccount.set(true);
          return this.personService.searchAccounts(email);
        })
      )
      .subscribe((accounts) => {
        this.checkingAccount.set(false);
        const typed = this.inviteEmail.trim().toLowerCase();
        const match = accounts.find(a => a.email?.toLowerCase() === typed) ?? null;
        this.applyMatch(match);
      });
  }

  ngOnInit(): void {
    this.workspaceService.getEligibleRoles(this.workspaceId, 'Job').subscribe(roles => {
      this.eligibleRoles.set(roles);
      if (!roles.includes(this.role)) this.role = roles[0] ?? this.role;
    });
  }

  switchToInvite(): void {
    this.mode.set('invite');
  }

  switchToSearch(): void {
    this.mode.set('search');
  }

  onQueryChange(value: string): void {
    this.queryChanged.next(value);
  }

  choose(person: Person): void {
    this.error.set('');
    this.adding.set(true);
    this.added.emit({ person, role: this.role });
  }

  onInviteEmailChange(value: string): void {
    if (this.matchedAccount()) this.applyMatch(null);
    this.checkingAccount.set(false);

    const trimmed = value.trim();
    if (!this.looksLikeEmail(trimmed)) return;
    this.inviteEmailChanged.next(trimmed);
  }

  submitInvite(): void {
    const email = this.inviteEmail.trim();
    if (!email) return;
    this.error.set('');
    this.adding.set(true);
    this.invited.emit({
      email,
      firstName: this.inviteFirstName.trim() || undefined,
      lastName: this.inviteLastName.trim() || undefined,
      role: this.role
    });
  }

  /** Call after successfully handling the `added`/`invited` event - lets the parent close the modal. */
  markAdded(): void {
    this.adding.set(false);
  }

  /** Call if handling the `added`/`invited` event failed - shows the error, re-enables the form. */
  markFailed(message: string): void {
    this.adding.set(false);
    this.error.set(message);
  }

  private applyMatch(account: Account | null): void {
    if (account) {
      if (!this.matchedAccount()) {
        this.draftFirstName = this.inviteFirstName;
        this.draftLastName = this.inviteLastName;
      }
      this.matchedAccount.set(account);
      this.inviteFirstName = account.firstName;
      this.inviteLastName = account.lastName;
    } else {
      const wasMatched = this.matchedAccount();
      this.matchedAccount.set(null);
      if (wasMatched) {
        this.inviteFirstName = this.draftFirstName;
        this.inviteLastName = this.draftLastName;
      }
    }
  }

  private looksLikeEmail(value: string): boolean {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);
  }
}
