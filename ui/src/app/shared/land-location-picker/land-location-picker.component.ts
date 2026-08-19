import { Component, ElementRef, EventEmitter, Input, OnChanges, OnDestroy, OnInit, Output, SimpleChanges, ViewChild, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import * as L from 'leaflet';

// An inline SVG divIcon instead of Leaflet's default raster marker - no image file to
// fail to load, and it stays crisp at any zoom/DPI. Every point is equal (no "primary"
// pin) so one icon covers all of them; a dashed gray pin marks a not-yet-named pending
// point so it's visible the instant the map is clicked, before the name is saved.
function pinSvg(fill: string): string {
  return `<svg width="26" height="34" viewBox="0 0 32 42" xmlns="http://www.w3.org/2000/svg">
    <path d="M16 0C7.16 0 0 7.16 0 16c0 11 16 26 16 26s16-15 16-26C32 7.16 24.84 0 16 0z" fill="${fill}"/>
    <circle cx="16" cy="16" r="6" fill="#ffffff"/>
  </svg>`;
}
const pointIcon = L.divIcon({ className: 'land-map-point', html: pinSvg('#2563eb'), iconSize: [26, 34], iconAnchor: [13, 34] });
const pendingIcon = L.divIcon({ className: 'land-map-pending-point', html: pinSvg('#9ca3af'), iconSize: [26, 34], iconAnchor: [13, 34] });

export interface LandMapMarker {
  id: string;
  name: string;
  lat: number;
  lng: number;
  /** Defaults true (draggable when the picker itself isn't readonly). Set false for markers the caller never wants moved - e.g. the public link's already-set points, which are read-only even though the map still accepts new clicks. */
  editable?: boolean;
}

interface NominatimResult {
  display_name: string;
  lat: string;
  lon: string;
}

/**
 * Leaflet + OpenStreetMap point picker - no API key, no billing. One map, click (or
 * search) to add a point directly (a gray pending pin appears instantly, before it's
 * named/saved), drag an existing point to move it. Auto-fits the view to every marker
 * so the map always opens showing what's already there. Used both from the authenticated
 * land-detail-panel and the public unauthenticated set-location/map-view pages, so it
 * owns no save/HTTP logic of its own - it only emits what the user did, the caller
 * persists it.
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
        <button
          type="button"
          class="text-sm"
          [class.text-primary-600]="!addMode()"
          [class.text-primary-500]="addMode()"
          [class.font-medium]="addMode()"
          (click)="toggleAddMode()"
        >
          {{ addMode() ? '✕ Cancel adding a point' : '+ Add a point' }}
        </button>
      }
      <div class="relative">
        <div #mapEl class="w-full rounded-md border border-neutral-200" [class]="heightClass" [class.cursor-crosshair]="addMode()"></div>
        @if (addMode()) {
          <div class="absolute top-sm left-sm z-[1000] bg-primary-600 text-white text-xs px-sm py-xs rounded-md shadow pointer-events-none">
            Tap the map to place a point
          </div>
        }
        @if (markers.length > 0) {
          <button
            type="button"
            class="absolute top-sm right-sm z-[1000] bg-white shadow rounded-md px-sm py-xs text-xs text-neutral-700 hover:bg-neutral-50 border border-neutral-200"
            title="Fit view to every point"
            (click)="fitAllMarkers()"
          >
            ⤢ Fit all
          </button>
        }
      </div>
      @if (!readonly) {
        <p class="text-xs text-neutral-500">Drag an existing pin to move it.</p>
      }
    </div>
  `
})
export class LandLocationPickerComponent implements OnInit, OnChanges, OnDestroy {
  @Input() initialLat: number | null = null;
  @Input() initialLng: number | null = null;
  /** View-only mode: pan/zoom the map, no search, no click-to-add, no drag. */
  @Input() readonly = false;
  /** Tailwind height class for the map container - callers embedding a small inline preview vs. the full picker want different sizes. */
  @Input() heightClass = 'h-72';
  /** Every point to render - the caller owns the list (and persistence); this component only draws it and reports clicks/drags. */
  @Input() markers: LandMapMarker[] = [];
  /** A just-clicked, not-yet-named point - rendered as a distinct gray pin so the click feels instant instead of silently doing nothing until the name is saved. */
  @Input() pendingPoint: { lat: number; lng: number } | null = null;
  @Output() pointAdded = new EventEmitter<{ lat: number; lng: number }>();
  @Output() pointMoved = new EventEmitter<{ id: string; lat: number; lng: number }>();

  @ViewChild('mapEl', { static: true }) mapEl!: ElementRef<HTMLDivElement>;

  searchQuery = '';
  searchResults = signal<NominatimResult[]>([]);
  searching = signal(false);
  searchError = signal('');
  /** Click-to-add must be explicitly armed - an un-armed map click just pans/zooms like normal, so a stray tap (very easy to trigger on a touchscreen, or while just scrolling past the map) never drops an accidental pending point. Auto-disarms after one point so the next incidental tap doesn't add another. */
  addMode = signal(false);

  private map!: L.Map;
  private markerLayers = new Map<string, L.Marker>();
  private pendingLayer: L.Marker | null = null;
  private hasFitBounds = false;
  private hasCorrectedInitialSize = false;
  private resizeObserver: ResizeObserver | null = null;

  ngOnInit(): void {
    const firstMarker = this.markers[0];
    const startLat = this.initialLat ?? firstMarker?.lat ?? 7.8731; // Sri Lanka centroid - a reasonable default when nothing exists yet
    const startLng = this.initialLng ?? firstMarker?.lng ?? 80.7718;
    const startZoom = this.initialLat !== null || firstMarker ? 16 : 7;

    this.map = L.map(this.mapEl.nativeElement).setView([startLat, startLng], startZoom);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '&copy; OpenStreetMap contributors',
      maxZoom: 19
    }).addTo(this.map);

    if (!this.readonly) {
      this.map.on('click', (e: L.LeafletMouseEvent) => {
        if (!this.addMode()) return;
        this.pointAdded.emit({ lat: e.latlng.lat, lng: e.latlng.lng });
        this.addMode.set(false);
      });
    }

    this.renderMarkers();
    this.renderPendingMarker();
    this.fitToMarkers();

    // Leaflet lays out tiles/markers against the container's size at construction time - if
    // this component mounts inside a collapsed/tabbed/still-animating panel, that size can be
    // zero or stale, leaving blank tiles until the user manually zooms. invalidateSize() alone
    // only fixes the tiles though: fitToMarkers()/setView() already ran above against that same
    // wrong size, so the view's center/zoom can still be locked onto the wrong place - panning
    // every marker out of the visible area even once the tiles repaint. Re-running fitToMarkers
    // once the container reaches its real size fixes both at once; the flag keeps this a one-time
    // correction so it doesn't fight a manual pan/zoom on every later resize.
    this.resizeObserver = new ResizeObserver(() => {
      this.map.invalidateSize();
      if (!this.hasCorrectedInitialSize) {
        this.hasCorrectedInitialSize = true;
        this.hasFitBounds = false;
        this.fitToMarkers();
      }
    });
    this.resizeObserver.observe(this.mapEl.nativeElement);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.map) return;
    if (changes['markers']) {
      this.renderMarkers();
      this.fitToMarkers();
    }
    if (changes['pendingPoint']) {
      this.renderPendingMarker();
    }
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    this.map?.remove();
  }

  toggleAddMode(): void {
    this.addMode.update(v => !v);
  }

  /**
   * Fits the view to every marker once, the first time markers arrive (e.g. after an async
   * fetch resolves) - never again after that. Re-fitting on every later markers change (a
   * marker being dragged to a new position, say) would yank the view out from under whatever
   * the user is doing mid-drag. Once that first fit has happened, only an explicit "Fit all"
   * click (fitAllMarkers) or the one-time post-mount size correction re-fits.
   */
  private fitToMarkers(): void {
    if (this.markers.length === 0 || this.hasFitBounds) return;
    this.applyFit();
  }

  /** Manual re-fit, triggered by the "Fit all" control - ignores hasFitBounds so it always works, even after the one-time auto-fit already happened. */
  fitAllMarkers(): void {
    if (this.markers.length === 0) return;
    this.applyFit();
  }

  private applyFit(): void {
    if (this.markers.length === 1) {
      this.map.setView([this.markers[0].lat, this.markers[0].lng], 16);
    } else {
      const bounds = L.latLngBounds(this.markers.map(m => [m.lat, m.lng] as [number, number]));
      this.map.fitBounds(bounds, { padding: [24, 24] });
    }
    this.hasFitBounds = true;
  }

  private renderPendingMarker(): void {
    this.pendingLayer?.remove();
    this.pendingLayer = null;
    if (this.pendingPoint) {
      this.pendingLayer = L.marker([this.pendingPoint.lat, this.pendingPoint.lng], { icon: pendingIcon }).addTo(this.map);
    }
  }

  private renderMarkers(): void {
    const seenIds = new Set(this.markers.map(m => m.id));
    for (const [id, layer] of this.markerLayers) {
      if (!seenIds.has(id)) {
        layer.remove();
        this.markerLayers.delete(id);
      }
    }

    for (const point of this.markers) {
      const draggable = !this.readonly && point.editable !== false;

      const existing = this.markerLayers.get(point.id);
      if (existing) {
        existing.setLatLng([point.lat, point.lng]);
        existing.setTooltipContent(point.name);
        continue;
      }

      const marker = L.marker([point.lat, point.lng], { icon: pointIcon, draggable })
        .addTo(this.map)
        .bindTooltip(point.name, { permanent: true, direction: 'top', className: 'land-map-label', offset: [0, -30] });

      if (draggable) {
        marker.on('dragend', () => {
          const pos = marker.getLatLng();
          this.pointMoved.emit({ id: point.id, lat: pos.lat, lng: pos.lng });
        });
      }

      this.markerLayers.set(point.id, marker);
    }
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
          this.searchError.set('No results found — click the map to add a point.');
        }
      })
      .catch(() => {
        this.searching.set(false);
        this.searchError.set('Search unavailable — click the map to add a point.');
      });
  }

  chooseSearchResult(r: NominatimResult): void {
    const lat = parseFloat(r.lat);
    const lng = parseFloat(r.lon);
    this.map.setView([lat, lng], 16);
    this.pointAdded.emit({ lat, lng });
    this.searchResults.set([]);
    this.searchQuery = r.display_name;
  }
}
