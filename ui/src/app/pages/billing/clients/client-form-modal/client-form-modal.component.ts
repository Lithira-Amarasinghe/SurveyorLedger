import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Address, Client, ClientRequest, ClientService } from '../../../../core/billing.service';

@Component({
  selector: 'app-client-form-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-md" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">{{ editing ? 'Edit client' : 'New client' }}</h2>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Name</label>
            <input class="input-field" type="text" name="name" [(ngModel)]="name" required autofocus />
          </div>
          <div class="grid grid-cols-2 gap-sm">
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Phone</label>
              <input class="input-field" type="text" name="phone" [(ngModel)]="phone" />
            </div>
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Email</label>
              <input class="input-field" type="email" name="email" [(ngModel)]="email" />
            </div>
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Street</label>
            <input class="input-field" type="text" name="street" [(ngModel)]="street" />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">City</label>
            <input class="input-field" type="text" name="city" [(ngModel)]="city" />
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading() || !name.trim()">
              {{ loading() ? 'Saving…' : editing ? 'Save' : 'Create' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class ClientFormModalComponent implements OnInit {
  @Input() workspaceId = '';
  @Input() editing: Client | null = null;
  @Output() cancel = new EventEmitter<void>();
  @Output() saved = new EventEmitter<Client>();

  name = '';
  phone = '';
  email = '';
  street = '';
  city = '';
  loading = signal(false);
  error = signal('');

  constructor(private clientService: ClientService) {}

  ngOnInit(): void {
    if (this.editing) {
      this.name = this.editing.name;
      this.phone = this.editing.phone ?? '';
      this.email = this.editing.email ?? '';
      this.street = this.editing.address.street ?? '';
      this.city = this.editing.address.city ?? '';
    }
  }

  submit(): void {
    if (!this.name.trim()) return;
    this.error.set('');
    this.loading.set(true);

    const address: Address = { street: this.street.trim() || null, city: this.city.trim() || null, district: null, postalCode: null, country: null };
    const request: ClientRequest = {
      name: this.name.trim(),
      phone: this.phone.trim() || undefined,
      email: this.email.trim() || undefined,
      address
    };

    const save$ = this.editing
      ? this.clientService.update(this.workspaceId, this.editing.clientId, request)
      : this.clientService.create(this.workspaceId, request);

    save$.subscribe({
      next: client => {
        this.loading.set(false);
        this.saved.emit(client);
      },
      error: err => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not save client.');
      }
    });
  }
}
