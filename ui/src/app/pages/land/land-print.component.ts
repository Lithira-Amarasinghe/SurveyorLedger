import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { forkJoin } from 'rxjs';
import { Land, LandBoundary, LandDeed, LandPhoto, LandService, LandSurvey, addressLine, formatArea, telHref, whatsAppHref } from '../../core/land.service';
import { LandLocationQrComponent } from '../../shared/land-location-qr/land-location-qr.component';
import { PhotoGridComponent } from '../../shared/photo-grid/photo-grid.component';

/**
 * Standalone print view - no app shell, laid out for one page. window.print() with the
 * browser's native "Save as PDF" IS the export mechanism; no server-side PDF library.
 */
@Component({
  selector: 'app-land-print',
  standalone: true,
  imports: [CommonModule, LandLocationQrComponent, PhotoGridComponent],
  template: `
    @if (loading()) {
      <p class="p-lg text-sm text-neutral-500">Loading…</p>
    } @else if (land(); as land) {
      <div class="max-w-2xl mx-auto p-lg">
        <div class="flex justify-between items-start mb-lg print:hidden">
          <h1 class="text-lg font-semibold">Land Summary</h1>
          <button type="button" class="btn-primary" (click)="print()">Print / Save as PDF</button>
        </div>

        <h1 class="text-xl font-semibold text-neutral-900">{{ addressLine(land) }}</h1>
        @if (land.area.acres !== null || land.area.roods !== null || land.area.perches !== null) {
          <p class="text-sm text-neutral-600">{{ formatArea(land.area) }}</p>
        }

        @if (land.ownerName) {
          <div class="mt-md">
            <h2 class="text-xs font-semibold text-neutral-500 uppercase">Owner</h2>
            <p class="text-sm text-neutral-900">{{ land.ownerName }}</p>
            @if (land.ownerPhone) {
              <p class="text-sm">
                {{ land.ownerPhone }}
                <a [href]="telHref(land.ownerPhone)">Call</a> ·
                <a [href]="whatsAppHref(land.ownerPhone)">WhatsApp</a>
              </p>
            }
          </div>
        }

        @if (land.latitude !== null && land.longitude !== null) {
          <div class="mt-md">
            <h2 class="text-xs font-semibold text-neutral-500 uppercase mb-xs">Location</h2>
            <img
              [src]="'https://staticmap.openstreetmap.de/staticmap.php?center=' + land.latitude + ',' + land.longitude + '&zoom=16&size=600x300&markers=' + land.latitude + ',' + land.longitude + ',red-pushpin'"
              alt="Map of land location"
              class="w-full max-w-md rounded-md border border-neutral-200"
            />
            <app-land-location-qr [lat]="land.latitude" [lng]="land.longitude" [sizePx]="120" />
          </div>
        }

        @if (deeds().length > 0) {
          <div class="mt-md">
            <h2 class="text-xs font-semibold text-neutral-500 uppercase mb-xs">Deeds</h2>
            @for (d of deeds(); track d.id) {
              <p class="text-sm">{{ d.deedNumber }} — {{ d.issuedDate | date: 'mediumDate' }}</p>
            }
          </div>
        }

        @if (surveys().length > 0) {
          <div class="mt-md">
            <h2 class="text-xs font-semibold text-neutral-500 uppercase mb-xs">Surveys</h2>
            @for (s of surveys(); track s.id) {
              <p class="text-sm">{{ s.surveyPlanNumber }} — {{ s.surveyDate | date: 'mediumDate' }}</p>
            }
          </div>
        }

        @if (boundaries().length > 0) {
          <div class="mt-md">
            <h2 class="text-xs font-semibold text-neutral-500 uppercase mb-xs">Boundaries</h2>
            @for (b of boundaries(); track b.id) {
              <p class="text-sm">{{ b.label }}@if (b.description) { — {{ b.description }} }</p>
            }
          </div>
        }

        @if (photos().length > 0) {
          <div class="mt-md">
            <h2 class="text-xs font-semibold text-neutral-500 uppercase mb-xs">Photos</h2>
            <app-photo-grid [photos]="photos()" [photoUrls]="photoUrls()" [readonly]="true" />
          </div>
        }
      </div>
    }
  `
})
export class LandPrintComponent implements OnInit {
  loading = signal(true);
  land = signal<Land | null>(null);
  surveys = signal<LandSurvey[]>([]);
  deeds = signal<LandDeed[]>([]);
  boundaries = signal<LandBoundary[]>([]);
  photos = signal<LandPhoto[]>([]);
  photoUrls = signal<Record<string, string>>({});

  addressLine = addressLine;
  formatArea = formatArea;
  telHref = telHref;
  whatsAppHref = whatsAppHref;

  private workspaceId = '';
  private landId = '';

  constructor(private route: ActivatedRoute, private landService: LandService) {}

  ngOnInit(): void {
    this.workspaceId = this.route.snapshot.paramMap.get('id') ?? '';
    this.landId = this.route.snapshot.paramMap.get('landId') ?? '';

    forkJoin({
      land: this.landService.getById(this.workspaceId, this.landId),
      surveys: this.landService.getSurveys(this.workspaceId, this.landId),
      deeds: this.landService.getDeeds(this.workspaceId, this.landId),
      boundaries: this.landService.getBoundaries(this.workspaceId, this.landId),
      photos: this.landService.listPhotos(this.workspaceId, this.landId)
    }).subscribe(({ land, surveys, deeds, boundaries, photos }) => {
      this.land.set(land);
      this.surveys.set(surveys);
      this.deeds.set(deeds);
      this.boundaries.set(boundaries);
      this.photos.set(photos);
      photos.forEach(photo => {
        this.landService.getPhotoBlob(this.workspaceId, this.landId, photo.photoId).subscribe(blob => {
          this.photoUrls.update(urls => ({ ...urls, [photo.photoId]: URL.createObjectURL(blob) }));
        });
      });
      this.loading.set(false);
    });
  }

  print(): void {
    window.print();
  }
}
