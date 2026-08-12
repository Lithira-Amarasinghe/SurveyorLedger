import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { LandLocationLinkPreview, LandLocationLinkService } from '../../core/land-location-link.service';
import { LandLocationPickerComponent } from '../../shared/land-location-picker/land-location-picker.component';

/**
 * Standalone public page, reached by people with no account - no app shell,
 * no auth guard. Mirrors PublicDocumentUploadComponent's structure.
 */
@Component({
  selector: 'app-public-set-location',
  standalone: true,
  imports: [CommonModule, LandLocationPickerComponent],
  template: `
    <div class="min-h-screen flex items-center justify-center bg-neutral-50 p-lg">
      <div class="max-w-lg w-full bg-white rounded-md shadow p-lg">
        @if (loading()) {
          <p class="text-sm text-neutral-500">Loading…</p>
        } @else if (error()) {
          <p class="text-sm text-neutral-600">{{ error() }}</p>
        } @else if (saved()) {
          <p class="text-sm text-neutral-900">Location saved — you can close this page.</p>
          <button type="button" class="text-sm text-primary-600 mt-sm" (click)="saved.set(false)">
            Adjust the pin
          </button>
        } @else {
          <h1 class="text-lg font-semibold text-neutral-900 mb-xs">Set land location</h1>
          <p class="text-sm text-neutral-500 mb-md">{{ preview()!.addressLine }}</p>
          <app-land-location-picker
            [initialLat]="preview()!.latitude"
            [initialLng]="preview()!.longitude"
            (locationChosen)="onLocationChosen($event)"
          />
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
  saved = signal(false);
  saveError = signal('');

  private token = '';

  constructor(private route: ActivatedRoute, private linkService: LandLocationLinkService) {}

  ngOnInit(): void {
    this.token = this.route.snapshot.paramMap.get('token') ?? '';
    this.linkService.getPreview(this.token).subscribe({
      next: (preview) => {
        this.preview.set(preview);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('This link is no longer valid.');
        this.loading.set(false);
      }
    });
  }

  onLocationChosen(location: { lat: number; lng: number }): void {
    this.saveError.set('');
    this.linkService.setLocation(this.token, location).subscribe({
      next: () => {
        this.saved.set(true);
        this.preview.update(p => (p ? { ...p, latitude: location.lat, longitude: location.lng } : p));
      },
      error: (err) => this.saveError.set(err.error?.message ?? 'Could not save location.')
    });
  }
}
