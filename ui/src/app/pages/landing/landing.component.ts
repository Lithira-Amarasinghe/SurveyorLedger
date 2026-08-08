import { Component } from '@angular/core';
import { AsyncPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [AsyncPipe, RouterLink],
  template: `
    <div class="min-h-screen bg-neutral-50">
      <header class="flex items-center justify-between px-lg py-md max-w-5xl mx-auto">
        <div class="flex items-center gap-sm">
          <div class="w-7 h-7 rounded bg-primary-500 text-white flex items-center justify-center text-xs font-bold">SL</div>
          <span class="font-semibold text-neutral-900 text-sm">SurveyorLedger</span>
        </div>

        @if (authService.isAuthenticated$ | async) {
          <a routerLink="/app/dashboard" class="btn-primary">Go to Dashboard</a>
        } @else {
          <div class="flex items-center gap-sm">
            <a routerLink="/auth/login" class="btn-secondary">Login</a>
            <a routerLink="/auth/register" class="btn-primary">Get Started</a>
          </div>
        }
      </header>

      <main class="max-w-3xl mx-auto px-lg py-24 text-center">
        <h1 class="text-4xl font-semibold text-neutral-900 tracking-tight">
          Run your survey jobs from one place
        </h1>
        <p class="mt-lg text-lg text-neutral-600">
          SurveyorLedger keeps your workspaces, teams, and job data organized —
          built for survey businesses that need clean records, not spreadsheets.
        </p>

        <div class="mt-2xl flex items-center justify-center gap-sm">
          @if (authService.isAuthenticated$ | async) {
            <a routerLink="/app/dashboard" class="btn-primary">Go to Dashboard</a>
          } @else {
            <a routerLink="/auth/register" class="btn-primary">Get Started</a>
            <a routerLink="/auth/login" class="btn-secondary">Login</a>
          }
        </div>
      </main>
    </div>
  `
})
export class LandingComponent {
  constructor(protected authService: AuthService) {}
}
