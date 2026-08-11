import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Address, Land, LandService } from '../../../core/land.service';
import { OwnerPickerComponent, OwnerValue } from '../owner-picker/owner-picker.component';

@Component({
  selector: 'app-create-land-modal',
  standalone: true,
  imports: [CommonModule, FormsModule, OwnerPickerComponent],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-md" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">New land</h2>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Street</label>
            <input class="input-field" type="text" name="street" [(ngModel)]="street" required autofocus />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">City</label>
            <input class="input-field" type="text" name="city" [(ngModel)]="city" />
          </div>
          <div class="flex gap-sm">
            <div class="flex-1">
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Size</label>
              <input class="input-field" type="number" name="size" [(ngModel)]="size" />
            </div>
            <div class="flex-1">
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Unit</label>
              <input class="input-field" type="text" name="sizeUnit" [(ngModel)]="sizeUnit" placeholder="e.g. acres" />
            </div>
          </div>

          <app-owner-picker [value]="owner" (valueChange)="owner = $event" />

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading() || !street.trim()">
              {{ loading() ? 'Creating…' : 'Create' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class CreateLandModalComponent {
  @Input() workspaceId = '';
  @Output() cancel = new EventEmitter<void>();
  @Output() created = new EventEmitter<Land>();

  street = '';
  city = '';
  size: number | null = null;
  sizeUnit = '';
  owner: OwnerValue = {};
  loading = signal(false);
  error = signal('');

  constructor(private landService: LandService) {}

  submit(): void {
    if (!this.street.trim()) return;
    this.error.set('');
    this.loading.set(true);

    const address: Address = { street: this.street.trim(), city: this.city.trim() || null, district: null, postalCode: null, country: null };

    this.landService
      .create(this.workspaceId, {
        address,
        size: this.size ?? undefined,
        sizeUnit: this.sizeUnit.trim() || undefined,
        ...this.owner
      })
      .subscribe({
        next: (land) => {
          this.loading.set(false);
          this.created.emit(land);
        },
        error: (err) => {
          this.loading.set(false);
          this.error.set(err.error?.message ?? 'Could not create land record.');
        }
      });
  }
}
