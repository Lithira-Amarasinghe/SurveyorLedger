import { Component, ElementRef, EventEmitter, Input, OnDestroy, OnInit, Output, ViewChild, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import * as L from 'leaflet';

// An inline SVG divIcon instead of Leaflet's default raster marker - no image file to
// fail to load (the previous fix vendored PNGs locally, but a drawn icon removes the
// failure mode entirely: nothing to 404, nothing to flash as a broken-image placeholder
// while it loads), and it stays crisp at any zoom/DPI.
const pinIcon = L.divIcon({
  className: 'land-location-pin',
  html: `<svg width="32" height="42" viewBox="0 0 32 42" xmlns="http://www.w3.org/2000/svg">
    <path d="M16 0C7.16 0 0 7.16 0 16c0 11 16 26 16 26s16-15 16-26C32 7.16 24.84 0 16 0z" fill="#dc2626"/>
    <circle cx="16" cy="16" r="6" fill="#ffffff"/>
  </svg>`,
  iconSize: [32, 42],
  iconAnchor: [16, 42]
});

interface NominatimResult {
  display_name: string;
  lat: string;
  lon: string;
}

/**
 * Leaflet + OpenStreetMap pin picker - no API key, no billing. Used both from the
 * authenticated land-detail-panel and the public unauthenticated set-location page,
 * so it owns no save/HTTP logic of its own - it only emits the chosen point.
 */
@Component({
  selector: 'app-land-location-picker',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="space-y-sm">
      @if (!readonly) {
        <div class="flex gap-sm">
          <input
            class="input-field flex-1"
            type="text"
            placeholder="Search for an address…"
            [(ngModel)]="searchQuery"
            (keydown.enter)="search()"
          />
          <button type="button" class="btn-secondary" (click)="search()" [disabled]="searching()">
            {{ searching() ? 'Searching…' : 'Search' }}
          </button>
        </div>
        @if (searchError()) {
          <p class="text-xs text-neutral-500">{{ searchError() }}</p>
        }
        @if (searchResults().length > 0) {
          <div class="border border-neutral-200 rounded-md divide-y divide-neutral-100 max-h-40 overflow-y-auto">
            @for (r of searchResults(); track r.display_name) {
              <button
                type="button"
                class="w-full text-left px-md py-sm text-sm hover:bg-neutral-50"
                (click)="chooseSearchResult(r)"
              >
                {{ r.display_name }}
              </button>
            }
          </div>
        }
      }
      <div #mapEl class="w-full rounded-md border border-neutral-200" [class]="heightClass"></div>
      @if (!readonly) {
        <p class="text-xs text-neutral-500">
          {{ chosenLat !== null ? (chosenLat | number: '1.6-6') + ', ' + (chosenLng | number: '1.6-6') : 'Click the map or search to place the pin.' }}
        </p>
        <div class="flex justify-end">
          <button type="button" class="btn-primary" [disabled]="chosenLat === null" (click)="confirm()">
            Use this location
          </button>
        </div>
      }
    </div>
  `
})
export class LandLocationPickerComponent implements OnInit, OnDestroy {
  @Input() initialLat: number | null = null;
  @Input() initialLng: number | null = null;
  /** View-only mode: pan/zoom the map, no search, no click/drag-to-place, no confirm button. */
  @Input() readonly = false;
  /** Tailwind height class for the map container - callers embedding a small inline preview vs. the full picker modal want different sizes. */
  @Input() heightClass = 'h-72';
  @Output() locationChosen = new EventEmitter<{ lat: number; lng: number }>();

  @ViewChild('mapEl', { static: true }) mapEl!: ElementRef<HTMLDivElement>;

  searchQuery = '';
  searchResults = signal<NominatimResult[]>([]);
  searching = signal(false);
  searchError = signal('');
  chosenLat: number | null = null;
  chosenLng: number | null = null;

  private map!: L.Map;
  private marker: L.Marker | null = null;

  ngOnInit(): void {
    const startLat = this.initialLat ?? 7.8731; // Sri Lanka centroid - a reasonable default when no pin exists yet
    const startLng = this.initialLng ?? 80.7718;
    const startZoom = this.initialLat !== null ? 16 : 7;

    this.map = L.map(this.mapEl.nativeElement).setView([startLat, startLng], startZoom);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '&copy; OpenStreetMap contributors',
      maxZoom: 19
    }).addTo(this.map);

    if (this.initialLat !== null && this.initialLng !== null) {
      this.placePin(this.initialLat, this.initialLng);
    }

    if (!this.readonly) {
      this.map.on('click', (e: L.LeafletMouseEvent) => {
        this.placePin(e.latlng.lat, e.latlng.lng);
      });
    }
  }

  ngOnDestroy(): void {
    this.map?.remove();
  }

  search(): void {
    const q = this.searchQuery.trim();
    if (!q) return;

    this.searching.set(true);
    this.searchError.set('');
    this.searchResults.set([]);

    fetch(`https://nominatim.openstreetmap.org/search?format=json&limit=5&q=${encodeURIComponent(q)}`)
      .then(res => {
        if (!res.ok) throw new Error('Search request failed');
        return res.json() as Promise<NominatimResult[]>;
      })
      .then(results => {
        this.searchResults.set(results);
        this.searching.set(false);
        if (results.length === 0) {
          this.searchError.set('No results found — click the map to place the pin.');
        }
      })
      .catch(() => {
        this.searching.set(false);
        this.searchError.set('Search unavailable — click the map to place the pin.');
      });
  }

  chooseSearchResult(r: NominatimResult): void {
    const lat = parseFloat(r.lat);
    const lng = parseFloat(r.lon);
    this.map.setView([lat, lng], 16);
    this.placePin(lat, lng);
    this.searchResults.set([]);
    this.searchQuery = r.display_name;
  }

  private placePin(lat: number, lng: number): void {
    this.chosenLat = lat;
    this.chosenLng = lng;

    if (this.marker) {
      this.marker.setLatLng([lat, lng]);
    } else {
      this.marker = L.marker([lat, lng], { icon: pinIcon, draggable: !this.readonly }).addTo(this.map);
      if (!this.readonly) {
        this.marker.on('dragend', () => {
          const pos = this.marker!.getLatLng();
          this.chosenLat = pos.lat;
          this.chosenLng = pos.lng;
        });
      }
    }
  }

  confirm(): void {
    if (this.chosenLat === null || this.chosenLng === null) return;
    this.locationChosen.emit({ lat: this.chosenLat, lng: this.chosenLng });
  }
}
