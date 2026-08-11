import { Component, HostListener, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Observable, Subject } from 'rxjs';
import { UserService, UserProfile } from '../../core/user.service';
import { HasUnsavedChanges } from '../../core/unsaved-changes.guard';

/** The editable fields, captured as stored so unsaved edits can be detected and discarded. */
interface ProfileFields {
  firstName: string;
  lastName: string;
  phone: string;
  street: string;
  city: string;
  district: string;
  postalCode: string;
  country: string;
}

/**
 * One set of always-visible fields, but changes are only persisted on an explicit Save.
 * Deliberately not the save-on-blur pattern used by land-detail-panel/job-detail: this is
 * identity data, so a stray keystroke must not silently overwrite it. The action bar only
 * appears once something actually differs from what's stored.
 * Email is read-only - there is no email-change flow, and identity is keyed on it.
 */
@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="p-lg max-w-2xl mx-auto">
      <div class="flex items-center justify-between mb-lg gap-md">
        <div class="flex items-center gap-sm">
          <h1 class="text-lg font-semibold text-neutral-900">Profile</h1>
          @if (saved()) {
            <span class="text-xs text-green-600">Saved</span>
          } @else if (isDirty()) {
            <span class="text-xs text-amber-600">Unsaved changes</span>
          }
        </div>
        @if (isDirty()) {
          <div class="flex items-center gap-sm">
            <button type="button" class="btn-secondary" [disabled]="saving()" (click)="discard()">
              Discard
            </button>
            <button type="button" class="btn-primary" [disabled]="saving()" (click)="save()">
              {{ saving() ? 'Saving…' : 'Save changes' }}
            </button>
          </div>
        }
      </div>

      @if (error()) {
        <p class="text-sm text-primary-500 mb-md">{{ error() }}</p>
      }

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (profile(); as p) {
        <div class="card space-y-lg">
          <div>
            <h2 class="text-xs font-semibold text-neutral-500 uppercase mb-sm">Account</h2>
            <div class="flex items-center justify-between px-md py-sm rounded bg-neutral-50">
              <div>
                <p class="text-sm text-neutral-900">{{ p.email ?? 'No email on file' }}</p>
                <p class="text-xs text-neutral-500">Joined {{ p.createdAt | date: 'mediumDate' }}</p>
              </div>
              @if (p.emailVerified) {
                <span class="text-xs px-sm py-xs rounded bg-green-100 text-green-700">Verified</span>
              } @else {
                <span class="text-xs px-sm py-xs rounded bg-amber-100 text-amber-700">Unverified</span>
              }
            </div>
          </div>

          <div>
            <h2 class="text-xs font-semibold text-neutral-500 uppercase mb-sm">Personal</h2>
            <div class="grid grid-cols-2 gap-sm">
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">First name</label>
                <input class="input-field" [(ngModel)]="firstName" name="firstName" />
              </div>
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Last name</label>
                <input class="input-field" [(ngModel)]="lastName" name="lastName" />
              </div>
            </div>
            <div class="mt-sm">
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Phone</label>
              <input class="input-field" [(ngModel)]="phone" name="phone" placeholder="Optional" />
            </div>
          </div>

          <div>
            <h2 class="text-xs font-semibold text-neutral-500 uppercase mb-sm">Address</h2>
            <div class="space-y-sm">
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Street</label>
                <input class="input-field" [(ngModel)]="street" name="street" placeholder="Optional" />
              </div>
              <div class="grid grid-cols-2 gap-sm">
                <div>
                  <label class="block text-xs font-medium text-neutral-700 mb-xs">City</label>
                  <input class="input-field" [(ngModel)]="city" name="city" placeholder="Optional" />
                </div>
                <div>
                  <label class="block text-xs font-medium text-neutral-700 mb-xs">District</label>
                  <input class="input-field" [(ngModel)]="district" name="district" placeholder="Optional" />
                </div>
                <div>
                  <label class="block text-xs font-medium text-neutral-700 mb-xs">Postal code</label>
                  <input class="input-field" [(ngModel)]="postalCode" name="postalCode" placeholder="Optional" />
                </div>
                <div>
                  <label class="block text-xs font-medium text-neutral-700 mb-xs">Country</label>
                  <input class="input-field" [(ngModel)]="country" name="country" placeholder="Optional" />
                </div>
              </div>
            </div>
          </div>
        </div>
      } @else {
        <div class="card text-sm text-primary-500">
          Could not load your profile.
          <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
        </div>
      }
    </div>

    @if (confirmingLeave()) {
      <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg">
        <div class="card w-full max-w-sm">
          <h2 class="text-base font-semibold text-neutral-900">Unsaved changes</h2>
          <p class="text-sm text-neutral-600 mt-xs">
            You've edited your profile but haven't saved. What would you like to do?
          </p>

          @if (error()) {
            <p class="text-sm text-primary-500 mt-md">{{ error() }}</p>
          }

          <div class="flex flex-col gap-sm mt-lg">
            <button type="button" class="btn-primary" [disabled]="saving()" (click)="saveAndLeave()">
              {{ saving() ? 'Saving…' : 'Save and leave' }}
            </button>
            <button type="button" class="btn-secondary" [disabled]="saving()" (click)="discardAndLeave()">
              Discard changes
            </button>
            <button type="button" class="btn-secondary" [disabled]="saving()" (click)="stayOnPage()">
              Keep editing
            </button>
          </div>
        </div>
      </div>
    }
  `
})
export class ProfileComponent implements OnInit, HasUnsavedChanges {
  profile = signal<UserProfile | null>(null);
  loading = signal(true);
  saving = signal(false);
  saved = signal(false);
  error = signal('');
  confirmingLeave = signal(false);

  firstName = '';
  lastName = '';
  phone = '';
  street = '';
  city = '';
  district = '';
  postalCode = '';
  country = '';

  constructor(private userService: UserService) {}

  ngOnInit(): void {
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    this.userService.getProfile().subscribe({
      next: (profile) => {
        this.apply(profile);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err.error?.message ?? 'Could not load profile.');
        this.loading.set(false);
      }
    });
  }

  /** True once any field differs from what's stored - drives the action bar and the badge. */
  isDirty(): boolean {
    const stored = this.stored;
    if (!stored) return false;

    return (
      this.firstName !== stored.firstName ||
      this.lastName !== stored.lastName ||
      this.phone !== stored.phone ||
      this.street !== stored.street ||
      this.city !== stored.city ||
      this.district !== stored.district ||
      this.postalCode !== stored.postalCode ||
      this.country !== stored.country
    );
  }

  save(onSaved?: () => void): void {
    if (!this.profile() || !this.isDirty()) return;

    if (!this.firstName.trim() || !this.lastName.trim()) {
      this.error.set('First and last name are required.');
      return;
    }

    this.error.set('');
    this.saving.set(true);

    this.userService
      .updateProfile({
        firstName: this.firstName.trim(),
        lastName: this.lastName.trim(),
        phone: this.phone.trim() || undefined,
        address: {
          street: this.street.trim() || null,
          city: this.city.trim() || null,
          district: this.district.trim() || null,
          postalCode: this.postalCode.trim() || null,
          country: this.country.trim() || null
        }
      })
      .subscribe({
        next: (profile) => {
          this.apply(profile);
          this.saving.set(false);
          this.saved.set(true);
          setTimeout(() => this.saved.set(false), 2000);
          onSaved?.();
        },
        error: (err) => {
          this.saving.set(false);
          this.error.set(err.error?.message ?? 'Could not save changes.');
        }
      });
  }

  /** Throws away every unsaved edit and returns the form to what's stored. */
  discard(): void {
    const current = this.profile();
    if (current) this.apply(current);
    this.error.set('');
  }

  /**
   * Router guard hook. Resolves once the user picks an option in the dialog, so navigation
   * pauses rather than silently dropping their edits.
   */
  canDeactivate(): boolean | Observable<boolean> {
    if (!this.isDirty()) return true;

    this.error.set('');
    this.confirmingLeave.set(true);
    this.leaveDecision = new Subject<boolean>();
    return this.leaveDecision.asObservable();
  }

  /**
   * Refresh and tab-close can't be intercepted by the router, and browsers only allow their
   * own generic dialog here - preventDefault is what triggers it.
   */
  @HostListener('window:beforeunload', ['$event'])
  onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.isDirty()) event.preventDefault();
  }

  saveAndLeave(): void {
    this.save(() => this.resolveLeave(true));
  }

  discardAndLeave(): void {
    this.discard();
    this.resolveLeave(true);
  }

  stayOnPage(): void {
    this.resolveLeave(false);
  }

  private resolveLeave(allow: boolean): void {
    this.confirmingLeave.set(false);
    this.leaveDecision?.next(allow);
    this.leaveDecision?.complete();
    this.leaveDecision = null;
  }

  private leaveDecision: Subject<boolean> | null = null;

  /** Snapshot of what the server holds, so edits can be detected and discarded. */
  private stored: ProfileFields | null = null;

  private apply(profile: UserProfile): void {
    this.profile.set(profile);
    this.firstName = profile.firstName;
    this.lastName = profile.lastName;
    this.phone = profile.phone ?? '';
    this.street = profile.address?.street ?? '';
    this.city = profile.address?.city ?? '';
    this.district = profile.address?.district ?? '';
    this.postalCode = profile.address?.postalCode ?? '';
    this.country = profile.address?.country ?? '';

    this.stored = {
      firstName: this.firstName,
      lastName: this.lastName,
      phone: this.phone,
      street: this.street,
      city: this.city,
      district: this.district,
      postalCode: this.postalCode,
      country: this.country
    };
  }
}
