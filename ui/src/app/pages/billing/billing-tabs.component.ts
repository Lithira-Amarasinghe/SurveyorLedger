import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';

@Component({
  selector: 'app-billing-tabs',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive],
  template: `
    <div class="flex gap-sm border-b border-neutral-200 mb-lg">
      <a
        [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices']"
        routerLinkActive="border-primary-500 text-primary-600"
        class="px-md py-sm text-sm font-medium text-neutral-600 border-b-2 border-transparent hover:text-neutral-900"
      >
        Invoices
      </a>
      <a
        [routerLink]="['/app/workspace', workspaceId, 'billing', 'quotations']"
        routerLinkActive="border-primary-500 text-primary-600"
        class="px-md py-sm text-sm font-medium text-neutral-600 border-b-2 border-transparent hover:text-neutral-900"
      >
        Quotations
      </a>
      <a
        [routerLink]="['/app/workspace', workspaceId, 'billing', 'clients']"
        routerLinkActive="border-primary-500 text-primary-600"
        class="px-md py-sm text-sm font-medium text-neutral-600 border-b-2 border-transparent hover:text-neutral-900"
      >
        Clients
      </a>
    </div>
  `
})
export class BillingTabsComponent {
  @Input() workspaceId = '';
}
