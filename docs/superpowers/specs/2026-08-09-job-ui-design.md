# Job Feature UI — Design Spec (Phase 1 slice)

## Purpose

Backend (Job/Land/JobParticipant/JobLand/Client) is done — see [2026-08-09-job-land-user-design.md](2026-08-09-job-land-user-design.md). This spec covers the Angular UI for the actual daily workflow: admin gets a call, creates a job, captures the client, attaches land, all in one flow. The sidebar already has a dead "Jobs" link pointing at a `ComingSoonComponent` placeholder — this replaces it.

**Scope:** Job list + Job detail page only. Dedicated Land management screens (list, deed/survey/boundary editing) and a dedicated Client management screen are deferred — this slice covers the workflow end to end, not full admin coverage of every entity.

## Routes

`app.routes.ts`, inside the existing `workspace/:id` children:
- `jobs` — replace `ComingSoonComponent` with `JobListComponent`
- `jobs/:jobId` — new, `JobDetailComponent`

## Pages

### Job list (`pages/job/job-list.component.ts`)
Table matching `MembersComponent`'s styling exactly: JobNumber, Title, Status (colored badge), created date. Row click → detail page.

"New job" button → small modal (matches `create-modal` pattern), single required Title field. On success, navigate straight to the new job's detail page — nothing else to fill in yet, matches the backend's title-only creation.

### Job detail (`pages/job/job-detail.component.ts`)
Single scrolling page, sectioned, no tabs:

1. **Header** — JobNumber (read-only), Title (inline-editable), Status (dropdown: Draft/Scheduled/InProgress/Completed/Cancelled), Description (inline-editable, optional).
2. **People** — unified list of all `JobParticipant`s regardless of type (Client/Surveyor/Assistant/Other shown as a badge per row), each removable. "Add person" widget: one search box searches across all people (see PersonService below), type-ahead results, "Create new client" fallback shown if no match, `ParticipantType` selected at add time via a dropdown next to the result/create action.
3. **Land** — attached Land cards (address, size/unit). "Add land" widget: search box hitting `GET /land?query=`, "Create new land" fallback with a small inline form (address fields, size, unit) if no match.

## Services (new)

All follow `WorkspaceService`'s existing pattern: plain `HttpClient`, `Observable<T>` return types, unwrap the backend's `ApiResponse<T>` envelope via `map`.

- **`JobService`** (`core/job.service.ts`) — `list(workspaceId)`, `create(workspaceId, title)`, `getById(workspaceId, jobId)`, `update(workspaceId, jobId, {title, description})`, `updateStatus(workspaceId, jobId, status)`, `addParticipant(workspaceId, jobId, userId, participantType)`, `removeParticipant(workspaceId, jobId, userId)`, `addLand(workspaceId, jobId, landId)`, `removeLand(workspaceId, jobId, landId)`.
- **`LandService`** (`core/land.service.ts`) — `search(workspaceId, query)`, `create(workspaceId, request)`.
- **`PersonService`** (`core/person.service.ts`) — the unified layer the UI actually talks to for the People section:
  - `searchPeople(workspaceId, query)`: calls `GET /workspace/{id}/client?query=` and `WorkspaceService.getMembers(workspaceId)` (client-side filtered by query on name/email), merges into one `Person[]` list (`{userId, name, subtitle, isStaff}`), de-duplicated by `userId`.
  - `createClient(workspaceId, {firstName, lastName, phone})`: wraps `POST /workspace/{id}/client`.
  - No separate `ClientService` — this is the only frontend-facing service for the People widget, so `JobDetailComponent` never needs to know there are two backend sources.

## Error/loading conventions
Matches `MembersComponent`: `loading`/`error` signals per page, retry button on error, optimistic local update with rollback on failed mutations where the UI already knows what to roll back to (e.g. status change, remove-participant).

## Out of scope this slice
- Dedicated Land list/CRUD page (search/create-inline in Job detail covers reuse for now).
- Dedicated Client list/CRUD page.
- Milestones/Documents/Payments UI (not built on backend either).
- Survey/Deed/Boundary editing UI for Land (backend supports it, not surfaced here).

## Verification
- `ng build` clean.
- Manual: create job (title only) → land on detail page → add a new client via search-then-create → add an existing staff member as Surveyor via the same search box → search-then-create a new land → change status → navigate back to list, confirm new job appears with correct status badge.
