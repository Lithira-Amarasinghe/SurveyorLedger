import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { Address, Land, LandBoundary, LandDeed, LandService, LandSurvey } from '../../../core/land.service';
import { OwnerPickerComponent, OwnerValue } from '../owner-picker/owner-picker.component';
import { LandLocationPickerComponent } from '../../../shared/land-location-picker/land-location-picker.component';

@Component({
  selector: 'app-land-detail-panel',
  standalone: true,
  imports: [CommonModule, FormsModule, OwnerPickerComponent, LandLocationPickerComponent],
  template: `
    @if (loading()) {
      <p class="text-sm text-neutral-500">Loading…</p>
    } @else if (error()) {
      <div class="text-sm text-primary-500">
        {{ error() }}
        <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
      </div>
    } @else {
      <div class="space-y-lg">
        <div>
          <div class="flex items-center justify-between mb-sm gap-sm">
            <div class="flex items-center gap-sm">
              <h3 class="text-xs font-semibold text-neutral-500 uppercase">Details</h3>
              @if (detailsDirty()) {
                <span class="text-xs text-amber-600">Unsaved changes</span>
              }
            </div>
            @if (detailsDirty()) {
              <span class="flex items-center gap-sm">
                @if (detailsError()) {
                  <span class="text-xs text-primary-500">{{ detailsError() }}</span>
                }
                <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" [disabled]="savingDetails()" (click)="discardDetails()">
                  Discard
                </button>
                <button type="button" class="text-xs text-primary-500 hover:text-primary-600 font-medium" [disabled]="savingDetails()" (click)="saveDetails()">
                  {{ savingDetails() ? 'Saving…' : 'Save changes' }}
                </button>
              </span>
            } @else if (confirmingDelete()) {
              <span class="text-xs text-neutral-600">
                Delete this land record?
                <button type="button" class="text-primary-500 font-medium ml-xs" [disabled]="deleting()" (click)="confirmDelete()">
                  {{ deleting() ? 'Deleting…' : 'Yes' }}
                </button>
                <button type="button" class="text-neutral-500 ml-xs" (click)="confirmingDelete.set(false)">No</button>
              </span>
            } @else {
              <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="confirmingDelete.set(true)">
                Delete
              </button>
            }
          </div>
          <div class="grid grid-cols-2 gap-sm">
            <input class="input-field" placeholder="Street" [(ngModel)]="street" />
            <input class="input-field" placeholder="City" [(ngModel)]="city" />
            <input class="input-field" placeholder="District" [(ngModel)]="district" />
            <input class="input-field" type="number" placeholder="Size" [(ngModel)]="size" />
            <input class="input-field" placeholder="Unit" [(ngModel)]="sizeUnit" />
            <input class="input-field" placeholder="GPS coordinates" [(ngModel)]="gpsCoordinates" />
          </div>
          <textarea class="input-field mt-sm" rows="2" placeholder="Notes" [(ngModel)]="notes"></textarea>

          <div class="mt-sm">
            <app-owner-picker
              [value]="owner"
              [initialAccountLabel]="ownerLabel"
              (valueChange)="onOwnerChange($event)"
            />
          </div>
        </div>

        <div>
          <h3 class="text-xs font-semibold text-neutral-500 uppercase mb-sm">Location</h3>
          @if (land()?.latitude !== null && land()?.latitude !== undefined) {
            <p class="text-sm text-neutral-900 mb-sm">{{ land()!.latitude }}, {{ land()!.longitude }}</p>
            <app-land-location-picker
              [initialLat]="land()!.latitude"
              [initialLng]="land()!.longitude"
              [readonly]="true"
              heightClass="h-48"
            />
          } @else {
            <p class="text-sm text-neutral-500">Not set</p>
          }
          <div class="flex flex-wrap gap-sm mt-sm">
            <button type="button" class="text-xs text-primary-600 hover:text-primary-700" (click)="pickerOpen.set(true)">
              {{ land()?.latitude != null ? 'Update location' : 'Set location' }}
            </button>
            @if (land()?.latitude !== null && land()?.latitude !== undefined) {
              <a
                class="text-xs text-primary-600 hover:text-primary-700"
                [href]="'https://www.google.com/maps?q=' + land()!.latitude + ',' + land()!.longitude"
                target="_blank"
                rel="noopener"
              >
                Open in Google Maps
              </a>
              <button type="button" class="text-xs text-primary-600 hover:text-primary-700" (click)="copyMapsLink()">
                {{ mapsLinkCopied() ? 'Copied!' : 'Copy Google Maps link' }}
              </button>
            }
            @if (land()?.hasActiveLocationShareLink) {
              <button type="button" class="text-xs text-primary-600 hover:text-primary-700" (click)="copyShareLink()">
                {{ shareLinkCopied() ? 'Copied!' : 'Copy share link' }}
              </button>
              <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" (click)="regenerateShareLink()">
                Regenerate link
              </button>
              <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" (click)="revokeShareLink()">
                Revoke link
              </button>
            } @else {
              <button type="button" class="text-xs text-primary-600 hover:text-primary-700" (click)="copyShareLink()">
                Copy share link (for client)
              </button>
            }
          </div>
          @if (locationError()) {
            <p class="text-xs text-primary-500 mt-xs">{{ locationError() }}</p>
          }
        </div>

        <div>
          <h3 class="text-xs font-semibold text-neutral-500 uppercase mb-sm">Surveys</h3>
          @if (surveys().length > 0) {
            <div class="space-y-xs mb-sm">
              @for (s of surveys(); track s.id) {
                <div class="px-md py-sm rounded bg-neutral-50 text-sm flex items-center justify-between">
                  <div>
                    <span class="text-neutral-900">{{ s.surveyPlanNumber }}</span>
                    <span class="text-neutral-500"> · {{ s.surveyDate | date: 'mediumDate' }}</span>
                    @if (s.surveyedByName) {
                      <span class="text-neutral-500"> · {{ s.surveyedByName }}</span>
                    }
                  </div>
                  @if (confirmingDeleteSurveyId() === s.id) {
                    <span class="text-xs text-neutral-600 whitespace-nowrap">
                      Delete?
                      <button type="button" class="text-primary-500 font-medium ml-xs" (click)="deleteSurvey(s.id)">Yes</button>
                      <button type="button" class="text-neutral-500 ml-xs" (click)="confirmingDeleteSurveyId.set(null)">No</button>
                    </span>
                  } @else {
                    <span class="whitespace-nowrap">
                      <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" (click)="startEditSurvey(s)">Edit</button>
                      <button type="button" class="text-xs text-primary-500 hover:text-primary-600 ml-sm" (click)="confirmingDeleteSurveyId.set(s.id)">
                        Delete
                      </button>
                    </span>
                  }
                </div>
              }
            </div>
          }
          @if (addingSurvey()) {
            <div class="border border-neutral-200 rounded-md p-md space-y-sm">
              <input class="input-field" placeholder="Survey plan number" [(ngModel)]="newSurveyPlanNumber" />
              <input class="input-field" type="date" [(ngModel)]="newSurveyDate" />
              <input class="input-field" placeholder="Surveyed by (optional)" [(ngModel)]="newSurveyedByName" />
              <div class="flex justify-end gap-sm">
                <button type="button" class="btn-secondary" (click)="cancelSurveyForm()">Cancel</button>
                <button type="button" class="btn-primary" [disabled]="!newSurveyPlanNumber.trim() || !newSurveyDate" (click)="submitSurvey()">
                  {{ editingSurveyId() ? 'Save' : 'Add' }}
                </button>
              </div>
            </div>
          } @else {
            <button type="button" class="text-sm text-primary-600" (click)="addingSurvey.set(true)">+ Add survey</button>
          }
        </div>

        <div>
          <h3 class="text-xs font-semibold text-neutral-500 uppercase mb-sm">Deeds</h3>
          @if (deeds().length > 0) {
            <div class="space-y-xs mb-sm">
              @for (d of deeds(); track d.id) {
                <div class="px-md py-sm rounded bg-neutral-50 text-sm flex items-center justify-between">
                  <div>
                    <span class="text-neutral-900">{{ d.deedNumber }}</span>
                    <span class="text-neutral-500"> · {{ d.issuedDate | date: 'mediumDate' }}</span>
                    @if (d.isCurrent) {
                      <span class="text-xs px-sm py-xs rounded bg-green-100 text-green-700 ml-sm">Current</span>
                    }
                  </div>
                  @if (confirmingDeleteDeedId() === d.id) {
                    <span class="text-xs text-neutral-600 whitespace-nowrap">
                      Delete?
                      <button type="button" class="text-primary-500 font-medium ml-xs" (click)="deleteDeed(d.id)">Yes</button>
                      <button type="button" class="text-neutral-500 ml-xs" (click)="confirmingDeleteDeedId.set(null)">No</button>
                    </span>
                  } @else {
                    <span class="whitespace-nowrap">
                      <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" (click)="startEditDeed(d)">Edit</button>
                      <button type="button" class="text-xs text-primary-500 hover:text-primary-600 ml-sm" (click)="confirmingDeleteDeedId.set(d.id)">
                        Delete
                      </button>
                    </span>
                  }
                </div>
              }
            </div>
          }
          @if (addingDeed()) {
            <div class="border border-neutral-200 rounded-md p-md space-y-sm">
              <input class="input-field" placeholder="Deed number" [(ngModel)]="newDeedNumber" />
              <input class="input-field" type="date" [(ngModel)]="newDeedIssuedDate" />
              <label class="flex items-center gap-sm text-sm text-neutral-700">
                <input type="checkbox" [(ngModel)]="newDeedIsCurrent" />
                This is the current deed
              </label>
              <div class="flex justify-end gap-sm">
                <button type="button" class="btn-secondary" (click)="cancelDeedForm()">Cancel</button>
                <button type="button" class="btn-primary" [disabled]="!newDeedNumber.trim() || !newDeedIssuedDate" (click)="submitDeed()">
                  {{ editingDeedId() ? 'Save' : 'Add' }}
                </button>
              </div>
            </div>
          } @else {
            <button type="button" class="text-sm text-primary-600" (click)="addingDeed.set(true)">+ Add deed</button>
          }
        </div>

        <div>
          <h3 class="text-xs font-semibold text-neutral-500 uppercase mb-sm">Boundaries</h3>
          @if (boundaries().length > 0) {
            <div class="space-y-xs mb-sm">
              @for (b of boundaries(); track b.id) {
                <div class="px-md py-sm rounded bg-neutral-50 text-sm flex items-center justify-between">
                  <div>
                    <span class="text-neutral-900">{{ b.label }}</span>
                    @if (b.description) {
                      <span class="text-neutral-500"> · {{ b.description }}</span>
                    }
                  </div>
                  @if (confirmingDeleteBoundaryId() === b.id) {
                    <span class="text-xs text-neutral-600 whitespace-nowrap">
                      Delete?
                      <button type="button" class="text-primary-500 font-medium ml-xs" (click)="deleteBoundary(b.id)">Yes</button>
                      <button type="button" class="text-neutral-500 ml-xs" (click)="confirmingDeleteBoundaryId.set(null)">No</button>
                    </span>
                  } @else {
                    <span class="whitespace-nowrap">
                      <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" (click)="startEditBoundary(b)">Edit</button>
                      <button type="button" class="text-xs text-primary-500 hover:text-primary-600 ml-sm" (click)="confirmingDeleteBoundaryId.set(b.id)">
                        Delete
                      </button>
                    </span>
                  }
                </div>
              }
            </div>
          }
          @if (addingBoundary()) {
            <div class="border border-neutral-200 rounded-md p-md space-y-sm">
              <input class="input-field" placeholder="Label (e.g. North, River side)" [(ngModel)]="newBoundaryLabel" />
              <input class="input-field" placeholder="Description (optional)" [(ngModel)]="newBoundaryDescription" />
              <div class="flex justify-end gap-sm">
                <button type="button" class="btn-secondary" (click)="cancelBoundaryForm()">Cancel</button>
                <button type="button" class="btn-primary" [disabled]="!newBoundaryLabel.trim()" (click)="submitBoundary()">
                  {{ editingBoundaryId() ? 'Save' : 'Add' }}
                </button>
              </div>
            </div>
          } @else {
            <button type="button" class="text-sm text-primary-600" (click)="addingBoundary.set(true)">+ Add boundary</button>
          }
        </div>
      </div>
    }
    @if (pickerOpen()) {
      <div class="fixed inset-0 bg-black/40 flex items-center justify-center z-50 p-lg" (click)="pickerOpen.set(false)">
        <div class="bg-white rounded-md p-lg max-w-lg w-full" (click)="$event.stopPropagation()">
          <h3 class="text-sm font-semibold text-neutral-900 mb-md">Set land location</h3>
          <app-land-location-picker
            [initialLat]="land()?.latitude ?? null"
            [initialLng]="land()?.longitude ?? null"
            (locationChosen)="onLocationChosen($event)"
          />
        </div>
      </div>
    }
  `
})
export class LandDetailPanelComponent implements OnInit {
  @Input() workspaceId = '';
  @Input() landId = '';
  @Output() deleted = new EventEmitter<void>();

