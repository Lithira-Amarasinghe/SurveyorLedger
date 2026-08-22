import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Organization, OrganizationMember, OrganizationService } from '../../core/organization.service';

@Component({
  selector: 'app-organization-settings',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="p-lg max-w-2xl mx-auto space-y-lg">
      <h1 class="text-lg font-semibold text-neutral-900">Organization settings</h1>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (organization(); as org) {
        <div class="card">
          <h2 class="text-sm font-semibold text-neutral-900 mb-xs">Subscription</h2>
          <p class="text-xs text-neutral-500 mb-md">
            {{ org.workspaceCount }} of {{ org.maxWorkspaces >= 2147483647 ? '∞' : org.maxWorkspaces }} workspaces used.
          </p>
          <div class="flex items-center gap-sm">
            <select class="input-field w-40" [(ngModel)]="selectedTier">
              <option value="Free">Free</option>
              <option value="Pro">Pro</option>
              <option value="Business">Business</option>
            </select>
            <button type="button" class="btn-primary" [disabled]="savingTier()" (click)="saveTier()">
              {{ savingTier() ? 'Saving…' : 'Update tier' }}
            </button>
          </div>
          @if (tierError()) {
            <p class="text-sm text-primary-500 mt-xs">{{ tierError() }}</p>
          }
        </div>

        <div class="card">
          <h2 class="text-sm font-semibold text-neutral-900 mb-md">Members</h2>
          @if (members().length === 0) {
            <p class="text-sm text-neutral-500">No members yet.</p>
          } @else {
            <div class="space-y-sm">
              @for (member of members(); track member.userId) {
                <div class="flex items-center justify-between text-sm">
                  <div>
                    <span class="text-neutral-900">{{ member.firstName }} {{ member.lastName }}</span>
                    <span class="text-neutral-500 ml-xs">{{ member.email }}</span>
                    @if (member.isOwner) {
                      <span class="text-xs px-xs py-[1px] rounded bg-neutral-100 text-neutral-600 ml-xs">Owner</span>
                    }
                  </div>
                  @if (!member.isOwner) {
                    <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="removeMember(member)">Remove</button>
                  }
                </div>
              }
            </div>
          }
        </div>
      }
    </div>
  `
})
export class OrganizationSettingsComponent implements OnInit {
  organizationId = '';
  organization = signal<Organization | null>(null);
  members = signal<OrganizationMember[]>([]);
  loading = signal(true);
  savingTier = signal(false);
  tierError = signal('');
  selectedTier = 'Free';

  constructor(
    private route: ActivatedRoute,
    private organizationService: OrganizationService
  ) {}

  ngOnInit(): void {
    this.organizationId = this.route.snapshot.paramMap.get('id') ?? '';
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.organizationService.getById(this.organizationId).subscribe({
      next: (org) => {
        this.organization.set(org);
        this.selectedTier = org.tier;
        this.organizationService.getMembers(this.organizationId).subscribe({
          next: (members) => { this.members.set(members); this.loading.set(false); },
          error: () => this.loading.set(false)
        });
      },
      error: () => this.loading.set(false)
    });
  }

  saveTier(): void {
    this.tierError.set('');
    this.savingTier.set(true);
    this.organizationService.updateSubscription(this.organizationId, this.selectedTier).subscribe({
      next: (org) => { this.organization.set(org); this.savingTier.set(false); },
      error: (err) => {
        this.savingTier.set(false);
        this.tierError.set(err.error?.message ?? 'Could not update subscription.');
      }
    });
  }

  removeMember(member: OrganizationMember): void {
    this.organizationService.removeMember(this.organizationId, member.userId).subscribe({
      next: () => this.members.update(list => list.filter(m => m.userId !== member.userId))
    });
  }
}
