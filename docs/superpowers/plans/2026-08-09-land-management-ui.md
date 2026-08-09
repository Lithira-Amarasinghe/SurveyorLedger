# Land Management UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Full land management (details + Survey/Deed/Boundary history) reachable both inline inside Job detail and as a dedicated Land list/detail screen, via one shared `LandDetailPanelComponent`.

**Architecture:** Same standalone-component + signals pattern as the Job feature UI (`docs/superpowers/plans/2026-08-09-job-feature-ui.md`). `LandDetailPanelComponent` is the single implementation of "manage a Land," mounted both inline (Job detail) and full-page (dedicated Land detail route) — no duplicated logic between the two call sites.

**Tech Stack:** Angular 21 standalone components, RxJS, existing Tailwind utility classes (`btn-primary`, `btn-secondary`, `card`, `input-field`), Vitest via `ng test` (not raw `npx vitest` — the Angular Vitest builder compiles the whole app per run, confirmed during Phase 1).

## Global Constraints

- Follow `docs/superpowers/specs/2026-08-09-land-ui-design.md` exactly.
- `LandDetailPanelComponent` is self-contained — fetches its own data from `[landId]`/`[workspaceId]` inputs, no data passed down from parents. This is what makes it reusable in two places without the two call sites diverging.
- Only one land panel expanded at a time inside Job detail (`expandedLandId` signal holds at most one id).
- New deed `IsCurrent=true` supersedes the prior current deed automatically server-side — the UI never toggles other deeds' `IsCurrent` itself.
- Reuse existing Tailwind classes — no new CSS.
- No `any` casts, no reaching into child component internals from a parent (same rule as Phase 1).

---

## File Structure

**New files:**
- `ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts` — the shared panel
- `ui/src/app/pages/land/land-list.component.ts` — dedicated list page
- `ui/src/app/pages/land/land-detail.component.ts` — thin page wrapper around the panel
- `ui/src/app/pages/land/create-land-modal/create-land-modal.component.ts`

**Modified files:**
- `ui/src/app/core/land.service.ts` — add `getById`, `update`, survey/deed/boundary CRUD methods + types
- `ui/src/app/core/land.service.spec.ts` — tests for the above
- `ui/src/app/pages/job/job-detail.component.ts` — land rows become expandable, mount `LandDetailPanelComponent` inline
- `ui/src/app/app.routes.ts` — add `lands` and `lands/:landId` routes
- `ui/src/app/shell/sidebar.component.ts` — add "Land" nav link

---

### Task 1: LandService additions

**Files:**
- Modify: `ui/src/app/core/land.service.ts`
- Modify: `ui/src/app/core/land.service.spec.ts`

**Interfaces:**
- Consumes: nothing new.
- Produces: `LandSurvey`, `LandDeed`, `LandBoundary` interfaces, and on `LandService`: `getById`, `update`, `getSurveys`, `addSurvey`, `getDeeds`, `addDeed`, `getBoundaries`, `addBoundary`.

- [ ] **Step 1: Add the new tests to `land.service.spec.ts`**

Append this to the existing `describe('LandService', ...)` block (after the existing `create()` test, before the closing `});`):

