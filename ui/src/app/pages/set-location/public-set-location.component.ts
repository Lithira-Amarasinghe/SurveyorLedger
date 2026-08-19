import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { LandLocationLinkPreview, LandLocationLinkService } from '../../core/land-location-link.service';
import { LandLocationPickerComponent, LandMapMarker } from '../../shared/land-location-picker/land-location-picker.component';

/**
 * Standalone public page, reached by people with no account - no app shell,
 * no auth guard. Mirrors PublicDocumentUploadComponent's structure. Shows every point
 * already set as fixed (non-draggable) pins and lets the visitor add more - they can
 * never edit or delete an existing one, matching the add-only token endpoint.
 */
@Component({
  selector: 'app-public-set-location',
  standalone: true,
  imports: [CommonModule, FormsModule, LandLocationPickerComponent],
  template: `
    <div class="min-h-screen flex items-center justify-center bg-neutral-50 p-lg">
      <div class="max-w-lg w-full bg-white rounded-md shadow p-lg">
        @if (loading()) {
          <p class="text-sm text-neutral-500">Loading…</p>
        } @else if (error()) {
          <p class="text-sm text-neutral-600">{{ error() }}</p>
        } @else {
          <h1 class="text-lg font-semibold text-neutral-900 mb-xs">Set land location</h1>
          <p class="text-sm text-neutral-500 mb-md">{{ preview()!.addressLine }}</p>

          @if (allMarkers().length > 0) {
            <p class="text-xs text-neutral-500 mb-xs">Already set:</p>
            <ul class="text-xs text-neutral-700 mb-md list-disc list-inside">
              @for (m of allMarkers(); track m.id) {
                <li>{{ m.name }}</li>
              }
            </ul>
          }

          <app-land-location-picker
            [markers]="allMarkers()"
            [pendingPoint]="pendingLocation()"
            (pointAdded)="onPointAdded($event)"
          />

          @if (pendingName() !== null) {
            <div class="mt-sm flex gap-sm">
              <input class="input-field flex-1" placeholder="Name this point (e.g. Front gate)" [(ngModel)]="pendingName" />
              <button type="button" class="btn-primary" (click)="savePoint()" [disabled]="saving()">
                {{ saving() ? 'Saving…' : 'Save point' }}
              </button>
            </div>
          }
          @if (savedMessage()) {
            <p class="text-xs text-primary-600 mt-sm">{{ savedMessage() }}</p>
          }
          @if (saveError()) {
            <p class="text-xs text-primary-500 mt-sm">{{ saveError() }}</p>
          }
        }
      </div>
    </div>
  `
})
export class PublicSetLocationComponent implements OnInit {
  loading = signal(true);
  error = signal('');
  preview = signal<LandLocationLinkPreview | null>(null);
  saving = signal(false);
  saveError = signal('');
  savedMessage = signal('');

  pendingName = signal<string | null>(null);
  pendingLocation = signal<{ lat: number; lng: number } | null>(null);

  allMarkers = signal<LandMapMarker[]>([]);

  private token = '';

  constructor(private route: ActivatedRoute, private linkService: LandLocationLinkService) {}

  ngOnInit(): void {
    this.token = this.route.snapshot.paramMap.get('token') ?? '';
    this.linkService.getPreview(this.token).subscribe({
      next: (preview) => {
        this.preview.set(preview);
        this.allMarkers.set(preview.points.map(p => ({ id: p.id, name: p.name, lat: p.latitude, lng: p.longitude, editable: false })));
        this.loading.set(false);
      },
      error: () => {
        this.error.set('This link is no longer valid.');
        this.loading.set(false);
      }
    });
  }

  onPointAdded(location: { lat: number; lng: number }): void {
    this.pendingLocation.set(location);
    this.pendingName.set('');
    this.savedMessage.set('');
    this.saveError.set('');
  }

  savePoint(): void {
    const name = (this.pendingName() ?? '').trim();
    const pending = this.pendingLocation();
    if (!name || !pending) return;

    this.saving.set(true);
    this.saveError.set('');
    this.linkService.addPoint(this.token, { name, latitude: pending.lat, longitude: pending.lng }).subscribe({
      next: (point) => {
        this.saving.set(false);
        this.pendingName.set(null);
        this.pendingLocation.set(null);
        this.savedMessage.set('Point saved — you can add another, or close this page.');
        this.allMarkers.update(markers => [...markers, { id: point.id, name: point.name, lat: point.latitude, lng: point.longitude, editable: false }]);
      },
      error: (err) => {
        this.saving.set(false);
        this.saveError.set(err.error?.message ?? 'Could not save point.');
      }
    });
  }
}
