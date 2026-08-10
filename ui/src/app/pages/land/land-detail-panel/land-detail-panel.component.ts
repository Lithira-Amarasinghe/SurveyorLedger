import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { Address, Land, LandBoundary, LandDeed, LandService, LandSurvey } from '../../../core/land.service';

@Component({
  selector: 'app-land-detail-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
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
          <div class="flex items-center justify-between mb-sm">
            <h3 class="text-xs font-semibold text-neutral-500 uppercase">Details</h3>
            @if (confirmingDelete()) {
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
            <input class="input-field" placeholder="Street" [(ngModel)]="street" (blur)="saveDetails()" />
            <input class="input-field" placeholder="City" [(ngModel)]="city" (blur)="saveDetails()" />
            <input class="input-field" placeholder="District" [(ngModel)]="district" (blur)="saveDetails()" />
            <input class="input-field" type="number" placeholder="Size" [(ngModel)]="size" (blur)="saveDetails()" />
            <input class="input-field" placeholder="Unit" [(ngModel)]="sizeUnit" (blur)="saveDetails()" />
            <input class="input-field" placeholder="GPS coordinates" [(ngModel)]="gpsCoordinates" (blur)="saveDetails()" />
          </div>
          <textarea class="input-field mt-sm" rows="2" placeholder="Notes" [(ngModel)]="notes" (blur)="saveDetails()"></textarea>
        </div>

        <div>
          <h3 class="text-xs font-semibold text-neutral-500 uppercase mb-sm">Surveys</h3>
          @if (surveys().length > 0) {
            <div class="space-y-xs mb-sm">
              @for (s of surveys(); track s.id) {
                <div class="px-md py-sm rounded bg-neutral-50 text-sm">
                  <span class="text-neutral-900">{{ s.surveyPlanNumber }}</span>
                  <span class="text-neutral-500"> · {{ s.surveyDate | date: 'mediumDate' }}</span>
                  @if (s.surveyedByName) {
                    <span class="text-neutral-500"> · {{ s.surveyedByName }}</span>
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
                <button type="button" class="btn-secondary" (click)="addingSurvey.set(false)">Cancel</button>
                <button type="button" class="btn-primary" [disabled]="!newSurveyPlanNumber.trim() || !newSurveyDate" (click)="submitSurvey()">
                  Add
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
                <div class="px-md py-sm rounded bg-neutral-50 text-sm">
                  <span class="text-neutral-900">{{ d.deedNumber }}</span>
                  <span class="text-neutral-500"> · {{ d.issuedDate | date: 'mediumDate' }}</span>
                  @if (d.isCurrent) {
                    <span class="text-xs px-sm py-xs rounded bg-green-100 text-green-700 ml-sm">Current</span>
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
                <button type="button" class="btn-secondary" (click)="addingDeed.set(false)">Cancel</button>
                <button type="button" class="btn-primary" [disabled]="!newDeedNumber.trim() || !newDeedIssuedDate" (click)="submitDeed()">
                  Add
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
                <div class="px-md py-sm rounded bg-neutral-50 text-sm">
                  <span class="text-neutral-900">{{ b.label }}</span>
                  @if (b.description) {
                    <span class="text-neutral-500"> · {{ b.description }}</span>
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
                <button type="button" class="btn-secondary" (click)="addingBoundary.set(false)">Cancel</button>
                <button type="button" class="btn-primary" [disabled]="!newBoundaryLabel.trim()" (click)="submitBoundary()">Add</button>
              </div>
            </div>
          } @else {
            <button type="button" class="text-sm text-primary-600" (click)="addingBoundary.set(true)">+ Add boundary</button>
          }
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
  surveys = signal<LandSurvey[]>([]);
  deeds = signal<LandDeed[]>([]);
  boundaries = signal<LandBoundary[]>([]);

  street = '';
  city = '';
  district = '';
  size: number | null = null;
  sizeUnit = '';
  gpsCoordinates = '';
  notes = '';

  addingSurvey = signal(false);
  newSurveyPlanNumber = '';
  newSurveyDate = '';
  newSurveyedByName = '';

  addingDeed = signal(false);
  newDeedNumber = '';
  newDeedIssuedDate = '';
  newDeedIsCurrent = true;

  addingBoundary = signal(false);
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

  saveDetails(): void {
    const current = this.land();
    if (!current) return;

    const address: Address = {
      street: this.street.trim() || null,
      city: this.city.trim() || null,
      district: this.district.trim() || null,
      postalCode: current.address.postalCode,
      country: current.address.country
    };

    this.landService
      .update(this.workspaceId, this.landId, {
        address,
        size: this.size ?? undefined,
        sizeUnit: this.sizeUnit.trim() || undefined,
        gpsCoordinates: this.gpsCoordinates.trim() || undefined,
        notes: this.notes.trim() || undefined
      })
      .subscribe({
        next: (land) => this.land.set(land),
        error: (err) => this.error.set(err.error?.message ?? 'Could not save changes.')
      });
  }

  submitSurvey(): void {
    if (!this.newSurveyPlanNumber.trim() || !this.newSurveyDate) return;
    this.landService
      .addSurvey(this.workspaceId, this.landId, {
        surveyPlanNumber: this.newSurveyPlanNumber.trim(),
        surveyDate: this.newSurveyDate,
        surveyedByName: this.newSurveyedByName.trim() || undefined
      })
      .subscribe({
        next: (survey) => {
          this.surveys.update(list => [survey, ...list]);
          this.addingSurvey.set(false);
          this.newSurveyPlanNumber = '';
          this.newSurveyDate = '';
          this.newSurveyedByName = '';
        },
        error: (err) => this.error.set(err.error?.message ?? 'Could not add survey.')
      });
  }

  submitDeed(): void {
    if (!this.newDeedNumber.trim() || !this.newDeedIssuedDate) return;
    this.landService
      .addDeed(this.workspaceId, this.landId, {
        deedNumber: this.newDeedNumber.trim(),
        issuedDate: this.newDeedIssuedDate,
        isCurrent: this.newDeedIsCurrent
      })
      .subscribe({
        next: (deed) => {
          // A new current deed supersedes the old one server-side - refetch the list
          // rather than patch it locally, so the previously-current deed's badge updates too.
          this.landService.getDeeds(this.workspaceId, this.landId).subscribe(deeds => this.deeds.set(deeds));
          this.addingDeed.set(false);
          this.newDeedNumber = '';
          this.newDeedIssuedDate = '';
          this.newDeedIsCurrent = true;
        },
        error: (err) => this.error.set(err.error?.message ?? 'Could not add deed.')
      });
  }

  submitBoundary(): void {
    if (!this.newBoundaryLabel.trim()) return;
    this.landService
      .addBoundary(this.workspaceId, this.landId, {
        label: this.newBoundaryLabel.trim(),
        description: this.newBoundaryDescription.trim() || undefined
      })
      .subscribe({
        next: (boundary) => {
          this.boundaries.update(list => [...list, boundary]);
          this.addingBoundary.set(false);
          this.newBoundaryLabel = '';
          this.newBoundaryDescription = '';
        },
        error: (err) => this.error.set(err.error?.message ?? 'Could not add boundary.')
      });
  }
}
