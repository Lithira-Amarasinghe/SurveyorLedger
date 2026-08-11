import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink, Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="min-h-screen flex items-center justify-center bg-neutral-50 px-lg">
      <div class="card w-full max-w-sm">
        <h1 class="text-lg font-semibold text-neutral-900">Sign in</h1>
        <p class="text-sm text-neutral-600 mt-xs">Welcome back to SurveyorLedger.</p>

        @if (verifiedMessage()) {
          <p class="text-sm text-green-600 mt-md">{{ verifiedMessage() }}</p>
        }

        <form class="mt-xl space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Email</label>
            <input class="input-field" type="email" name="email" [(ngModel)]="email" required autocomplete="email" />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Password</label>
            <input class="input-field" type="password" name="password" [(ngModel)]="password" required autocomplete="current-password" />
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">
              {{ error() }}
              @if (unverifiedEmail()) {
                <a [routerLink]="['/auth/verify-otp']" [queryParams]="{ email: unverifiedEmail() }">Verify it now</a>
              }
            </p>
          }

          <button class="btn-primary w-full" type="submit" [disabled]="loading()">
            {{ loading() ? 'Signing in…' : 'Sign in' }}
          </button>
        </form>

        <p class="text-sm text-neutral-600 mt-lg text-center">
          <a routerLink="/auth/forgot-password">Forgot your password?</a>
        </p>
        <p class="text-sm text-neutral-600 mt-sm text-center">
          No account? <a routerLink="/auth/register">Register</a>
        </p>
      </div>
    </div>
  `
})
export class LoginComponent {
  email = '';
  password = '';
  loading = signal(false);
  error = signal('');
  unverifiedEmail = signal('');
  verifiedMessage = signal('');

  constructor(private authService: AuthService, private router: Router, private route: ActivatedRoute) {
    if (this.route.snapshot.queryParamMap.get('verified') === '1') {
      this.verifiedMessage.set('Email verified. Please log in to continue.');
      this.email = this.route.snapshot.queryParamMap.get('email') ?? '';
    } else if (this.route.snapshot.queryParamMap.get('reset') === '1') {
      this.verifiedMessage.set('Password reset. Sign in with your new password.');
      this.email = this.route.snapshot.queryParamMap.get('email') ?? '';
    }
  }

  submit(): void {
    this.error.set('');
    this.unverifiedEmail.set('');
    this.loading.set(true);
    this.authService.login(this.email, this.password).subscribe({
      next: () => {
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/';
        this.router.navigateByUrl(returnUrl);
      },
      error: (err) => {
        this.loading.set(false);
        const message = err.error?.message ?? 'Invalid email or password.';
        this.error.set(message);
        if (message.toLowerCase().includes('not verified')) {
          this.unverifiedEmail.set(this.email);
        }
      }
    });
  }
}