  loading = signal(true);
  error = signal('');
  land = signal<Land | null>(null);
  confirmingDelete = signal(false);
  deleting = signal(false);
  savingDetails = signal(false);
  detailsError = signal('');
  surveys = signal<LandSurvey[]>([]);
  deeds = signal<LandDeed[]>([]);
  boundaries = signal<LandBoundary[]>([]);
  pickerOpen = signal(false);
  locationError = signal('');
  mapsLinkCopied = signal(false);
  shareLinkCopied = signal(false);

  owner: OwnerValue = {};
  /** Display name for an existing account owner, so the picker can render it without a re-fetch. */
  ownerLabel: string | null = null;
  street = '';
  city = '';
  district = '';
  size: number | null = null;
  sizeUnit = '';
  gpsCoordinates = '';
  notes = '';

  addingSurvey = signal(false);
  editingSurveyId = signal<string | null>(null);
  confirmingDeleteSurveyId = signal<string | null>(null);
  newSurveyPlanNumber = '';
  newSurveyDate = '';
  newSurveyedByName = '';

  addingDeed = signal(false);
  editingDeedId = signal<string | null>(null);
  confirmingDeleteDeedId = signal<string | null>(null);
  newDeedNumber = '';
  newDeedIssuedDate = '';
  newDeedIsCurrent = true;

