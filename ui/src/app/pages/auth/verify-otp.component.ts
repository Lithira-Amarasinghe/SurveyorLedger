import { Component, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';

const RESEND_COOLDOWN_SECONDS = 60;

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
          Didn't get a code?
          <button
            type="button"
            class="text-primary-600 disabled:text-neutral-400 disabled:cursor-not-allowed"
            [disabled]="resendCooldown() > 0 || resending()"
            (click)="resend()"
          >
            {{ resendCooldown() > 0 ? 'Resend in ' + resendCooldown() + 's' : (resending() ? 'Sending…' : 'Resend code') }}
          </button>
        </p>
        <p class="text-sm text-neutral-600 mt-sm text-center">
          <a routerLink="/auth/login">Back to sign in</a>
        </p>
      </div>
    </div>
  `
})
export class VerifyOtpComponent implements OnDestroy {
  email = '';
  otpCode = '';
  loading = signal(false);
  error = signal('');
  resending = signal(false);
  resendCooldown = signal(0);
  private cooldownTimer?: ReturnType<typeof setInterval>;

  constructor(private route: ActivatedRoute, private authService: AuthService, private router: Router) {
    this.email = this.route.snapshot.queryParamMap.get('email') ?? '';
    this.startCooldown();
  }

  ngOnDestroy(): void {
    if (this.cooldownTimer) clearInterval(this.cooldownTimer);
  }

  submit(): void {
    this.error.set('');
    this.loading.set(true);
    this.authService.verifyOtp(this.email, this.otpCode).subscribe({
      next: () => {
        this.router.navigate(['/auth/login'], { queryParams: { verified: '1', email: this.email } });
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Invalid or expired code.');
      }
    });
  }

  resend(): void {
    this.error.set('');
    this.resending.set(true);
    this.authService.resendOtp(this.email).subscribe({
      next: () => {
        this.resending.set(false);
        this.startCooldown();
      },
      error: (err) => {
        this.resending.set(false);
        this.error.set(err.error?.message ?? 'Could not resend the code.');
      }
    });
  }

  private startCooldown(): void {
    this.resendCooldown.set(RESEND_COOLDOWN_SECONDS);
    if (this.cooldownTimer) clearInterval(this.cooldownTimer);
    this.cooldownTimer = setInterval(() => {
      const next = this.resendCooldown() - 1;
      if (next <= 0) {
        this.resendCooldown.set(0);
        clearInterval(this.cooldownTimer);
      } else {
        this.resendCooldown.set(next);
      }
    }, 1000);
  }
}
