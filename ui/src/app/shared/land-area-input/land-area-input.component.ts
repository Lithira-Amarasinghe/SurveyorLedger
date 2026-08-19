import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  LandAreaValue,
  acresRoodsPerchesToSquareMeters,
  hectaresToSquareMeters,
  squareMetersToAcresRoodsPerches,
  squareMetersToHectares
} from '../../core/land.service';

type AreaTab = 'arp' | 'sqm' | 'ha';

/**
 * Unit-system-tabbed area input - Acres/Roods/Perches, Square meters, or Hectares.
 * Emits only the active tab's field(s) populated, matching the backend's "exactly one
 * unit system per write" contract. No HTTP inside the component - controlled, same
 * pattern as LandLocationPickerComponent/OwnerPickerComponent.
 */
@Component({
  selector: 'app-land-area-input',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="space-y-sm">
      <div class="flex gap-xs text-xs">
        <button type="button" class="px-sm py-xs rounded" [class.bg-primary-100]="tab === 'arp'" (click)="selectTab('arp')">
          Acres/Roods/Perches
        </button>
        <button type="button" class="px-sm py-xs rounded" [class.bg-primary-100]="tab === 'sqm'" (click)="selectTab('sqm')">
          Square meters
        </button>
        <button type="button" class="px-sm py-xs rounded" [class.bg-primary-100]="tab === 'ha'" (click)="selectTab('ha')">
          Hectares
        </button>
      </div>

      @if (tab === 'arp') {
        <div class="flex gap-sm">
          <input class="input-field w-24" type="number" min="0" placeholder="Acres" [(ngModel)]="acres" (ngModelChange)="onArpChange()" />
          <select class="input-field w-24" [(ngModel)]="roods" (ngModelChange)="onArpChange()">
            <option [ngValue]="0">0 Roods</option>
            <option [ngValue]="1">1 Rood</option>
            <option [ngValue]="2">2 Roods</option>
            <option [ngValue]="3">3 Roods</option>
          </select>
          <input class="input-field w-28" type="number" min="0" max="39.99" step="0.01" placeholder="Perches" [(ngModel)]="perches" (ngModelChange)="onArpChange()" />
        </div>
      } @else if (tab === 'sqm') {
        <input class="input-field w-40" type="number" min="0" step="0.01" placeholder="Square meters" [(ngModel)]="squareMeters" (ngModelChange)="onSqmChange()" />
      } @else {
        <input class="input-field w-40" type="number" min="0" step="0.0001" placeholder="Hectares" [(ngModel)]="hectares" (ngModelChange)="onHaChange()" />
      }

      <p class="text-xs text-neutral-500">{{ previewLine() }}</p>
    </div>
  `
})
export class LandAreaInputComponent implements OnChanges {
  @Input() value: LandAreaValue = { acres: null, roods: null, perches: null, squareMeters: null, hectares: null };
  @Output() valueChange = new EventEmitter<Partial<LandAreaValue>>();

  tab: AreaTab = 'arp';
  acres: number | null = null;
  roods = 0;
  perches: number | null = null;
  squareMeters: number | null = null;
  hectares: number | null = null;

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['value']) return;
    this.acres = this.value.acres;
    this.roods = this.value.roods ?? 0;
    this.perches = this.value.perches;
    this.squareMeters = this.value.squareMeters;
    this.hectares = this.value.hectares;
  }

  selectTab(tab: AreaTab): void {
    this.tab = tab;
  }

  onArpChange(): void {
    this.valueChange.emit({ acres: this.acres, roods: this.roods, perches: this.perches, squareMeters: null, hectares: null });
  }

  onSqmChange(): void {
    this.valueChange.emit({ acres: null, roods: null, perches: null, squareMeters: this.squareMeters, hectares: null });
  }

  onHaChange(): void {
    this.valueChange.emit({ acres: null, roods: null, perches: null, squareMeters: null, hectares: this.hectares });
  }

  previewLine(): string {
    const sqm = this.currentSquareMeters();
    if (sqm === null) return 'Enter a value to see the equivalent in other units.';

    if (this.tab === 'arp') {
      return `≈ ${sqm.toLocaleString(undefined, { maximumFractionDigits: 0 })} m² · ${squareMetersToHectares(sqm).toFixed(2)} ha`;
    }
    const { acres, roods, perches } = squareMetersToAcresRoodsPerches(sqm);
    if (this.tab === 'sqm') {
      return `≈ ${acres}A ${roods}R ${perches}P · ${squareMetersToHectares(sqm).toFixed(2)} ha`;
    }
    return `≈ ${acres}A ${roods}R ${perches}P · ${sqm.toLocaleString(undefined, { maximumFractionDigits: 0 })} m²`;
  }

  private currentSquareMeters(): number | null {
    if (this.tab === 'arp') {
      if (this.acres === null && this.perches === null) return null;
      return acresRoodsPerchesToSquareMeters(this.acres ?? 0, this.roods, this.perches ?? 0);
    }
    if (this.tab === 'sqm') {
      return this.squareMeters === null ? null : this.squareMeters;
    }
    return this.hectares === null ? null : hectaresToSquareMeters(this.hectares);
  }
}
