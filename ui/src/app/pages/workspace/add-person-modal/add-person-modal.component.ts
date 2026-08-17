import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap } from 'rxjs/operators';
import { InvitationService } from '../../../core/invitation.service';
import { PersonService, Account } from '../../../core/person.service';
import { WorkspaceService } from '../../../core/workspace.service';

@Component({
  selector: 'app-add-person-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-md" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">Add member</h2>
        <p class="text-xs text-neutral-500 mt-xs">They'll get an email invite. Nothing is granted until they accept.</p>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <input class="input-field" type="email" placeholder="Email" [(ngModel)]="email" name="email" (ngModelChange)="onEmailChange($event)" required />
            @if (checkingEmail()) {
              <p class="text-xs text-neutral-500 mt-xs">Checking…</p>
            } @else if (matchedAccount()) {
              <p class="text-xs text-green-600 mt-xs">Existing account found - name is theirs, not yours to edit.</p>
            }
          </div>

          <div class="grid grid-cols-2 gap-sm">
            <input class="input-field" placeholder="First name" [(ngModel)]="firstName" name="firstName" [disabled]="!!matchedAccount()" required />
            <input class="input-field" placeholder="Last name" [(ngModel)]="lastName" name="lastName" [disabled]="!!matchedAccount()" required />
          </div>

          @if (!matchedAccount()) {
            <input class="input-field" placeholder="Phone (optional)" [(ngModel)]="phone" name="phone" />
            <div class="grid grid-cols-2 gap-sm">
              <input class="input-field" placeholder="Street (optional)" [(ngModel)]="street" name="street" />
              <input class="input-field" placeholder="City (optional)" [(ngModel)]="city" name="city" />
            </div>
          }

          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Role</label>
            <select class="input-field" name="role" [(ngModel)]="role">
              @for (r of eligibleRoles(); track r) {
                <option [value]="r">{{ r }}</option>
              }
            </select>
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="!firstName.trim() || !lastName.trim() || !email.trim() || loading()">
              {{ loading() ? 'Sending…' : 'Send invite' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class AddPersonModalComponent implements OnInit {
  @Input({ required: true }) workspaceId!: string;
  @Output() cancel = new EventEmitter<void>();
  @Output() created = new EventEmitter<void>();

  firstName = '';
  lastName = '';
  email = '';
  phone = '';
  street = '';
  city = '';
  role = 'Member';
  eligibleRoles = signal<string[]>(['Admin', 'Surveyor', 'Member', 'WorkspaceMember']);
  loading = signal(false);
  error = signal('');

  checkingEmail = signal(false);
  matchedAccount = signal<Account | null>(null);

  /** Fields the admin typed by hand before a match locked them - restored if the match clears. */
  private draftFirstName = '';
  private draftLastName = '';

  private emailInput$ = new Subject<string>();

  constructor(
    private invitationService: InvitationService,
    private personService: PersonService,
    private workspaceService: WorkspaceService
  ) {
    this.emailInput$
      .pipe(
        debounceTime(400),
        distinctUntilChanged(),
        switchMap(email => {
          const trimmed = email.trim();
          if (!this.looksLikeEmail(trimmed)) return [];
          this.checkingEmail.set(true);
          return this.personService.searchAccounts(trimmed);
        })
      )
      .subscribe(accounts => {
        this.checkingEmail.set(false);
        const typed = this.email.trim().toLowerCase();
        const match = accounts.find(a => a.email?.toLowerCase() === typed) ?? null;
        this.applyMatch(match);
      });
  }

  ngOnInit(): void {
    this.workspaceService.getEligibleRoles(this.workspaceId, 'Workspace').subscribe(roles => {
      this.eligibleRoles.set(roles);
      if (!roles.includes(this.role)) this.role = roles[0] ?? this.role;
    });
  }

  onEmailChange(value: string): void {
    // Any account match is tied to the exact email that produced it - editing the email
    // invalidates it immediately so stale identity data can't linger on screen.
    if (this.matchedAccount()) this.applyMatch(null);
    this.checkingEmail.set(false);

    const trimmed = value.trim();
    if (!this.looksLikeEmail(trimmed)) return;
    this.emailInput$.next(trimmed);
  }

  private applyMatch(account: Account | null): void {
    if (account) {
      if (!this.matchedAccount()) {
        // First time locking - stash whatever the admin had typed so it can come back if
        // they change the email again and the match no longer applies.
        this.draftFirstName = this.firstName;
        this.draftLastName = this.lastName;
      }
      this.matchedAccount.set(account);
      this.firstName = account.firstName;
      this.lastName = account.lastName;
    } else {
      const wasMatched = this.matchedAccount();
      this.matchedAccount.set(null);
      if (wasMatched) {
        this.firstName = this.draftFirstName;
        this.lastName = this.draftLastName;
      }
    }
  }

  private looksLikeEmail(value: string): boolean {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);
  }

  submit(): void {
    if (!this.firstName.trim() || !this.lastName.trim() || !this.email.trim()) return;
    this.error.set('');
    this.loading.set(true);

    this.invitationService
      .create(this.workspaceId, {
        email: this.email.trim(),
        role: this.role,
        // Existing account: server ignores these anyway and keeps their real profile - the
        // fields above are shown read-only for the same reason, so send them regardless.
        firstName: this.firstName.trim(),
        lastName: this.lastName.trim(),
        phone: this.phone.trim() || undefined,
        address: {
          street: this.street.trim() || undefined,
          city: this.city.trim() || undefined
        }
      })
      .subscribe({
        next: () => {
          this.loading.set(false);
          this.created.emit();
        },
        error: (err) => {
          this.loading.set(false);
          this.error.set(err.error?.message ?? 'Could not add member.');
        }
      });
  }
}