```typescript
  it('getById() gets a single land', () => {
    const land = { landId: 'l1', address: { street: 'Main St', city: null, district: null, postalCode: null, country: null }, size: null, sizeUnit: null, gpsCoordinates: null, notes: null, createdAt: '2026-01-01', updatedAt: '2026-01-01' };
    service.getById(workspaceId, 'l1').subscribe(result => expect(result).toEqual(land));
    const req = httpMock.expectOne(`${base}/l1`);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: land });
  });

  it('update() puts the land request', () => {
    const request = { address: { street: 'New St', city: null, district: null, postalCode: null, country: null } };
    service.update(workspaceId, 'l1', request).subscribe();
    const req = httpMock.expectOne(`${base}/l1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(request);
    req.flush({ success: true, data: {} });
  });

  it('getSurveys() gets the /surveys sub-route', () => {
    const surveys = [{ id: 's1', landId: 'l1', surveyPlanNumber: 'SP-1', surveyDate: '2020-01-01', surveyedByName: null, notes: null, createdAt: '2026-01-01' }];
    service.getSurveys(workspaceId, 'l1').subscribe(result => expect(result).toEqual(surveys));
    const req = httpMock.expectOne(`${base}/l1/surveys`);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: surveys });
  });

  it('addSurvey() posts to /surveys', () => {
    const request = { surveyPlanNumber: 'SP-2', surveyDate: '2026-01-01' };
    service.addSurvey(workspaceId, 'l1', request).subscribe();
    const req = httpMock.expectOne(`${base}/l1/surveys`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush({ success: true, data: {} });
  });

  it('getDeeds() gets the /deeds sub-route', () => {
    const deeds = [{ id: 'd1', landId: 'l1', deedNumber: 'DN-1', issuedDate: '2020-01-01', isCurrent: true, notes: null, createdAt: '2026-01-01' }];
    service.getDeeds(workspaceId, 'l1').subscribe(result => expect(result).toEqual(deeds));
    const req = httpMock.expectOne(`${base}/l1/deeds`);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: deeds });
  });

  it('addDeed() posts to /deeds', () => {
    const request = { deedNumber: 'DN-2', issuedDate: '2026-01-01', isCurrent: true };
    service.addDeed(workspaceId, 'l1', request).subscribe();
    const req = httpMock.expectOne(`${base}/l1/deeds`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush({ success: true, data: {} });
  });

  it('getBoundaries() gets the /boundaries sub-route', () => {
    const boundaries = [{ id: 'b1', landId: 'l1', label: 'North', description: null, createdAt: '2026-01-01' }];
    service.getBoundaries(workspaceId, 'l1').subscribe(result => expect(result).toEqual(boundaries));
    const req = httpMock.expectOne(`${base}/l1/boundaries`);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: boundaries });
  });

  it('addBoundary() posts to /boundaries', () => {
    const request = { label: 'River side', description: 'Runs along the river' };
    service.addBoundary(workspaceId, 'l1', request).subscribe();
    const req = httpMock.expectOne(`${base}/l1/boundaries`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush({ success: true, data: {} });
  });
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ui && npx ng test --include='src/app/core/land.service.spec.ts'`
Expected: FAIL — `getById`/`update`/`getSurveys`/etc. don't exist on `LandService` yet (compile error).

- [ ] **Step 3: Add the implementation to `land.service.ts`**

Add these interfaces after the existing `LandRequest` interface:

```typescript
export interface LandSurvey {
  id: string;
  landId: string;
  surveyPlanNumber: string;
  surveyDate: string;
  surveyedByName: string | null;
  notes: string | null;
  createdAt: string;
}

export interface LandSurveyRequest {
  surveyPlanNumber: string;
  surveyDate: string;
  surveyedByName?: string;
  notes?: string;
}

export interface LandDeed {
  id: string;
  landId: string;
  deedNumber: string;
  issuedDate: string;
  isCurrent: boolean;
  notes: string | null;
  createdAt: string;
}

export interface LandDeedRequest {
  deedNumber: string;
  issuedDate: string;
  isCurrent: boolean;
  notes?: string;
}

export interface LandBoundary {
  id: string;
  landId: string;
  label: string;
  description: string | null;
  createdAt: string;
}

export interface LandBoundaryRequest {
  label: string;
  description?: string;
}
```

Add these methods inside the `LandService` class, after `create`:

```typescript
  getById(workspaceId: string, landId: string): Observable<Land> {
    return this.http.get<ApiResponse<Land>>(`${this.base(workspaceId)}/${landId}`).pipe(map(res => res.data));
  }

  update(workspaceId: string, landId: string, request: LandRequest): Observable<Land> {
    return this.http.put<ApiResponse<Land>>(`${this.base(workspaceId)}/${landId}`, request).pipe(map(res => res.data));
  }

  getSurveys(workspaceId: string, landId: string): Observable<LandSurvey[]> {
    return this.http.get<ApiResponse<LandSurvey[]>>(`${this.base(workspaceId)}/${landId}/surveys`).pipe(map(res => res.data));
  }

  addSurvey(workspaceId: string, landId: string, request: LandSurveyRequest): Observable<LandSurvey> {
    return this.http
      .post<ApiResponse<LandSurvey>>(`${this.base(workspaceId)}/${landId}/surveys`, request)
      .pipe(map(res => res.data));
  }

  getDeeds(workspaceId: string, landId: string): Observable<LandDeed[]> {
    return this.http.get<ApiResponse<LandDeed[]>>(`${this.base(workspaceId)}/${landId}/deeds`).pipe(map(res => res.data));
  }

  addDeed(workspaceId: string, landId: string, request: LandDeedRequest): Observable<LandDeed> {
    return this.http.post<ApiResponse<LandDeed>>(`${this.base(workspaceId)}/${landId}/deeds`, request).pipe(map(res => res.data));
  }

  getBoundaries(workspaceId: string, landId: string): Observable<LandBoundary[]> {
    return this.http.get<ApiResponse<LandBoundary[]>>(`${this.base(workspaceId)}/${landId}/boundaries`).pipe(map(res => res.data));
  }

  addBoundary(workspaceId: string, landId: string, request: LandBoundaryRequest): Observable<LandBoundary> {
    return this.http
      .post<ApiResponse<LandBoundary>>(`${this.base(workspaceId)}/${landId}/boundaries`, request)
      .pipe(map(res => res.data));
  }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd ui && npx ng test --include='src/app/core/land.service.spec.ts'`
Expected: PASS, all 14 tests green (6 original + 8 new).

- [ ] **Step 5: Commit**

```bash
git add ui/src/app/core/land.service.ts ui/src/app/core/land.service.spec.ts
git commit -m "feat: add survey/deed/boundary CRUD to LandService"
```

---

### Task 2: LandDetailPanelComponent

**Files:**
- Create: `ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts`

**Interfaces:**
- Consumes: `LandService` (Task 1: `getById`, `update`, `getSurveys`, `addSurvey`, `getDeeds`, `addDeed`, `getBoundaries`, `addBoundary`), `Land`/`Address`/`LandSurvey`/`LandDeed`/`LandBoundary` types (Task 1).
- Produces: `LandDetailPanelComponent` with `@Input() workspaceId: string`, `@Input() landId: string`. No outputs — fully self-contained, nothing for a parent to react to.

- [ ] **Step 1: Write the component**

```typescript
// ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts
import { Component, Input, OnInit, signal } from '@angular/core';
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
          <h3 class="text-xs font-semibold text-neutral-500 uppercase mb-sm">Details</h3>
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

  loading = signal(true);
  error = signal('');
  land = signal<Land | null>(null);
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
```

- [ ] **Step 2: Verify it compiles**

Run: `cd ui && npx tsc --noEmit -p tsconfig.app.json`
Expected: no errors from this file.

- [ ] **Step 3: Commit**

```bash
git add ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts
git commit -m "feat: add LandDetailPanelComponent - shared full land management view"
```

---

### Task 3: JobDetailComponent — expandable land rows

**Files:**
- Modify: `ui/src/app/pages/job/job-detail.component.ts`

**Interfaces:**
- Consumes: `LandDetailPanelComponent` (Task 2).

Replace the static Land row display with an expand/collapse toggle. Only one land expanded at a time.

- [ ] **Step 1: Add the import and `expandedLandId` state**

In `ui/src/app/pages/job/job-detail.component.ts`, add to imports:

```typescript
import { LandDetailPanelComponent } from '../land/land-detail-panel/land-detail-panel.component';
```

Add `LandDetailPanelComponent` to the `@Component` decorator's `imports` array (alongside `AddPersonWidgetComponent`, `AddLandWidgetComponent`).

Add a new signal to the component class, next to the other signals:

```typescript
expandedLandId = signal<string | null>(null);

toggleLand(landId: string): void {
  this.expandedLandId.update(current => (current === landId ? null : landId));
}
```

- [ ] **Step 2: Replace the Land row template**

Find this block in the template (the Land section's `@for` loop):

```html
              @for (l of lands(); track l.landId) {
                <div class="flex items-center justify-between px-md py-sm rounded bg-neutral-50">
                  <div>
                    <span class="text-sm text-neutral-900">{{ addressLine(l) }}</span>
                    @if (l.size) {
                      <span class="text-xs text-neutral-500 block">{{ l.size }} {{ l.sizeUnit }}</span>
                    }
                  </div>
                  <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="removeLand(l)">
                    Remove
                  </button>
                </div>
              }
```

Replace it with:

```html
              @for (l of lands(); track l.landId) {
                <div class="rounded bg-neutral-50">
                  <div class="flex items-center justify-between px-md py-sm cursor-pointer" (click)="toggleLand(l.landId)">
                    <div>
                      <span class="text-sm text-neutral-900">{{ addressLine(l) }}</span>
                      @if (l.size) {
                        <span class="text-xs text-neutral-500 block">{{ l.size }} {{ l.sizeUnit }}</span>
                      }
                    </div>
                    <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="removeLand(l); $event.stopPropagation()">
                      Remove
                    </button>
                  </div>
                  @if (expandedLandId() === l.landId) {
                    <div class="px-md pb-md pt-sm border-t border-neutral-200">
                      <app-land-detail-panel [workspaceId]="workspaceId" [landId]="l.landId" />
                    </div>
                  }
                </div>
              }
```

- [ ] **Step 3: Verify it compiles**

Run: `cd ui && npx tsc --noEmit -p tsconfig.app.json`
Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add ui/src/app/pages/job/job-detail.component.ts
git commit -m "feat: make Job detail's land rows expand into the full LandDetailPanel"
```

---

### Task 4: CreateLandModalComponent

**Files:**
- Create: `ui/src/app/pages/land/create-land-modal/create-land-modal.component.ts`

**Interfaces:**
- Consumes: `LandService.create(workspaceId, request): Observable<Land>` (existing), `Land`/`Address` types (existing).
- Produces: `CreateLandModalComponent` with `@Input() workspaceId: string`, `@Output() cancel: EventEmitter<void>`, `@Output() created: EventEmitter<Land>`.

Mirrors `CreateJobModalComponent`'s structure with Street/City/Size/Unit fields instead of a single Title field.

- [ ] **Step 1: Write the component**

```typescript
// ui/src/app/pages/land/create-land-modal/create-land-modal.component.ts
import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Address, Land, LandService } from '../../../core/land.service';

@Component({
  selector: 'app-create-land-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-md" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">New land</h2>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Street</label>
            <input class="input-field" type="text" name="street" [(ngModel)]="street" required autofocus />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">City</label>
            <input class="input-field" type="text" name="city" [(ngModel)]="city" />
          </div>
          <div class="flex gap-sm">
            <div class="flex-1">
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Size</label>
              <input class="input-field" type="number" name="size" [(ngModel)]="size" />
            </div>
            <div class="flex-1">
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Unit</label>
              <input class="input-field" type="text" name="sizeUnit" [(ngModel)]="sizeUnit" placeholder="e.g. acres" />
            </div>
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading() || !street.trim()">
              {{ loading() ? 'Creating…' : 'Create' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class CreateLandModalComponent {
  @Input() workspaceId = '';
  @Output() cancel = new EventEmitter<void>();
  @Output() created = new EventEmitter<Land>();

  street = '';
  city = '';
  size: number | null = null;
  sizeUnit = '';
  loading = signal(false);
  error = signal('');

  constructor(private landService: LandService) {}

  submit(): void {
    if (!this.street.trim()) return;
    this.error.set('');
    this.loading.set(true);

    const address: Address = { street: this.street.trim(), city: this.city.trim() || null, district: null, postalCode: null, country: null };

    this.landService
      .create(this.workspaceId, { address, size: this.size ?? undefined, sizeUnit: this.sizeUnit.trim() || undefined })
      .subscribe({
        next: (land) => {
          this.loading.set(false);
          this.created.emit(land);
        },
        error: (err) => {
          this.loading.set(false);
          this.error.set(err.error?.message ?? 'Could not create land record.');
        }
      });
  }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `cd ui && npx tsc --noEmit -p tsconfig.app.json`
Expected: no new errors from this file.

- [ ] **Step 3: Commit**

```bash
git add ui/src/app/pages/land/create-land-modal/create-land-modal.component.ts
git commit -m "feat: add CreateLandModalComponent for standalone land creation"
```

---

### Task 5: LandListComponent, LandDetailComponent, routes, sidebar link

**Files:**
- Create: `ui/src/app/pages/land/land-list.component.ts`
- Create: `ui/src/app/pages/land/land-detail.component.ts`
- Modify: `ui/src/app/app.routes.ts`
- Modify: `ui/src/app/shell/sidebar.component.ts`

**Interfaces:**
- Consumes: `LandService.search` (existing, used with no query to list everything), `LandService.getDeeds`/`getSurveys` (Task 1, for the count columns), `Land` type, `CreateLandModalComponent` (Task 4), `LandDetailPanelComponent` (Task 2), `addressLine` (existing).

- [ ] **Step 1: Write `LandListComponent`**

```typescript
// ui/src/app/pages/land/land-list.component.ts
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { forkJoin } from 'rxjs';
import { map } from 'rxjs/operators';
import { Land, LandService, addressLine } from '../../core/land.service';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';
import { CreateLandModalComponent } from './create-land-modal/create-land-modal.component';

interface LandRow {
  land: Land;
  deedCount: number;
  surveyCount: number;
}

@Component({
  selector: 'app-land-list',
  standalone: true,
  imports: [CommonModule, CreateLandModalComponent],
  template: `
    <div class="p-lg max-w-4xl mx-auto">
      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Land</h1>
        <button class="btn-primary" (click)="modalOpen.set(true)">New land</button>
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (error()) {
        <div class="card text-sm text-primary-500">
          {{ error() }}
          <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
        </div>
      } @else if (rows().length === 0) {
        <div class="card text-center text-sm text-neutral-500">No land records yet. Create one to get started.</div>
      } @else {
        <div class="card p-0 overflow-x-auto">
          <table class="w-full text-sm">
            <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
              <tr>
                <th class="text-left px-lg py-sm font-medium">Address</th>
                <th class="text-left px-lg py-sm font-medium">Size</th>
                <th class="text-left px-lg py-sm font-medium">Deeds</th>
                <th class="text-left px-lg py-sm font-medium">Surveys</th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.land.landId) {
                <tr class="border-t border-neutral-200 cursor-pointer hover:bg-neutral-50" (click)="open(row.land)">
                  <td class="px-lg py-sm text-neutral-900">{{ addressLine(row.land) }}</td>
                  <td class="px-lg py-sm text-neutral-600">
                    @if (row.land.size) {
                      {{ row.land.size }} {{ row.land.sizeUnit }}
                    } @else {
                      —
                    }
                  </td>
                  <td class="px-lg py-sm text-neutral-600">{{ row.deedCount }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ row.surveyCount }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>

    @if (modalOpen()) {
      <app-create-land-modal [workspaceId]="workspaceId" (cancel)="modalOpen.set(false)" (created)="onCreated($event)" />
    }
  `
})
export class LandListComponent implements OnInit {
  workspaceId = '';
  rows = signal<LandRow[]>([]);
  loading = signal(true);
  error = signal('');
  modalOpen = signal(false);

  addressLine = addressLine;

  constructor(
    private landService: LandService,
    private currentWorkspace: CurrentWorkspaceService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    this.landService.search(this.workspaceId).subscribe({
      next: (lands) => {
        if (lands.length === 0) {
          this.rows.set([]);
          this.loading.set(false);
          return;
        }
        forkJoin(
          lands.map(land =>
            forkJoin({
              deeds: this.landService.getDeeds(this.workspaceId, land.landId),
              surveys: this.landService.getSurveys(this.workspaceId, land.landId)
            }).pipe(map(({ deeds, surveys }) => ({ land, deedCount: deeds.length, surveyCount: surveys.length })))
          )
        ).subscribe({
          next: (rows) => {
            this.rows.set(rows);
            this.loading.set(false);
          },
          error: (err) => {
            this.error.set(err.error?.message ?? 'Could not load land records.');
            this.loading.set(false);
          }
        });
      },
      error: (err) => {
        this.error.set(err.error?.message ?? 'Could not load land records.');
        this.loading.set(false);
      }
    });
  }

  open(land: Land): void {
    this.router.navigate(['/app/workspace', this.workspaceId, 'lands', land.landId]);
  }

  onCreated(land: Land): void {
    this.modalOpen.set(false);
    this.router.navigate(['/app/workspace', this.workspaceId, 'lands', land.landId]);
  }
}
```

- [ ] **Step 2: Write `LandDetailComponent`**

```typescript
// ui/src/app/pages/land/land-detail.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';
import { LandDetailPanelComponent } from './land-detail-panel/land-detail-panel.component';

@Component({
  selector: 'app-land-detail',
  standalone: true,
  imports: [CommonModule, LandDetailPanelComponent],
  template: `
    <div class="p-lg max-w-3xl mx-auto">
      <div class="card">
        <app-land-detail-panel [workspaceId]="workspaceId" [landId]="landId" />
      </div>
    </div>
  `
})
export class LandDetailComponent implements OnInit {
  workspaceId = '';
  landId = '';

  constructor(private currentWorkspace: CurrentWorkspaceService, private route: ActivatedRoute) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.landId = this.route.snapshot.paramMap.get('landId') ?? '';
  }
}
```

- [ ] **Step 3: Wire the routes**

Edit `ui/src/app/app.routes.ts` — add these imports:

```typescript
import { LandListComponent } from './pages/land/land-list.component';
import { LandDetailComponent } from './pages/land/land-detail.component';
```

Add these two route entries inside the `workspace/:id` children array, alongside the `jobs`/`jobs/:jobId` entries:

```typescript
{ path: 'lands', component: LandListComponent },
{ path: 'lands/:landId', component: LandDetailComponent },
```

- [ ] **Step 4: Add the sidebar link**

Edit `ui/src/app/shell/sidebar.component.ts` — in the template, inside the workspace nav block (the `@if (workspace(); as ws)` branch), add a new link immediately after the existing "Jobs" `<a>` and before "Members":

```html
          <a
            [routerLink]="['/app/workspace', ws.workspaceId, 'lands']"
            routerLinkActive="bg-primary-50 text-primary-600"
            class="flex items-center gap-sm px-md py-sm rounded text-sm text-neutral-700 hover:bg-neutral-100"
            (click)="navigate.emit()"
          >
            Land
          </a>
