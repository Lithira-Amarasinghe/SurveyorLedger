import { Component, EventEmitter, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Workspace, WorkspaceService } from '../../../core/workspace.service';
import { Organization, OrganizationService } from '../../../core/organization.service';
import { CurrentOrganizationService } from '../../../core/current-organization.service';

const NEW_ORG_VALUE = '__new__';

@Component({
  selector: 'app-create-workspace-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-md" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">New workspace</h2>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Organization</label>
            <select class="input-field" name="organization" [(ngModel)]="selectedOrgId">
              @for (org of organizations(); track org.id) {
                <option [value]="org.id">{{ org.name }}</option>
              }
              <option [value]="newOrgSentinel">+ Create new organization</option>
            </select>
          </div>

          @if (selectedOrgId === newOrgSentinel) {
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">New organization name</label>
              <input class="input-field" type="text" name="newOrgName" [(ngModel)]="newOrgName" required />
            </div>
          }

          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Name</label>
            <input class="input-field" type="text" name="name" [(ngModel)]="name" required />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Description</label>
            <textarea class="input-field" name="description" rows="3" [(ngModel)]="description"></textarea>
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading()">
              {{ loading() ? 'Creating…' : 'Create' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class CreateWorkspaceModalComponent implements OnInit {
  @Output() cancel = new EventEmitter<void>();
  @Output() created = new EventEmitter<Workspace>();

  newOrgSentinel = NEW_ORG_VALUE;
  organizations = signal<Organization[]>([]);
  selectedOrgId = '';
  newOrgName = '';

  name = '';
  description = '';
  loading = signal(false);
  error = signal('');

  constructor(
    private workspaceService: WorkspaceService,
    private organizationService: OrganizationService,
    private currentOrg: CurrentOrganizationService
  ) {}

  ngOnInit(): void {
    this.organizationService.list().subscribe(orgs => {
      this.organizations.set(orgs);
      const active = this.currentOrg.current();
      this.selectedOrgId = active && orgs.some(o => o.id === active.id)
        ? active.id
        : (orgs[0]?.id ?? this.newOrgSentinel);
    });
  }

  submit(): void {
    this.error.set('');
    this.loading.set(true);

    if (this.selectedOrgId === this.newOrgSentinel) {
      this.organizationService.create(this.newOrgName).subscribe({
        next: (org) => this.createWorkspace(org.id),
        error: (err) => {
          this.loading.set(false);
          this.error.set(err.error?.message ?? 'Could not create organization.');
        }
      });
      return;
    }

    this.createWorkspace(this.selectedOrgId);
  }

  private createWorkspace(organizationId: string): void {
    this.workspaceService.create(this.name, this.description, organizationId).subscribe({
      next: (workspace) => {
        this.loading.set(false);
        this.created.emit(workspace);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not create workspace.');
      }
    });
  }
}
