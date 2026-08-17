# Job Participant UI — Design

**Date:** 2026-08-17
**Status:** Approved

## Context

Recent backend work (same day) split `job.manage_participants` out of `job.edit`, moved
chaining-role invites (Surveyor) to Workspace scope with a Job descendant grant, and added
`ScopedAccessService.GetUsersWithAccessAsync` (direct grants + ancestor `*.view_all` holders).
None of this reached the UI yet. Three gaps:

1. `job-detail.component.ts` shows add/remove-participant controls to everyone; Surveyor now
   gets a 403 on click since `job.manage_participants` is Admin-only.
2. `InvitationController` only populates `JobLabel` when `Invitation.ScopeType == Job` — the
   new Workspace-scope-with-Job-descendant invites (chaining roles) get a null `JobLabel`,
   so the pending-invitations screen silently drops the job assignment from view.
3. `GetUsersWithAccessAsync` has no controller endpoint or UI consumer.

## Section A — Real permission check, not a role-name string

`members.component.ts` already gates Admin-only UI with `roles.includes('Admin')` off
`CurrentWorkspaceService`. That works there because workspace membership *is* the role. It's
wrong for job participant management: `job.manage_participants` is a permission, and hardcoding
"Admin" in the UI silently drifts from whatever the backend actually enforces the moment that
permission is granted to a second role.

- `IScopedAccessService.CanAccessJobAsync(userId, workspaceId, jobId, action) -> bool` — new
  method, same rule `EnsureJobAccessAsync` already enforces (blanket `job.view_all` bypass via
  workspace-scope Casbin check, else per-job Casbin check), returns bool instead of throwing.
  `EnsureJobAccessAsync` itself is untouched — new method, not a refactor, zero risk to its
  existing tests/error messages.
- `JobResponse` gets `CanManageParticipants: bool`, computed in `JobService.GetByIdAsync` via
  `CanAccessJobAsync(..., "manage_participants")`.
- `job-detail.component.ts`: add/remove controls wrapped in `@if (job()?.canManageParticipants)`.

## Section B — Show workspace and job together on pending invites

`InvitationController` has three spots keyed only on `ScopeType == Job` to resolve a job label:
`ListMyInvitations`, `ResolveScopeAsync` (used by the token-preview and accept endpoints), and
`AcceptInvitation`'s `JobId` field. All three need the same fix: when
`DescendantScopeType == Job`, resolve *that* job too and populate `JobLabel` from it. No DTO
shape change - `MyInvitationResponse`/`InvitationPreviewResponse` already carry `WorkspaceName`
and `JobLabel` as independent nullable fields.

`invitations.component.ts` copy: `"Job only: {{ inv.jobLabel }}"` is wrong framing once both
fields can be set together - it's never "job only" when `workspaceName` is also shown above it.
Change to `"Also assigned to: {{ inv.jobLabel }}"`.

## Section C — Effective access holders, not just viewers

No backend logic change - `GetUsersWithAccessAsync`'s union (direct grants + `*.view_all`
holders) already returns real effective access, not read-only visibility: the view_all-holding
role (Admin) carries edit/delete/manage_participants bundled into the same grant. Only the
framing needs to be explicit:

- New endpoint: `GET /workspace/{workspaceId}/job/{jobId}/effective-participants` on
  `JobController`, backed by `JobService.GetEffectiveParticipantsAsync` (already exists).
- Response DTO tags each row `AccessType: "Direct" | "WorkspaceWide"` so the UI can label rows
  clearly (e.g. "Admin — full workspace access" vs "Surveyor — assigned to this job") instead of
  presenting a flat, unexplained list.
- UI: a read-only section on the job detail participants tab, separate from the
  manage-participants controls from Section A (which stay Direct-only - you can't "remove" an
  Admin's workspace-wide access from a job).

## Out of scope

- Changing which role holds `job.manage_participants` (Admin-only stays as-is).
- Any change to `GetParticipantsAsync` (Direct-only) - it remains what Section A's manage UI
  calls.
- Organization-level scope - not built yet, nothing here depends on it existing.
