import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, distinctUntilChanged, switchMap } from 'rxjs';
import { Account, PersonService } from '../../../core/person.service';

/** Exactly one form is ever populated: an account reference, or plain contact details. */
export interface OwnerValue {
  ownerId?: string;
  ownerName?: string;
  ownerPhone?: string;
  ownerEmail?: string;
}

/**
 * Picks a land owner. Searches every account in the system (not just workspace members) -
 * ownership is record-keeping, not access, so an owner may never have been invited
 * anywhere. Falls back to plain contact details for an owner with no account at all,
 * which is the common case for a landowner who will never log in.
 */
@Component({
  selector: 'app-owner-picker',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div>
      @if (selectedAccount(); as account) {
        <label class="block text-xs font-medium text-neutral-700 mb-xs">Owner</label>
        <div class="flex items-center justify-between px-md py-sm rounded bg-neutral-50">
          <div>
            <span class="text-sm text-neutral-900">{{ account.firstName }} {{ account.lastName }}</span>
            @if (account.email) {
              <span class="block text-xs text-neutral-500">{{ account.email }}</span>
            }
          </div>
          <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="clear()">Change</button>
        </div>
      } @else if (manualMode()) {
        <div class="flex items-center justify-between mb-xs">
          <label class="block text-xs font-medium text-neutral-700">Owner</label>
          <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="useSearch()">
            Search existing people
          </button>
        </div>
        <div class="space-y-sm">
          <input class="input-field" placeholder="Owner name" [ngModel]="manualName" (ngModelChange)="onManualChange('name', $event)" name="ownerName" />
          <div class="grid grid-cols-2 gap-sm">
            <input class="input-field" placeholder="Phone (optional)" [ngModel]="manualPhone" (ngModelChange)="onManualChange('phone', $event)" name="ownerPhone" />
            <input class="input-field" type="email" placeholder="Email (optional)" [ngModel]="manualEmail" (ngModelChange)="onManualChange('email', $event)" name="ownerEmail" />
          </div>
        </div>
      } @else {
        <div class="flex items-center justify-between mb-xs">
          <label class="block text-xs font-medium text-neutral-700">Owner</label>
          <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="useManual()">
            + New owner
          </button>
        </div>
        <input
          class="input-field"
          placeholder="Search people by name or email…"
          [ngModel]="query"
          (ngModelChange)="onQueryChange($event)"
          name="ownerSearch"
        />

        @if (searching()) {
          <p class="text-xs text-neutral-500 mt-xs">Searching…</p>
        } @else if (results().length > 0) {
          <div class="mt-xs border border-neutral-200 rounded divide-y divide-neutral-200">
            @for (account of results(); track account.userId) {
              <button
                type="button"
                class="w-full text-left px-md py-sm hover:bg-neutral-50"
                (click)="select(account)"
              >
                <span class="text-sm text-neutral-900">{{ account.firstName }} {{ account.lastName }}</span>
                @if (account.email) {
                  <span class="block text-xs text-neutral-500">{{ account.email }}</span>
                }
              </button>
            }
          </div>
        } @else if (query.trim().length >= 2) {
          <p class="text-xs text-neutral-500 mt-xs">No match.</p>
        }
      }
    </div>
  `
})
export class OwnerPickerComponent implements OnInit {
  @Input() value: OwnerValue = {};
  @Output() valueChange = new EventEmitter<OwnerValue>();

  /** Display name for an already-saved account owner, so the panel doesn't have to re-fetch it. */
  @Input() initialAccountLabel: string | null = null;

  query = '';
  manualName = '';
  manualPhone = '';
  manualEmail = '';

  results = signal<Account[]>([]);
  searching = signal(false);
  manualMode = signal(false);
  selectedAccount = signal<Account | null>(null);

  private queries = new Subject<string>();

  constructor(private personService: PersonService) {}

  ngOnInit(): void {
    if (this.value.ownerId && this.initialAccountLabel) {
      const [firstName, ...rest] = this.initialAccountLabel.split(' ');
      this.selectedAccount.set({
        userId: this.value.ownerId,
        firstName,
        lastName: rest.join(' '),
        email: this.value.ownerEmail ?? null
      });
    } else if (this.value.ownerName) {
      this.manualMode.set(true);
      this.manualName = this.value.ownerName;
      this.manualPhone = this.value.ownerPhone ?? '';
      this.manualEmail = this.value.ownerEmail ?? '';
    }

    this.queries
      .pipe(
        debounceTime(250),
        distinctUntilChanged(),
        switchMap(term => this.personService.searchAccounts(term))
      )
      .subscribe({
        next: accounts => {
          this.results.set(accounts);
          this.searching.set(false);
        },
        error: () => {
          this.results.set([]);
          this.searching.set(false);
        }
      });
  }

  onQueryChange(term: string): void {
    this.query = term;
    this.searching.set(term.trim().length >= 2);
    if (term.trim().length < 2) this.results.set([]);
    this.queries.next(term);
  }

  select(account: Account): void {
    this.selectedAccount.set(account);
    this.results.set([]);
    this.query = '';
    this.emit({ ownerId: account.userId });
  }

  clear(): void {
    this.selectedAccount.set(null);
    this.manualMode.set(false);
    this.emit({});
  }

  useManual(): void {
    this.manualMode.set(true);
    this.results.set([]);
    this.query = '';
  }

  useSearch(): void {
    this.manualMode.set(false);
    this.manualName = '';
    this.manualPhone = '';
    this.manualEmail = '';
    this.emit({});
  }

  onManualChange(field: 'name' | 'phone' | 'email', value: string): void {
    if (field === 'name') this.manualName = value;
    if (field === 'phone') this.manualPhone = value;
    if (field === 'email') this.manualEmail = value;

    // Name is what makes a plain owner record real - without it the phone/email alone
    // would fail the backend's "exactly one owner form" check.
    this.emit(
      this.manualName.trim()
        ? {
            ownerName: this.manualName.trim(),
            ownerPhone: this.manualPhone.trim() || undefined,
            ownerEmail: this.manualEmail.trim() || undefined
          }
        : {}
    );
  }

  private emit(value: OwnerValue): void {
    this.value = value;
    this.valueChange.emit(value);
  }
}