  addingBoundary = signal(false);
  editingBoundaryId = signal<string | null>(null);
  confirmingDeleteBoundaryId = signal<string | null>(null);
  newBoundaryLabel = '';
  newBoundaryDescription = '';

  constructor(private landService: LandService) {}

  ngOnInit(): void {
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    forkJoin({
      land: this.landService.getById(this.workspaceId, this.landId),
      surveys: this.landService.getSurveys(this.workspaceId, this.landId),
      deeds: this.landService.getDeeds(this.workspaceId, this.landId),
      boundaries: this.landService.getBoundaries(this.workspaceId, this.landId)
    }).subscribe({
      next: ({ land, surveys, deeds, boundaries }) => {
        this.land.set(land);
        this.street = land.address.street ?? '';
        this.city = land.address.city ?? '';
        this.district = land.address.district ?? '';
        this.size = land.size;
        this.sizeUnit = land.sizeUnit ?? '';
        this.gpsCoordinates = land.gpsCoordinates ?? '';
        this.notes = land.notes ?? '';
        this.owner = land.ownerId
          ? { ownerId: land.ownerId, ownerEmail: land.ownerEmail ?? undefined }
          : land.ownerName
            ? { ownerName: land.ownerName, ownerPhone: land.ownerPhone ?? undefined, ownerEmail: land.ownerEmail ?? undefined }
            : {};
        this.ownerLabel = land.ownerId ? land.ownerName : null;
        this.storedDetails = this.snapshotDetails();
        this.surveys.set(surveys);
        this.deeds.set(deeds);
        this.boundaries.set(boundaries);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err.error?.message ?? 'Could not load land record.');
        this.loading.set(false);
      }
    });
  }

  confirmDelete(): void {
    this.deleting.set(true);
    this.landService.delete(this.workspaceId, this.landId).subscribe({
      next: () => this.deleted.emit(),
      error: (err) => {
        this.deleting.set(false);
        this.confirmingDelete.set(false);
        this.error.set(err.error?.message ?? 'Could not delete land record.');
      }
    });
  }

  /** Snapshot of Details as last loaded/saved, so edits (including the owner) can be detected and discarded. */
  private storedDetails = '';

  private snapshotDetails(): string {
    return JSON.stringify({
      street: this.street, city: this.city, district: this.district,
      size: this.size, sizeUnit: this.sizeUnit, gpsCoordinates: this.gpsCoordinates,
      notes: this.notes, owner: this.owner
    });
  }

  detailsDirty(): boolean {
    return this.snapshotDetails() !== this.storedDetails;
  }

  /** The picker has no blur of its own - it just marks Details dirty like every other field. */
  onOwnerChange(value: OwnerValue): void {
    this.owner = value;
  }

  discardDetails(): void {
    const current = this.land();
    if (!current) return;
    this.street = current.address.street ?? '';
    this.city = current.address.city ?? '';
    this.district = current.address.district ?? '';
    this.size = current.size;
    this.sizeUnit = current.sizeUnit ?? '';
    this.gpsCoordinates = current.gpsCoordinates ?? '';
    this.notes = current.notes ?? '';
    this.owner = current.ownerId
      ? { ownerId: current.ownerId, ownerEmail: current.ownerEmail ?? undefined }
      : current.ownerName
        ? { ownerName: current.ownerName, ownerPhone: current.ownerPhone ?? undefined, ownerEmail: current.ownerEmail ?? undefined }
        : {};
    this.ownerLabel = current.ownerId ? current.ownerName : null;
    this.detailsError.set('');
  }

  saveDetails(onSaved?: () => void): void {
    const current = this.land();
    if (!current || !this.detailsDirty()) return;

    this.detailsError.set('');

    const address: Address = {
      street: this.street.trim() || null,
      city: this.city.trim() || null,
      district: this.district.trim() || null,
      postalCode: current.address.postalCode,
      country: current.address.country
    };

    this.savingDetails.set(true);
    this.landService
      .update(this.workspaceId, this.landId, {
        address,
        size: this.size ?? undefined,
        sizeUnit: this.sizeUnit.trim() || undefined,
        gpsCoordinates: this.gpsCoordinates.trim() || undefined,
        notes: this.notes.trim() || undefined,
        ...this.owner
      })
      .subscribe({
        next: (land) => {
          this.land.set(land);
          this.ownerLabel = land.ownerId ? land.ownerName : null;
          this.storedDetails = this.snapshotDetails();
          this.savingDetails.set(false);
          onSaved?.();
        },
        error: (err) => {
          this.savingDetails.set(false);
          this.detailsError.set(err.error?.message ?? 'Could not save changes.');
        }
      });
  }

  onLocationChosen(location: { lat: number; lng: number }): void {
    this.locationError.set('');
    this.landService.setLocation(this.workspaceId, this.landId, location).subscribe({
      next: (land) => {
        this.land.set(land);
        this.pickerOpen.set(false);
      },
      error: (err) => this.locationError.set(err.error?.message ?? 'Could not save location.')
    });
  }

  copyMapsLink(): void {
    const land = this.land();
    if (!land?.latitude || !land?.longitude) return;
    navigator.clipboard.writeText(`https://www.google.com/maps?q=${land.latitude},${land.longitude}`);
    this.mapsLinkCopied.set(true);
    setTimeout(() => this.mapsLinkCopied.set(false), 2000);
  }

  copyShareLink(): void {
    this.locationError.set('');
    this.landService.generateLocationShareLink(this.workspaceId, this.landId).subscribe({
      next: (token) => {
        navigator.clipboard.writeText(`${location.origin}/set-location/${token}`);
        this.shareLinkCopied.set(true);
        setTimeout(() => this.shareLinkCopied.set(false), 2000);
        this.land.update(l => (l ? { ...l, hasActiveLocationShareLink: true } : l));
      },
      error: (err) => this.locationError.set(err.error?.message ?? 'Could not create share link.')
    });
  }

  regenerateShareLink(): void {
    this.locationError.set('');
    this.landService.regenerateLocationShareLink(this.workspaceId, this.landId).subscribe({
      next: (token) => {
        navigator.clipboard.writeText(`${location.origin}/set-location/${token}`);
        this.shareLinkCopied.set(true);
        setTimeout(() => this.shareLinkCopied.set(false), 2000);
      },
      error: (err) => this.locationError.set(err.error?.message ?? 'Could not regenerate share link.')
    });
  }

  revokeShareLink(): void {
    this.locationError.set('');
    this.landService.revokeLocationShareLink(this.workspaceId, this.landId).subscribe({
      next: () => this.land.update(l => (l ? { ...l, hasActiveLocationShareLink: false } : l)),
      error: (err) => this.locationError.set(err.error?.message ?? 'Could not revoke share link.')
    });
  }

  startEditSurvey(s: LandSurvey): void {
    this.editingSurveyId.set(s.id);
    this.newSurveyPlanNumber = s.surveyPlanNumber;
    this.newSurveyDate = s.surveyDate.slice(0, 10);
    this.newSurveyedByName = s.surveyedByName ?? '';
    this.addingSurvey.set(true);
  }

  cancelSurveyForm(): void {
    this.addingSurvey.set(false);
    this.editingSurveyId.set(null);
    this.newSurveyPlanNumber = '';
    this.newSurveyDate = '';
    this.newSurveyedByName = '';
  }

  submitSurvey(): void {
    if (!this.newSurveyPlanNumber.trim() || !this.newSurveyDate) return;
    const request = {
      surveyPlanNumber: this.newSurveyPlanNumber.trim(),
      surveyDate: this.newSurveyDate,
      surveyedByName: this.newSurveyedByName.trim() || undefined
    };
    const editingId = this.editingSurveyId();

    const result$ = editingId
      ? this.landService.updateSurvey(this.workspaceId, this.landId, editingId, request)
      : this.landService.addSurvey(this.workspaceId, this.landId, request);

    result$.subscribe({
      next: (survey) => {
        this.surveys.update(list => (editingId ? list.map(s => (s.id === editingId ? survey : s)) : [survey, ...list]));
        this.cancelSurveyForm();
      },
      error: (err) => this.error.set(err.error?.message ?? 'Could not save survey.')
    });
  }

  deleteSurvey(surveyId: string): void {
    this.landService.deleteSurvey(this.workspaceId, this.landId, surveyId).subscribe({
      next: () => {
        this.surveys.update(list => list.filter(s => s.id !== surveyId));
        this.confirmingDeleteSurveyId.set(null);
      },
      error: (err) => this.error.set(err.error?.message ?? 'Could not delete survey.')
    });
  }

  startEditDeed(d: LandDeed): void {
    this.editingDeedId.set(d.id);
    this.newDeedNumber = d.deedNumber;
    this.newDeedIssuedDate = d.issuedDate.slice(0, 10);
    this.newDeedIsCurrent = d.isCurrent;
    this.addingDeed.set(true);
  }

  cancelDeedForm(): void {
    this.addingDeed.set(false);
    this.editingDeedId.set(null);
    this.newDeedNumber = '';
    this.newDeedIssuedDate = '';
    this.newDeedIsCurrent = true;
  }

  submitDeed(): void {
    if (!this.newDeedNumber.trim() || !this.newDeedIssuedDate) return;
    const request = { deedNumber: this.newDeedNumber.trim(), issuedDate: this.newDeedIssuedDate, isCurrent: this.newDeedIsCurrent };
    const editingId = this.editingDeedId();

    const result$ = editingId
      ? this.landService.updateDeed(this.workspaceId, this.landId, editingId, request)
      : this.landService.addDeed(this.workspaceId, this.landId, request);

    result$.subscribe({
      next: () => {
        // A current deed supersedes any other current deed server-side - refetch the
        // list rather than patch it locally, so every affected badge updates too.
        this.landService.getDeeds(this.workspaceId, this.landId).subscribe(deeds => this.deeds.set(deeds));
        this.cancelDeedForm();
      },
      error: (err) => this.error.set(err.error?.message ?? 'Could not save deed.')
    });
  }

  deleteDeed(deedId: string): void {
    this.landService.deleteDeed(this.workspaceId, this.landId, deedId).subscribe({
      next: () => {
        this.deeds.update(list => list.filter(d => d.id !== deedId));
        this.confirmingDeleteDeedId.set(null);
      },
      error: (err) => this.error.set(err.error?.message ?? 'Could not delete deed.')
    });
  }

  startEditBoundary(b: LandBoundary): void {
    this.editingBoundaryId.set(b.id);
    this.newBoundaryLabel = b.label;
    this.newBoundaryDescription = b.description ?? '';
    this.addingBoundary.set(true);
  }

  cancelBoundaryForm(): void {
    this.addingBoundary.set(false);
    this.editingBoundaryId.set(null);
    this.newBoundaryLabel = '';
    this.newBoundaryDescription = '';
  }

  submitBoundary(): void {
    if (!this.newBoundaryLabel.trim()) return;
    const request = { label: this.newBoundaryLabel.trim(), description: this.newBoundaryDescription.trim() || undefined };
    const editingId = this.editingBoundaryId();

    const result$ = editingId
      ? this.landService.updateBoundary(this.workspaceId, this.landId, editingId, request)
      : this.landService.addBoundary(this.workspaceId, this.landId, request);

    result$.subscribe({
      next: (boundary) => {
        this.boundaries.update(list => (editingId ? list.map(b => (b.id === editingId ? boundary : b)) : [...list, boundary]));
        this.cancelBoundaryForm();
      },
      error: (err) => this.error.set(err.error?.message ?? 'Could not save boundary.')
    });
  }

  deleteBoundary(boundaryId: string): void {
    this.landService.deleteBoundary(this.workspaceId, this.landId, boundaryId).subscribe({
      next: () => {
        this.boundaries.update(list => list.filter(b => b.id !== boundaryId));
        this.confirmingDeleteBoundaryId.set(null);
      },
      error: (err) => this.error.set(err.error?.message ?? 'Could not delete boundary.')
    });
  }
}
