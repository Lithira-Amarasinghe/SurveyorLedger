import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-verify-otp',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="min-h-screen flex items-center justify-center bg-neutral-50 px-lg">
      <div class="card w-full max-w-sm">
        <h1 class="text-lg font-semibold text-neutral-900">Verify your email</h1>
        <p class="text-sm text-neutral-600 mt-xs">Code sent to <strong>{{ email }}</strong></p>

        <form class="mt-xl space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">6-digit code</label>
            <input
              class="input-field text-center tracking-[0.5em] font-mono"
              type="text"
              inputmode="numeric"
              maxlength="6"
              name="otpCode"
              [(ngModel)]="otpCode"
              required
            />
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <button class="btn-primary w-full" type="submit" [disabled]="loading()">
            {{ loading() ? 'Verifying…' : 'Verify' }}
          </button>
        </form>

        <p class="text-sm text-neutral-600 mt-lg text-center">
          <a routerLink="/auth/login">Back to sign in</a>
        </p>
      </div>
    </div>
  `
})
export class VerifyOtpComponent {
  email = '';
  otpCode = '';
  loading = signal(false);
  error = signal('');

  constructor(private route: ActivatedRoute, private authService: AuthService, private router: Router) {
    this.email = this.route.snapshot.queryParamMap.get('email') ?? '';
  }

  submit(): void {
    this.error.set('');
    this.loading.set(true);
    this.authService.verifyOtp(this.email, this.otpCode).subscribe({
      next: () => {
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/';
        this.router.navigateByUrl(returnUrl);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Invalid or expired code.');
      }
    });
  }
}
