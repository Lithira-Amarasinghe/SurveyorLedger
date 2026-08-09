# Job / Land / User — Design Spec

## Purpose

Start the Job feature (previously Phase 3, now unblocked — `.claude/rules.md` updated to remove the exclusion). Covers the foundation entities only: User (unified for staff and clients), Job, Land, and their relations. Milestones, Documents, Payments, Comments/Notifications/ActivityLog are deliberately out of scope — later phases, designed to bolt onto this shape without rework.

## Core workflow this design serves

A call comes in while a surveyor is out working. Admin creates a Job with just a title, and a client record with just name + phone — no email, no login required yet. Land details and other specifics get filled in later, same day or after. If the client has an email, admin sends an invite (existing `Invitation` flow); on acceptance the client can log in and see their own job(s) — nothing else in the workspace.

## Entities

### User (unified — no separate Client entity)

```
User
- Id
- Email            nullable
- PasswordHash     nullable
- FirstName, LastName
- Phone            nullable
- Address          owned type (Street, City, District, PostalCode, Country) — no separate table
- EmailVerified, EmailVerifiedAt
- CreatedAt, UpdatedAt, IsActive
```

Rationale: Clients and staff are both "people in the system," differing only in whether they have login credentials yet. A client starts as a User row with Email/PasswordHash null; the existing `Invitation` flow fills those in later. Avoids a parallel Client entity and the two-FK junction table that would otherwise be needed everywhere a "person" is referenced.

`Email`/`PasswordHash` change from required to nullable — this is a breaking change to the existing `User` entity/migration history, called out explicitly since it touches auth.

**Access model:** Clients get no workspace-level `UserAccess`/Role — granting one would expose the whole workspace via existing RBAC (Casbin is workspace-scoped, not job-scoped). Instead, `JobService` checks: if caller has no workspace role, restrict job queries to jobs where they appear in `JobParticipant`. This is an explicit service-layer check, not a Casbin policy — noted so it isn't lost or "fixed" into a Casbin rule later without realizing why it's separate.

### Job

```
Job
- Id, WorkspaceId
- JobNumber        auto-generated, unique per workspace (e.g. JOB-0001)
- Title
- Description      nullable
- Status           enum (Draft, Scheduled, InProgress, Completed, Cancelled)
- CreatedBy         FK -> User
- CreatedAt, UpdatedAt, IsActive
```

Creatable with Title only (defaults Status=Draft). No Client/Land/Surveyor FK directly on Job — those are relations via junction tables below, since all three are many-to-many with Job.

### JobParticipant (clients, surveyors, assistants on a job)

```
JobParticipant
- Id, JobId, UserId
- ParticipantType   enum (Client, Surveyor, Assistant, Other)
- IsActive
- AddedBy           FK -> User
- AddedAt
```

One join table for every kind of person on a job. New participant kinds later = new enum value, no schema change.

### JobLand (many-to-many; supports reusing an existing Land on a new job)

```
JobLand
- Id, JobId, LandId
```

Search on Land address/deed/plan number when creating a job lets admin attach an existing Land instead of re-entering it.

### Land

```
Land
- Id, WorkspaceId
- Address          owned type
- Size, SizeUnit
- GpsCoordinates    nullable
- Notes             nullable
- CreatedAt, UpdatedAt, IsActive
```

Title number and survey plan number are NOT single fields here — they live in `LandDeed`/`LandSurvey` below, since both need multi-record history.

### LandSurvey (history — many per Land)

```
LandSurvey
- Id, LandId
- SurveyPlanNumber
- SurveyDate
- SurveyedByName    string, nullable (free text — historical surveys predate any User account)
- Notes
- CreatedAt
```

### LandDeed (history — many per Land, supports government reissue)

```
LandDeed
- Id, LandId
- DeedNumber
- IssuedDate
- IsCurrent         bool (marks the active deed; superseded ones stay with IsCurrent=false)
- Notes
- CreatedAt
```

### LandBoundary (flexible surrounding-property details — many per Land)

```
LandBoundary
- Id, LandId
- Label             string (free text — not restricted to N/S/E/W)
- Description       nullable
- CreatedAt
```

## Explicitly deferred (designed for, not built now)

- **Documents** (scanned PDFs for deeds/surveys, job attachments): attaches later via a generic `EntityType + EntityId` reference. `LandSurvey`, `LandDeed`, and `Job` already have their own `Id`, so this requires no schema change to them when built.
- **Milestones, Payments, Comments/Notifications/ActivityLog**: separate future phases, not referenced by anything in this spec.

## Out of scope for this spec

- Angular UI (backend only this pass).
- Casbin resource/action policy definitions for `job`/`land` resources — implementation plan covers wiring, not policy design (reuses existing pattern).

## Verification

- `dotnet build` clean.
- Unit tests: Job creatable with Title only; JobParticipant supports multiple Clients + multiple Surveyors per Job; JobLand allows attaching an existing Land to a new Job; LandDeed/LandSurvey support multiple records per Land; client-role User sees only their own JobParticipant jobs, not full workspace list.
- Manual: create Job via call-workflow (title+client name/phone only) → later attach Land (existing, via search) → attach second Client → invite client by email → confirm client login shows only their job.
