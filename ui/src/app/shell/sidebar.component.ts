import { Component, inject, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { CurrentWorkspaceService } from '../core/current-workspace.service';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive],
  template: `
    <nav class="flex flex-col h-full py-lg">
      <div class="px-lg flex items-center gap-sm">
        <div class="w-7 h-7 rounded bg-primary-500 text-white flex items-center justify-center text-xs font-bold">SL</div>
        <span class="font-semibold text-neutral-900 text-sm">SurveyorLedger</span>
      </div>

      @if (workspace(); as ws) {
        <div class="mt-lg px-lg">
          <p class="text-xs text-neutral-500 truncate">{{ ws.name }}</p>
        </div>

        <div class="mt-sm flex-1 px-sm space-y-xs">
          <a
            [routerLink]="['/app/workspace', ws.workspaceId]"
            routerLinkActive="bg-primary-50 text-primary-600"
            [routerLinkActiveOptions]="{ exact: true }"
            class="flex items-center gap-sm px-md py-sm rounded text-sm text-neutral-700 hover:bg-neutral-100"
            (click)="navigate.emit()"
          >
            Overview
          </a>
          <a
            [routerLink]="['/app/workspace', ws.workspaceId, 'jobs']"
            routerLinkActive="bg-primary-50 text-primary-600"
            class="flex items-center gap-sm px-md py-sm rounded text-sm text-neutral-700 hover:bg-neutral-100"
            (click)="navigate.emit()"
          >
            Jobs
          </a>
          <a
            [routerLink]="['/app/workspace', ws.workspaceId, 'lands']"
            routerLinkActive="bg-primary-50 text-primary-600"
            class="flex items-center gap-sm px-md py-sm rounded text-sm text-neutral-700 hover:bg-neutral-100"
            (click)="navigate.emit()"
          >
            Land
          </a>
          <a
            [routerLink]="['/app/workspace', ws.workspaceId, 'billing', 'invoices']"
            routerLinkActive="bg-primary-50 text-primary-600"
            class="flex items-center gap-sm px-md py-sm rounded text-sm text-neutral-700 hover:bg-neutral-100"
            (click)="navigate.emit()"
          >
            Billing
          </a>
          <a
            [routerLink]="['/app/workspace', ws.workspaceId, 'members']"
            routerLinkActive="bg-primary-50 text-primary-600"
            class="flex items-center gap-sm px-md py-sm rounded text-sm text-neutral-700 hover:bg-neutral-100"
            (click)="navigate.emit()"
          >
            Members
          </a>
          @if (isAdmin()) {
            <a
              [routerLink]="['/app/workspace', ws.workspaceId, 'roles']"
              routerLinkActive="bg-primary-50 text-primary-600"
              class="flex items-center gap-sm px-md py-sm rounded text-sm text-neutral-700 hover:bg-neutral-100"
              (click)="navigate.emit()"
            >
              Roles
            </a>
            <a
              [routerLink]="['/app/workspace', ws.workspaceId, 'reports']"
              routerLinkActive="bg-primary-50 text-primary-600"
              class="flex items-center gap-sm px-md py-sm rounded text-sm text-neutral-700 hover:bg-neutral-100"
              (click)="navigate.emit()"
            >
              Reports
            </a>
          }
        </div>

        <div class="px-sm space-y-xs">
          <a
            routerLink="/app/dashboard"
            class="flex items-center gap-sm px-md py-sm rounded text-sm text-neutral-700 hover:bg-neutral-100"
            (click)="navigate.emit()"
          >
            ← All workspaces
          </a>
          <button
            type="button"
            class="w-full flex items-center gap-sm px-md py-sm rounded text-sm text-neutral-700 hover:bg-neutral-100"
            (click)="logout()"
          >
            Logout
          </button>
        </div>
      } @else {
        <div class="mt-xl flex-1 px-sm space-y-xs">
          <a
            routerLink="/app/dashboard"
            routerLinkActive="bg-primary-50 text-primary-600"
            class="flex items-center gap-sm px-md py-sm rounded text-sm text-neutral-700 hover:bg-neutral-100"
            (click)="navigate.emit()"
          >
            Dashboard
          </a>
          <a
            routerLink="/app/profile"
            routerLinkActive="bg-primary-50 text-primary-600"
            class="flex items-center gap-sm px-md py-sm rounded text-sm text-neutral-700 hover:bg-neutral-100"
            (click)="navigate.emit()"
          >
            Profile
          </a>
          <a
            routerLink="/app/invitations"
            routerLinkActive="bg-primary-50 text-primary-600"
            class="flex items-center gap-sm px-md py-sm rounded text-sm text-neutral-700 hover:bg-neutral-100"
            (click)="navigate.emit()"
          >
            Invitations
          </a>
        </div>

        <div class="px-sm">
          <button
            type="button"
            class="w-full flex items-center gap-sm px-md py-sm rounded text-sm text-neutral-700 hover:bg-neutral-100"
            (click)="logout()"
          >
            Logout
          </button>
        </div>
      }
    </nav>
  `
})
export class SidebarComponent {
  private authService = inject(AuthService);
  private currentWorkspace = inject(CurrentWorkspaceService);

  navigate = output<void>();
  workspace = this.currentWorkspace.current;

  isAdmin(): boolean {
    return this.currentWorkspace.current()?.role === 'Admin';
  }

  logout(): void {
    this.authService.logout();
    window.location.href = '/';
  }
}
