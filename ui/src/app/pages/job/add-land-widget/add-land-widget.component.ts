import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap } from 'rxjs/operators';
import { Address, Land, LandAreaValue, LandService, addressLine, formatArea } from '../../../core/land.service';
import { LandAreaInputComponent } from '../../../shared/land-area-input/land-area-input.component';

@Component({
  selector: 'app-add-land-widget',
  standalone: true,
  imports: [CommonModule, FormsModule, LandAreaInputComponent],
  template: `
    <div class="border border-neutral-200 rounded-md p-md">
      @if (!creatingNew()) {
        <input
          class="input-field"
          type="text"
          placeholder="Search by address, deed, or survey plan number…"
          [(ngModel)]="query"
          (ngModelChange)="onQueryChange($event)"
        />

        @if (searching()) {
          <p class="text-xs text-neutral-500 mt-sm">Searching…</p>
        } @else if (query.trim().length > 0) {
          <div class="mt-sm space-y-xs">
            @for (land of results(); track land.landId) {
              <button
                type="button"
                class="w-full text-left px-md py-sm rounded hover:bg-neutral-100"
                (click)="choose(land)"
              >
                <span class="text-sm text-neutral-900">{{ addressLine(land) }}</span>
                @if (land.area.acres !== null || land.area.roods !== null || land.area.perches !== null) {
                  <span class="text-xs text-neutral-500 block">{{ formatArea(land.area) }}</span>
                }
              </button>
            }
            @if (results().length === 0) {
              <button
                type="button"
                class="w-full text-left px-md py-sm rounded hover:bg-neutral-100 text-sm text-primary-600"
                (click)="startCreate()"
              >
                + Create new land record
              </button>
            }
          </div>
        }
      } @else {
        <div class="space-y-sm">
          <p class="text-sm font-medium text-neutral-900">New land</p>
          <input class="input-field" type="text" placeholder="Street" [(ngModel)]="street" />
          <input class="input-field" type="text" placeholder="City" [(ngModel)]="city" />
          <input class="input-field" type="text" placeholder="District (optional)" [(ngModel)]="district" />
          <app-land-area-input [value]="area" (valueChange)="onAreaChange($event)" />
          @if (error()) {
            <p class="text-xs text-primary-500">{{ error() }}</p>
          }
          <div class="flex justify-end gap-sm">
            <button type="button" class="btn-secondary" (click)="reset()">Cancel</button>
            <button type="button" class="btn-primary" [disabled]="!street.trim() || creating()" (click)="createAndAdd()">
              {{ creating() ? 'Creating…' : 'Create & attach' }}
            </button>
          </div>
        </div>
      }
    </div>
  `
})
export class AddLandWidgetComponent {
  @Input() workspaceId = '';
  @Output() added = new EventEmitter<Land>();

  query = '';
  results = signal<Land[]>([]);
  searching = signal(false);
  creatingNew = signal(false);
  street = '';
  city = '';
  district = '';
  area: LandAreaValue = { acres: null, roods: null, perches: null, squareMeters: null, hectares: null };
  creating = signal(false);
  error = signal('');

  addressLine = addressLine;
  formatArea = formatArea;

  private queryChanged = new Subject<string>();

  constructor(private landService: LandService) {
    this.queryChanged
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        switchMap((q) => {
          if (!q.trim()) {
            this.searching.set(false);
            return [];
          }
          this.searching.set(true);
          return this.landService.search(this.workspaceId, q.trim());
        })
      )
      .subscribe({
        next: (lands) => {
          this.results.set(lands);
          this.searching.set(false);
        },
        error: () => this.searching.set(false)
      });
  }

  onQueryChange(value: string): void {
    this.queryChanged.next(value);
  }

  choose(land: Land): void {
    this.added.emit(land);
  }

  onAreaChange(value: Partial<LandAreaValue>): void {
    this.area = { acres: null, roods: null, perches: null, squareMeters: null, hectares: null, ...value };
  }

  startCreate(): void {
    this.street = this.query.trim();
    this.creatingNew.set(true);
  }

  createAndAdd(): void {
    if (!this.street.trim()) return;
    this.error.set('');
    this.creating.set(true);

    const address: Address = {
      street: this.street.trim(),
      city: this.city.trim() || null,
      district: this.district.trim() || null,
      postalCode: null,
      country: null
    };

    this.landService
      .create(this.workspaceId, {
        address,
        area: this.area
      })
      .subscribe({
        next: (land) => {
          this.creating.set(false);
          this.added.emit(land);
          this.reset();
        },
        error: (err) => {
          this.creating.set(false);
          this.error.set(err.error?.message ?? 'Could not create land record.');
        }
      });
  }

  reset(): void {
    this.query = '';
    this.results.set([]);
    this.creatingNew.set(false);
    this.street = '';
    this.city = '';
    this.district = '';
    this.area = { acres: null, roods: null, perches: null, squareMeters: null, hectares: null };
    this.error.set('');
  }
}
