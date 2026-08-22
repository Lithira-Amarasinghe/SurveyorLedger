import { Component, OnInit, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { OrganizationService, Organization } from '../core/organization.service';
import { CurrentOrganizationService } from '../core/current-organization.service';

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

      @if (currentOrg.current(); as org) {
        <div class="relative">
          <button
            type="button"
            class="flex items-center gap-xs px-md py-xs rounded text-sm text-neutral-700 hover:bg-neutral-100"
            (click)="orgMenuOpen.set(!orgMenuOpen())"
          >
            <span class="font-medium">{{ org.name }}</span>
            <span class="text-xs px-xs py-[1px] rounded bg-neutral-100 text-neutral-600">{{ org.tier }}</span>
            <span class="text-neutral-400">▾</span>
          </button>

          @if (orgMenuOpen()) {
            <div class="absolute left-0 mt-xs w-64 card p-xs shadow-lg z-10" (mouseleave)="orgMenuOpen.set(false)">
              @for (o of organizations(); track o.id) {
                <button
                  type="button"
                  class="w-full text-left px-md py-sm text-sm rounded hover:bg-neutral-100 flex items-center justify-between"
                  [class.bg-primary-50]="o.id === org.id"
                  (click)="switchTo(o)"
                >
                  <span>{{ o.name }}</span>
                  @if (o.id === org.id) {
                    <span class="text-primary-500">✓</span>
                  }
                </button>
              }
              <div class="border-t border-neutral-100 mt-xs pt-xs">
                <a routerLink="/app/organizations" class="block px-md py-sm text-sm text-neutral-700 hover:bg-neutral-100 rounded" (click)="orgMenuOpen.set(false)">
                  Manage organizations
                </a>
              </div>
            </div>
          }
        </div>
      }

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
            <a routerLink="/app/invitations" class="block px-md py-sm text-sm text-neutral-700 hover:bg-neutral-100 rounded" (click)="menuOpen.set(false)">Invitations</a>
            <button type="button" class="w-full text-left px-md py-sm text-sm text-neutral-700 hover:bg-neutral-100 rounded" (click)="logout()">Logout</button>
          </div>
        }
      </div>
    </header>
  `
})
export class TopbarComponent implements OnInit {
  paletteOpen = output<void>();
  menuToggle = output<void>();
  menuOpen = signal(false);
  orgMenuOpen = signal(false);
  organizations = signal<Organization[]>([]);

  constructor(
    private authService: AuthService,
    private organizationService: OrganizationService,
    protected currentOrg: CurrentOrganizationService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.organizationService.list().subscribe(orgs => this.organizations.set(orgs));
  }

  initials(): string {
    return 'U';
  }

  switchTo(org: Organization): void {
    this.currentOrg.set(org);
    this.orgMenuOpen.set(false);
    this.router.navigate(['/app/dashboard']);
  }

  logout(): void {
    this.authService.logout();
    window.location.href = '/';
  }
}
