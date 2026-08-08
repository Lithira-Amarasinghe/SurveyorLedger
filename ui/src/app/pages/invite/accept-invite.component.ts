import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { InvitationService, InvitationPreview } from '../../core/invitation.service';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-accept-invite',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="min-h-screen flex items-center justify-center bg-neutral-50 px-lg">
      <div class="card w-full max-w-sm">

        @if (loadingPreview()) {
          <p class="text-sm text-neutral-500">Loading invite…</p>
        } @else if (previewError()) {
          <h1 class="text-lg font-semibold text-neutral-900">Invite unavailable</h1>
          <p class="text-sm text-neutral-600 mt-xs">{{ previewError() }}</p>
          <p class="text-sm text-neutral-600 mt-lg text-center"><a routerLink="/auth/login">Back to sign in</a></p>
        } @else if (preview(); as p) {

          <h1 class="text-lg font-semibold text-neutral-900">Join {{ p.workspaceName }}</h1>
          <p class="text-sm text-neutral-600 mt-xs">You've been invited as <strong>{{ p.role }}</strong>.</p>

          @if (mismatchEmail()) {
            <p class="text-sm text-primary-500 mt-lg">
              This invite is for {{ p.email }}, you're signed in as {{ mismatchEmail() }}.
            </p>
            <button class="btn-secondary w-full mt-md" (click)="logoutAndRetry()">Log out</button>
          } @else if (authenticated()) {
            @if (acceptError()) {
              <p class="text-sm text-primary-500 mt-lg">{{ acceptError() }}</p>
            }
            <button class="btn-primary w-full mt-lg" [disabled]="accepting()" (click)="accept()">
              {{ accepting() ? 'Joining…' : 'Accept invite' }}
            </button>
          } @else {
            <div class="flex gap-sm mt-lg border-b border-neutral-200">
              <button
                type="button"
                class="px-md py-sm text-sm font-medium border-b-2 -mb-px"
                [class.border-primary-500]="mode() === 'login'"
                [class.text-primary-600]="mode() === 'login'"
                [class.border-transparent]="mode() !== 'login'"
                [class.text-neutral-500]="mode() !== 'login'"
                (click)="mode.set('login')"
              >Sign in</button>
              <button
                type="button"
                class="px-md py-sm text-sm font-medium border-b-2 -mb-px"
                [class.border-primary-500]="mode() === 'register'"
                [class.text-primary-600]="mode() === 'register'"
                [class.border-transparent]="mode() !== 'register'"
                [class.text-neutral-500]="mode() !== 'register'"
                (click)="mode.set('register')"
              >Create account</button>
            </div>

            <form class="mt-lg space-y-md" (ngSubmit)="submitAuth()">
              @if (mode() === 'register') {
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
              }
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Email</label>
                <input class="input-field" type="email" name="email" [value]="p.email" disabled />
              </div>
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Password</label>
                <input class="input-field" type="password" name="password" [(ngModel)]="password" required minlength="8" [autocomplete]="mode() === 'register' ? 'new-password' : 'current-password'" />
              </div>
              @if (mode() === 'register') {
                <div>
                  <label class="block text-xs font-medium text-neutral-700 mb-xs">Confirm password</label>
                  <input class="input-field" type="password" name="confirmPassword" [(ngModel)]="confirmPassword" required autocomplete="new-password" />
                </div>
              }

              @if (authError()) {
                <p class="text-sm text-primary-500">{{ authError() }}</p>
              }

              <button class="btn-primary w-full" type="submit" [disabled]="authLoading()">
                {{ authLoading() ? 'Please wait…' : (mode() === 'login' ? 'Sign in' : 'Create account') }}
              </button>
            </form>
          }
        }
      </div>
    </div>
  `
})
export class AcceptInviteComponent implements OnInit {
  token = '';
  loadingPreview = signal(true);
  previewError = signal('');
  preview = signal<InvitationPreview | null>(null);

  authenticated = signal(false);
  mismatchEmail = signal('');
  mode = signal<'login' | 'register'>('login');

  firstName = '';
  lastName = '';
  password = '';
  confirmPassword = '';
  authLoading = signal(false);
  authError = signal('');

  accepting = signal(false);
  acceptError = signal('');

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private invitationService: InvitationService,
    private authService: AuthService
  ) {}

  ngOnInit(): void {
    this.token = this.route.snapshot.paramMap.get('token') ?? '';

    this.invitationService.getByToken(this.token).subscribe({
      next: (preview) => {
        this.loadingPreview.set(false);
        if (preview.expired) {
          this.previewError.set('This invite has expired or was already used.');
          return;
        }
        this.preview.set(preview);
        this.checkAuthState(preview);
      },
      error: (err) => {
        this.loadingPreview.set(false);
        this.previewError.set(err.error?.message ?? 'This invite link is invalid.');
      }
    });
  }

  private checkAuthState(preview: InvitationPreview): void {
    this.authService.isAuthenticated$.subscribe(isAuthenticated => {
      this.authenticated.set(isAuthenticated);
      if (!isAuthenticated) {
        this.mismatchEmail.set('');
        return;
      }
      const currentEmail = this.authService.getCurrentEmail();
      if (currentEmail && currentEmail.toLowerCase() !== preview.email.toLowerCase()) {
        this.mismatchEmail.set(currentEmail);
      } else {
        this.mismatchEmail.set('');
      }
    });
  }

  submitAuth(): void {
    const preview = this.preview();
    if (!preview) return;

    this.authError.set('');

    if (this.mode() === 'register') {
      if (this.password !== this.confirmPassword) {
        this.authError.set('Passwords do not match.');
        return;
      }

      this.authLoading.set(true);
      this.invitationService.registerFromInvitation(this.token, this.password, this.confirmPassword, this.firstName, this.lastName).subscribe({
        next: () => {
          // Account is created already verified and the invite is auto-accepted server-side -
          // no tokens are issued, so send them to log in like any other new account.
          this.router.navigate(['/auth/login'], { queryParams: { verified: '1', email: preview.email } });
        },
        error: (err) => {
          this.authLoading.set(false);
          this.authError.set(err.error?.message ?? 'Could not create your account.');
        }
      });
      return;
    }

    this.authLoading.set(true);
    this.authService.login(preview.email, this.password).subscribe({
      next: () => this.accept(),
      error: (err) => {
        this.authLoading.set(false);
        const message = err.error?.message ?? 'Something went wrong.';
        if (message.toLowerCase().includes('not verified')) {
          this.router.navigate(['/auth/verify-otp'], {
            queryParams: { email: preview.email, returnUrl: `/invite/${this.token}` }
          });
          return;
        }
        this.authError.set(message);
      }
    });
  }

  accept(): void {
    this.authLoading.set(false);
    this.acceptError.set('');
    this.accepting.set(true);

    this.invitationService.accept(this.token).subscribe({
      next: (result) => this.router.navigate(['/app/workspace', result.workspaceId]),
      error: (err) => {
        this.accepting.set(false);
        const message = err.error?.message ?? 'Could not accept the invite.';
        if (err.status === 403) {
          this.mismatchEmail.set(this.authService.getCurrentEmail() ?? '');
        } else if (err.status === 410) {
          this.previewError.set('This invite expired while you were completing sign-in.');
          this.preview.set(null);
        } else {
          this.acceptError.set(message);
        }
      }
    });
  }

  logoutAndRetry(): void {
    this.authService.logout();
    window.location.reload();
  }
}
