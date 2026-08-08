import { Component, computed, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';

interface CommandItem {
  label: string;
  path: string;
}

const ROUTES: CommandItem[] = [
  { label: 'Workspace', path: '/app/workspace' },
  { label: 'Profile', path: '/app/profile' },
];

@Component({
  selector: 'app-command-palette',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-start justify-center pt-[15vh]" (click)="close.emit()">
      <div class="card w-full max-w-lg p-0 overflow-hidden" (click)="$event.stopPropagation()">
        <input
          #searchInput
          class="w-full px-lg py-md text-sm border-0 border-b border-neutral-200 focus:outline-none focus:ring-0"
          type="text"
          placeholder="Jump to…"
          [value]="query()"
          (input)="query.set(searchInput.value)"
          autofocus
        />
        <div class="max-h-64 overflow-y-auto">
          @for (item of results(); track item.path) {
            <button
              type="button"
              class="w-full text-left px-lg py-sm text-sm text-neutral-800 hover:bg-primary-50"
              (click)="go(item.path)"
            >
              {{ item.label }}
            </button>
          } @empty {
            <p class="px-lg py-md text-sm text-neutral-500">No matches.</p>
          }
        </div>
      </div>
    </div>
  `
})
export class CommandPaletteComponent {
  close = output<void>();
  query = signal('');

  results = computed(() =>
    ROUTES.filter(r => r.label.toLowerCase().includes(this.query().toLowerCase()))
  );

  constructor(private router: Router) {}

  go(path: string): void {
    this.router.navigate([path]);
    this.close.emit();
  }
}
