import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { Organization, OrganizationService } from '../../core/organization.service';
import { CurrentOrganizationService } from '../../core/current-organization.service';
import { CreateOrganizationModalComponent } from './create-modal/create-organization-modal.component';

@Component({
  selector: 'app-organizations-list',
  standalone: true,
  imports: [CommonModule, RouterLink, CreateOrganizationModalComponent],
  template: `
    <div class="p-lg max-w-4xl mx-auto">
      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Your organizations</h1>
        <button class="btn-primary" (click)="modalOpen.set(true)">New organization</button>
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (organizations().length === 0) {
        <div class="card text-center text-sm text-neutral-500">No organizations yet. Create one to get started.</div>
      } @else {
        <div class="grid gap-md sm:grid-cols-2">
          @for (org of organizations(); track org.id) {
            <div class="card">
              <button type="button" class="text-left w-full hover:opacity-80" (click)="switchTo(org)">
                <div class="flex items-center justify-between">
                  <span class="font-medium text-neutral-900">{{ org.name }}</span>
                  <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ org.tier }}</span>
                </div>
                <p class="text-xs text-neutral-500 mt-sm">{{ org.workspaceCount }} / {{ maxWorkspacesLabel(org) }} workspaces</p>
                <p class="text-xs text-neutral-500 mt-xs">Role: {{ org.callerRoles.join(', ') }}</p>
              </button>
              <a [routerLink]="['/app/organizations', org.id]" class="mt-sm inline-block text-xs text-primary-500 hover:text-primary-600">Manage</a>
            </div>
          }
        </div>
      }
    </div>

    @if (modalOpen()) {
      <app-create-organization-modal (cancel)="modalOpen.set(false)" (created)="onCreated($event)" />
    }
  `
})
export class OrganizationsListComponent implements OnInit {
  organizations = signal<Organization[]>([]);
  loading = signal(true);
  modalOpen = signal(false);

  constructor(
    private organizationService: OrganizationService,
    private currentOrg: CurrentOrganizationService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.organizationService.list().subscribe({
      next: (orgs) => { this.organizations.set(orgs); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  maxWorkspacesLabel(org: Organization): string {
    return org.maxWorkspaces >= 2147483647 ? '∞' : String(org.maxWorkspaces);
  }

  switchTo(org: Organization): void {
    this.currentOrg.set(org);
    this.router.navigate(['/app/dashboard']);
  }

  onCreated(org: Organization): void {
    this.modalOpen.set(false);
    this.organizations.update(list => [...list, org]);
    this.switchTo(org);
  }
}
