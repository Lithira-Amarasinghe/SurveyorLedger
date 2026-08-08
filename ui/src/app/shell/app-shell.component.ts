import { Component, HostListener, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet } from '@angular/router';
import { SidebarComponent } from './sidebar.component';
import { TopbarComponent } from './topbar.component';
import { CommandPaletteComponent } from './command-palette.component';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [CommonModule, RouterOutlet, SidebarComponent, TopbarComponent, CommandPaletteComponent],
  template: `
    <div class="h-screen flex bg-neutral-50">
      <aside
        class="w-60 border-r border-neutral-200 bg-white fixed inset-y-0 left-0 z-40 transition-transform md:static md:translate-x-0"
        [class.-translate-x-full]="!sidebarOpen()"
      >
        <app-sidebar (navigate)="sidebarOpen.set(false)" />
      </aside>

      @if (sidebarOpen()) {
        <div class="fixed inset-0 bg-neutral-900/30 z-30 md:hidden" (click)="sidebarOpen.set(false)"></div>
      }

      <div class="flex-1 flex flex-col min-w-0">
        <app-topbar (paletteOpen)="paletteOpen.set(true)" (menuToggle)="sidebarOpen.set(!sidebarOpen())" />
        <main class="flex-1 overflow-y-auto">
          <router-outlet />
        </main>
      </div>
    </div>

    @if (paletteOpen()) {
      <app-command-palette (close)="paletteOpen.set(false)" />
    }
  `
})
export class AppShellComponent {
  sidebarOpen = signal(false);
  paletteOpen = signal(false);

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      this.paletteOpen.set(true);
    }
    if (event.key === 'Escape') {
      this.paletteOpen.set(false);
    }
  }
}
