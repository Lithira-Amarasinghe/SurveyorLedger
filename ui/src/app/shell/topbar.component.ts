import { Component, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-topbar',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <header class="h-14 border-b border-neutral-200 bg-white flex items-center justify-between px-lg gap-md">
      <button
        type="button"
        class="md:hidden text-neutral-600 hover:text-neutral-900"
        (click)="menuToggle.emit()"
        aria-label="Toggle menu"
      >
        ☰
      </button>

      <button
        type="button"
        class="flex-1 max-w-sm flex items-center gap-sm px-md py-xs bg-neutral-100 rounded text-sm text-neutral-500 hover:bg-neutral-200 text-left"
        (click)="paletteOpen.emit()"
      >
        <span>Search…</span>
        <span class="ml-auto text-xs border border-neutral-300 rounded px-xs bg-white">⌘K</span>
      </button>

      <div class="relative">
        <button
          type="button"
          class="w-8 h-8 rounded-full bg-primary-500 text-white text-xs font-semibold flex items-center justify-center"
          (click)="menuOpen.set(!menuOpen())"
        >
          {{ initials() }}
        </button>

        @if (menuOpen()) {
          <div class="absolute right-0 mt-xs w-40 card p-xs shadow-lg" (mouseleave)="menuOpen.set(false)">
            <a routerLink="/app/profile" class="block px-md py-sm text-sm text-neutral-700 hover:bg-neutral-100 rounded" (click)="menuOpen.set(false)">Profile</a>
            <button type="button" class="w-full text-left px-md py-sm text-sm text-neutral-700 hover:bg-neutral-100 rounded" (click)="logout()">Logout</button>
          </div>
        }
      </div>
    </header>
  `
})
export class TopbarComponent {
  paletteOpen = output<void>();
  menuToggle = output<void>();
  menuOpen = signal(false);

  constructor(private authService: AuthService) {}

  initials(): string {
    return 'U';
  }

  logout(): void {
    this.authService.logout();
    window.location.href = '/';
  }
}
