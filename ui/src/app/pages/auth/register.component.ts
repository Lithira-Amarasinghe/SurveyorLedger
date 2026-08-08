import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink, Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="min-h-screen flex items-center justify-center bg-neutral-50 px-lg">
      <div class="card w-full max-w-sm">
        <h1 class="text-lg font-semibold text-neutral-900">Create account</h1>
        <p class="text-sm text-neutral-600 mt-xs">Start managing survey jobs.</p>

        <form class="mt-xl space-y-md" (ngSubmit)="submit()">
          <div class="grid grid-cols-2 gap-md">
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">First name</label>
              <input class="input-field" type="text" name="firstName" [(ngModel)]="firstName" required />
            </div>
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Last name</label>
              <input class="input-field" type="text" name="lastName" [(ngModel)]="lastName" required />
            </div>
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Email</label>
            <input class="input-field" type="email" name="email" [(ngModel)]="email" required autocomplete="email" />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Password</label>
            <input class="input-field" type="password" name="password" [(ngModel)]="password" required minlength="8" autocomplete="new-password" />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Confirm password</label>
            <input class="input-field" type="password" name="confirmPassword" [(ngModel)]="confirmPassword" required autocomplete="new-password" />
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <button class="btn-primary w-full" type="submit" [disabled]="loading()">
            {{ loading() ? 'Creating…' : 'Create account' }}
          </button>
        </form>

        <p class="text-sm text-neutral-600 mt-lg text-center">
          Already have an account? <a routerLink="/auth/login">Sign in</a>
        </p>
      </div>
    </div>
  `
})
export class RegisterComponent {
  email = '';
  password = '';
  confirmPassword = '';
  firstName = '';
  lastName = '';
  loading = signal(false);
  error = signal('');

  constructor(private authService: AuthService, private router: Router) {}

  submit(): void {
    this.error.set('');

    if (this.password !== this.confirmPassword) {
      this.error.set('Passwords do not match.');
      return;
    }

    this.loading.set(true);
    this.authService.register(this.email, this.password, this.confirmPassword, this.firstName, this.lastName).subscribe({
      next: () => this.router.navigate(['/auth/verify-otp'], { queryParams: { email: this.email } }),
      error: (err) => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Registration failed.');
      }
    });
  }
}