```

- [ ] **Step 5: Verify it compiles**

Run: `cd ui && npx tsc --noEmit -p tsconfig.app.json`
Expected: clean, zero errors.

- [ ] **Step 6: Commit**

```bash
git add ui/src/app/pages/land/land-list.component.ts ui/src/app/pages/land/land-detail.component.ts ui/src/app/app.routes.ts ui/src/app/shell/sidebar.component.ts
git commit -m "feat: add dedicated Land list/detail screens and sidebar link"
```

---

### Task 6: Full verification

- [ ] **Step 1: Run the full build**

Run: `cd ui && ng build`
Expected: clean build, no errors.

- [ ] **Step 2: Run all Land-related unit tests**

Run: `cd ui && npx ng test --include='src/app/core/land.service.spec.ts'`
Expected: all 14 PASS.

- [ ] **Step 3: Manual smoke test**

With API + UI running and logged in as an Admin:
1. Open a job with an attached land → click the land row → confirm it expands showing Details/Surveys/Deeds/Boundaries sections.
2. Edit the street field, click away → reload the job → confirm it persisted.
3. Add a survey, a deed (checked "current"), and a boundary → confirm each appears in its list immediately.
4. Add a second deed marked "current" → confirm the first deed's "Current" badge disappears and the new one shows it (proves the refetch-not-patch approach in `submitDeed()` works).
5. Click a different attached land row (if the job has more than one) → confirm the first panel collapses and the second expands.
6. Navigate to the sidebar's new "Land" link → confirm the list shows all lands with correct deed/survey counts.
7. Click "New land" → create one → confirm navigation to its detail page, and that it's a full `LandDetailPanelComponent` (not the inline Job-detail context).

- [ ] **Step 4: Commit is already done per-task — no separate final commit needed for this task.**
