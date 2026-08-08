import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { UserService, UserProfile } from '../../core/user.service';

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="p-lg max-w-2xl mx-auto">
      <h1 class="text-lg font-semibold text-neutral-900 mb-lg">Profile</h1>

      <div class="flex gap-md border-b border-neutral-200 mb-lg">
        <button
          type="button"
          class="px-md py-sm text-sm font-medium border-b-2 -mb-px"
          [class.border-primary-500]="tab() === 'view'"
          [class.text-primary-600]="tab() === 'view'"
          [class.border-transparent]="tab() !== 'view'"
          [class.text-neutral-500]="tab() !== 'view'"
          (click)="tab.set('view')"
        >
          View
        </button>
        <button
          type="button"
          class="px-md py-sm text-sm font-medium border-b-2 -mb-px"
          [class.border-primary-500]="tab() === 'edit'"
          [class.text-primary-600]="tab() === 'edit'"
          [class.border-transparent]="tab() !== 'edit'"
          [class.text-neutral-500]="tab() !== 'edit'"
          (click)="tab.set('edit')"
        >
          Edit
        </button>
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (tab() === 'view') {
        <div class="card space-y-md">
          <div>
            <p class="text-xs text-neutral-500">User ID</p>
            <p class="text-sm text-neutral-900">{{ profile()?.userId }}</p>
          </div>
          <div>
            <p class="text-xs text-neutral-500">Email</p>
            <p class="text-sm text-neutral-900">{{ profile()?.email }}</p>
          </div>
          <div>
            <p class="text-xs text-neutral-500">First name</p>
            <p class="text-sm text-neutral-900">{{ profile()?.firstName }}</p>
          </div>
          <div>
            <p class="text-xs text-neutral-500">Last name</p>
            <p class="text-sm text-neutral-900">{{ profile()?.lastName }}</p>
          </div>
        </div>
      } @else {
        <form class="card space-y-md" (ngSubmit)="save()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">First name</label>
            <input class="input-field" type="text" name="firstName" [(ngModel)]="firstName" required />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Last name</label>
            <input class="input-field" type="text" name="lastName" [(ngModel)]="lastName" required />
          </div>

          @if (saved()) {
            <p class="text-sm text-primary-600">Saved.</p>
          }

          <button class="btn-primary" type="submit" [disabled]="saving()">
            {{ saving() ? 'Saving…' : 'Save' }}
          </button>
        </form>
      }
    </div>
  `
})
export class ProfileComponent implements OnInit {
  profile = signal<UserProfile | null>(null);
  loading = signal(true);
  tab = signal<'view' | 'edit'>('view');

  firstName = '';
  lastName = '';
  saving = signal(false);
  saved = signal(false);

  constructor(private userService: UserService) {}

  ngOnInit(): void {
    this.userService.getProfile().subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.firstName = profile.firstName;
        this.lastName = profile.lastName;
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  save(): void {
    this.saving.set(true);
    this.saved.set(false);
    this.userService.updateProfile(this.firstName, this.lastName).subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.saving.set(false);
        this.saved.set(true);
      },
      error: () => this.saving.set(false)
    });
  }
}
