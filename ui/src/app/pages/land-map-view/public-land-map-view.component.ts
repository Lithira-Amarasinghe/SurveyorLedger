import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { LandMapViewLinkPreview, LandMapViewLinkService } from '../../core/land-map-view-link.service';
import { LandLocationPickerComponent, LandMapMarker } from '../../shared/land-location-picker/land-location-picker.component';
import { LandService } from '../../core/land.service';

/**
 * Standalone public page, reached by people with no account - no app shell, no auth
 * guard. Purely read-only: shows every point on the map plus a per-point "Open in Google
 * Maps"/"Copy link" so a client can navigate to the site, with no way to add, move, or
 * delete anything (unlike public-set-location.component.ts's add-a-point flow).
 */
@Component({
  selector: 'app-public-land-map-view',
  standalone: true,
  imports: [CommonModule, LandLocationPickerComponent],
  template: `
    <div class="min-h-screen flex items-center justify-center bg-neutral-50 p-lg">
      <div class="max-w-lg w-full bg-white rounded-md shadow p-lg">
        @if (loading()) {
          <p class="text-sm text-neutral-500">Loading…</p>
        } @else if (error()) {
          <p class="text-sm text-neutral-600">{{ error() }}</p>
        } @else {
          <h1 class="text-lg font-semibold text-neutral-900 mb-xs">Land location</h1>
          <p class="text-sm text-neutral-500 mb-md">{{ preview()!.addressLine }}</p>

          <app-land-location-picker [markers]="markers()" [readonly]="true" heightClass="h-72" />

          @if (preview()!.points.length > 0) {
            <div class="space-y-xs mt-md">
              @for (p of preview()!.points; track p.id) {
                <div class="flex items-center justify-between px-md py-sm rounded bg-neutral-50 text-sm">
                  <span class="text-neutral-900">{{ p.name }}</span>
                  <span class="flex items-center gap-sm text-xs">
                    <a [href]="mapsUrl(p.latitude, p.longitude)" target="_blank" rel="noopener" class="text-primary-600 hover:text-primary-700">
                      Open in Maps
                    </a>
                    <button type="button" class="text-neutral-500 hover:text-neutral-700" (click)="copyLink(p)">
                      {{ copiedId() === p.id ? 'Copied!' : 'Copy link' }}
                    </button>
                  </span>
                </div>
              }
            </div>
          } @else {
            <p class="text-sm text-neutral-500">No points have been set yet.</p>
          }
        }
      </div>
    </div>
  `
})
export class PublicLandMapViewComponent implements OnInit {
  loading = signal(true);
  error = signal('');
  preview = signal<LandMapViewLinkPreview | null>(null);
  copiedId = signal<string | null>(null);

  markers = signal<LandMapMarker[]>([]);

  constructor(private route: ActivatedRoute, private linkService: LandMapViewLinkService, private landService: LandService) {}

  ngOnInit(): void {
    const token = this.route.snapshot.paramMap.get('token') ?? '';
    this.linkService.getPreview(token).subscribe({
      next: (preview) => {
        this.preview.set(preview);
        this.markers.set(preview.points.map(p => ({ id: p.id, name: p.name, lat: p.latitude, lng: p.longitude, editable: false })));
        this.loading.set(false);
      },
      error: () => {
        this.error.set('This link is no longer valid.');
        this.loading.set(false);
      }
    });
  }

  mapsUrl(lat: number, lng: number): string {
    return this.landService.googleMapsUrl(lat, lng);
  }

  copyLink(p: { id: string; latitude: number; longitude: number }): void {
    navigator.clipboard.writeText(this.mapsUrl(p.latitude, p.longitude));
    this.copiedId.set(p.id);
    setTimeout(() => this.copiedId.set(null), 2000);
  }
}
