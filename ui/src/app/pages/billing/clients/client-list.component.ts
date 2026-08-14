import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { Client, ClientService } from '../../../core/billing.service';
import { CurrentWorkspaceService } from '../../../core/current-workspace.service';
import { BillingTabsComponent } from '../billing-tabs.component';
import { ClientFormModalComponent } from './client-form-modal/client-form-modal.component';

interface ClientRow {
  client: Client;
  balance: number;
}

@Component({
  selector: 'app-client-list',
  standalone: true,
  imports: [CommonModule, BillingTabsComponent, ClientFormModalComponent],
  template: `
    <div class="p-lg max-w-4xl mx-auto">
      <app-billing-tabs [workspaceId]="workspaceId" active="clients" />

      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Clients</h1>
        <button class="btn-primary" (click)="openCreate()">New client</button>
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (error()) {
        <div class="card text-sm text-primary-500">
          {{ error() }}
          <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
        </div>
      } @else if (rows().length === 0) {
        <div class="card text-center text-sm text-neutral-500">No clients yet. Create one to get started.</div>
      } @else {
        <div class="card p-0 overflow-x-auto">
          <table class="w-full text-sm">
            <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
              <tr>
                <th class="text-left px-lg py-sm font-medium">Name</th>
                <th class="text-left px-lg py-sm font-medium">Phone</th>
                <th class="text-left px-lg py-sm font-medium">Email</th>
                <th class="text-left px-lg py-sm font-medium">Outstanding balance</th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.client.clientId) {
                <tr class="border-t border-neutral-200 cursor-pointer hover:bg-neutral-50" (click)="openEdit(row.client)">
                  <td class="px-lg py-sm text-neutral-900">{{ row.client.name }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ row.client.phone ?? '—' }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ row.client.email ?? '—' }}</td>
                  <td class="px-lg py-sm" [class.text-primary-600]="row.balance > 0" [class.font-medium]="row.balance > 0">
                    {{ row.balance | number: '1.2-2' }}
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>

    @if (modalOpen()) {
      <app-client-form-modal [workspaceId]="workspaceId" [editing]="editingClient()" (cancel)="closeModal()" (saved)="onSaved()" />
    }
  `
})
export class ClientListComponent implements OnInit {
  workspaceId = '';
  rows = signal<ClientRow[]>([]);
  loading = signal(true);
  error = signal('');
  modalOpen = signal(false);
  editingClient = signal<Client | null>(null);

  constructor(private clientService: ClientService, private currentWorkspace: CurrentWorkspaceService) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    this.clientService.search(this.workspaceId).subscribe({
      next: clients => {
        if (clients.length === 0) {
          this.rows.set([]);
          this.loading.set(false);
          return;
        }
        forkJoin(
          clients.map(client =>
            this.clientService.getBalance(this.workspaceId, client.clientId).pipe(
              catchError(() => of({ clientId: client.clientId, outstandingBalance: 0 }))
            )
          )
        ).subscribe(balances => {
          this.rows.set(clients.map((client, i) => ({ client, balance: balances[i].outstandingBalance })));
          this.loading.set(false);
        });
      },
      error: err => {
        this.error.set(err.error?.message ?? 'Could not load clients.');
        this.loading.set(false);
      }
    });
  }

  openCreate(): void {
    this.editingClient.set(null);
    this.modalOpen.set(true);
  }

  openEdit(client: Client): void {
    this.editingClient.set(client);
    this.modalOpen.set(true);
  }

  closeModal(): void {
    this.modalOpen.set(false);
  }

  onSaved(): void {
    this.modalOpen.set(false);
    this.fetch();
  }
}
