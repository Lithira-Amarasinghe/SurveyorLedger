import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';

/**
 * Two-step reset: request a code, then set a new password with it. Step one shows
 * "account not found" if the email has no registered account with a password.
 */
@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="min-h-screen flex items-center justify-center bg-neutral-50 px-lg">
      <div class="card w-full max-w-sm">
        <h1 class="text-lg font-semibold text-neutral-900">Reset password</h1>

        @if (step() === 'request') {
          <p class="text-sm text-neutral-600 mt-xs">We'll email you a code to reset your password.</p>

          <form class="mt-xl space-y-md" (ngSubmit)="requestCode()">
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Email</label>
              <input class="input-field" type="email" name="email" [(ngModel)]="email" required autocomplete="email" />
            </div>

            @if (error()) {
              <p class="text-sm text-primary-500">{{ error() }}</p>
            }

            <button class="btn-primary w-full" type="submit" [disabled]="loading() || !email.trim()">
              {{ loading() ? 'Sending…' : 'Send reset code' }}
            </button>
          </form>
        } @else {
          <p class="text-sm text-neutral-600 mt-xs">
            If an account exists for {{ email }}, a code is on its way. Enter it below with your new password.
          </p>

          <form class="mt-xl space-y-md" (ngSubmit)="resetPassword()">
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Reset code</label>
              <input class="input-field" type="text" name="otpCode" [(ngModel)]="otpCode" required inputmode="numeric" autocomplete="one-time-code" />
            </div>
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">New password</label>
              <input class="input-field" type="password" name="newPassword" [(ngModel)]="newPassword" required minlength="8" autocomplete="new-password" />
            </div>
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Confirm new password</label>
              <input class="input-field" type="password" name="confirmPassword" [(ngModel)]="confirmPassword" required autocomplete="new-password" />
            </div>

            @if (error()) {
              <p class="text-sm text-primary-500">{{ error() }}</p>
            }

            <button class="btn-primary w-full" type="submit" [disabled]="loading()">
              {{ loading() ? 'Resetting…' : 'Reset password' }}
            </button>
            <button type="button" class="btn-secondary w-full" [disabled]="loading()" (click)="step.set('request')">
              Use a different email
            </button>
          </form>
        }

        <p class="text-sm text-neutral-600 mt-lg text-center">
          <a routerLink="/auth/login">Back to sign in</a>
        </p>
      </div>
    </div>
  `
})
export class ForgotPasswordComponent {
  email = '';
  otpCode = '';
  newPassword = '';
  confirmPassword = '';

  step = signal<'request' | 'reset'>('request');
  loading = signal(false);
  error = signal('');

  constructor(private authService: AuthService, private router: Router) {}

  requestCode(): void {
    if (!this.email.trim()) return;
    this.error.set('');
    this.loading.set(true);

    this.authService.forgotPassword(this.email.trim()).subscribe({
      next: () => {
        this.loading.set(false);
        this.step.set('reset');
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not send the reset code.');
      }
    });
  }

  resetPassword(): void {
    this.error.set('');
    if (this.newPassword !== this.confirmPassword) {
      this.error.set('Passwords do not match.');
      return;
    }

    this.loading.set(true);
    this.authService.resetPassword(this.email.trim(), this.otpCode.trim(), this.newPassword).subscribe({
      next: () => this.router.navigate(['/auth/login'], { queryParams: { reset: '1', email: this.email.trim() } }),
      error: (err) => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not reset your password.');
      }
    });
  }
}
