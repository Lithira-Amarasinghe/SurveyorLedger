# Land Management UI — Design Spec (Phase 2)

## Purpose

Phase 1 ([2026-08-09-job-ui-design.md](2026-08-09-job-ui-design.md)) shipped Job list/detail with inline search-or-create for Land, but only shows an address+size card — no way to see or edit a land's full details, or manage its Survey/Deed/Boundary history, from anywhere in the UI. This phase closes that gap: full land management reachable both from inside a Job and as its own dedicated screen.

## Core idea

One reusable component — `LandDetailPanelComponent` — is the single implementation of "view and manage everything about a Land." It's mounted in two places:
1. **Inside Job detail** — clicking an attached land card expands it inline, no navigation away from the job page.
2. **Dedicated Land screens** — a new list page and a detail page that's just this panel on its own route.

## Backend (already built, not yet wired into the Angular `LandService`)

- `GET /workspace/{id}/land/{landId}` — get one
- `PUT /workspace/{id}/land/{landId}` — update address/size/GPS/notes
- `GET/POST /workspace/{id}/land/{landId}/surveys`
- `GET/POST /workspace/{id}/land/{landId}/deeds`
- `GET/POST /workspace/{id}/land/{landId}/boundaries`

## `LandService` additions

```typescript
getById(workspaceId, landId): Observable<Land>
update(workspaceId, landId, request: LandRequest): Observable<Land>
getSurveys(workspaceId, landId): Observable<LandSurvey[]>
addSurvey(workspaceId, landId, request: LandSurveyRequest): Observable<LandSurvey>
getDeeds(workspaceId, landId): Observable<LandDeed[]>
addDeed(workspaceId, landId, request: LandDeedRequest): Observable<LandDeed>
getBoundaries(workspaceId, landId): Observable<LandBoundary[]>
addBoundary(workspaceId, landId, request: LandBoundaryRequest): Observable<LandBoundary>
```

New types: `LandSurvey {id, landId, surveyPlanNumber, surveyDate, surveyedByName: string | null, notes: string | null, createdAt}`, `LandDeed {id, landId, deedNumber, issuedDate, isCurrent, notes: string | null, createdAt}`, `LandBoundary {id, landId, label, description: string | null, createdAt}`.

## `LandDetailPanelComponent`

`@Input() workspaceId`, `@Input() landId`. Self-contained: fetches the Land + its Surveys/Deeds/Boundaries on init, no data passed in from the parent.

Layout, sectioned like Job detail:
1. **Details** — Address (street/city/district inline-editable), Size + unit, GPS coordinates, Notes. Same inline-edit-on-blur pattern as Job detail's title/description.
2. **Surveys** — list (plan number, date, surveyed-by-name, notes) + small inline "Add survey" form (matches `AddLandWidget`'s create-form styling: plain inputs in a bordered box, not a modal).
3. **Deeds** — list showing `IsCurrent` as a badge on the current one + "Add deed" form. A new deed with `IsCurrent` checked is understood by the backend to supersede the prior current deed automatically — the UI just submits, no client-side toggling logic needed.
4. **Boundaries** — list (label, description) + "Add boundary" form (free-text label, not a fixed N/S/E/W set).

## Job detail changes

`JobDetailComponent`'s Land section: each attached land row becomes a clickable header (address + size, as today) that toggles an `expandedLandId` signal. When expanded, renders `<app-land-detail-panel [workspaceId]="workspaceId" [landId]="l.landId" />` directly beneath that row. Only one land expanded at a time (clicking another collapses the first) — keeps the page from growing unbounded with multiple full panels open.

## Dedicated Land screens

- **Route:** `workspace/:id/lands` → `LandListComponent`; `workspace/:id/lands/:landId` → `LandDetailComponent` (thin wrapper rendering `LandDetailPanelComponent` full-page, matching `JobDetailComponent`'s page shell).
- **`LandListComponent`** — table (Address, Size, Deed count, Survey count), matching `JobListComponent`/`MembersComponent` styling. Row click → detail page. "New land" button → `CreateLandModalComponent` (address + size fields, mirrors `CreateJobModalComponent`'s structure) → on success, navigate to the new land's detail page.
- **Sidebar:** add a "Land" link (`sidebar.component.ts`) alongside Jobs/Members/Roles — there's currently no Land entry point outside of Job detail.

## Out of scope
- Land deletion UI (backend soft-delete exists, not surfaced).
- Bulk operations.
- Map/GPS picker — GPS stays a plain text field.

## Verification
- `ng build` clean, new `LandService` methods covered by unit tests (`HttpTestingController`, same pattern as existing `land.service.spec.ts`).
- Manual: open a job with an attached land → expand it → edit address → add a survey → add a deed → confirm it shows `IsCurrent` → collapse → expand another land, confirm the first collapses. Navigate to the dedicated Land list → confirm the land and its deed/survey counts show → click "New land" → create one → confirm it lands on the new detail page.
