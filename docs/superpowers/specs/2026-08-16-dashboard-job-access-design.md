# Dashboard: cross-scope job access + job-only viewing route

## Context

Job-only assignment (a user granted access to a single job with no workspace
membership) was built earlier this session — invites, consent rules, the
Members-list display all support it. But two gaps remain:

1. **No way to reach it.** A job-only user has nothing to click — the
   dashboard only ever listed workspaces (`GetUserWorkspacesAsync`, correctly
   requires workspace-scope access). There is no entry point for a job-only
   grant.
2. **Even with a direct link, it 404s.** `workspaceResolveGuard` gates every
   `/app/workspace/:id/**` route behind `workspace.view`, which a job-only
   user never holds. Discovered while fixing the invite-accept redirect: the
   API now returns the correct job/workspace ids, but the guard still bounces
   the user to the dashboard before the job page ever loads.

This spec covers both: a dashboard section for direct job access, and a
guard-safe route to actually view it.

## Decisions

- **Same dashboard page, no new route for the list.** Two sections stacked:
  Workspaces (unchanged), then "Jobs (direct access)" below it — jobs where
  the user holds a job-scope grant but not a workspace-scope one. Mirrors the
  "My Drive" / "Shared with me" split already standard in this class of
  product (Drive, Linear teams vs. shared issues, Notion shared pages) —
  container-level membership grouped normally, individually-shared items
  listed separately, not merged in.
- **A view filter, not a separate page**, toggles between three states on the
  same dashboard: default (both sections), "Jobs" (flattens every accessible
  job — workspace-derived and direct — into one list, workspace name shown
  per row), "Workspace" (workspaces section only, direct-access jobs hidden -
  they have no workspace to list under). Within the flattened Jobs view,
  narrow further by workspace, status, and access type (Member/Job-only).
- **New route `/app/job/:jobId`, not a patch to `workspaceResolveGuard`.**
  Modifying the existing guard to conditionally allow job-only access risks
  leaking workspace-shell nav (Overview/Land/Billing/Members/Roles tabs) to
  someone who can't use most of them. A separate minimal route sidesteps
  that entirely: same `JobDetailComponent` already built, just not wrapped in
  the workspace sidebar - a thin bar (workspace name for context, back-to-
  dashboard link) instead. Zero changes to the existing guard or route for
  full workspace members - purely additive.
- **Org-readiness, not Org itself.** Not building the Org level now. The
  `AccessType` tag (Member/JobOnly) and the union query stay expressed
  generically enough that a third level later is one more branch in the
  existing ancestor-walk pattern (`HasConsentCoverageAsync`,
  `ScopedAccessService`), not a rewrite of this feature.

## Backend

**New method** `IJobService.GetMyJobsAsync(Guid userId)` (or a home on
`ScopedAccessService` if that fits better at implementation time - decide
then, not architecturally significant): union of two existing per-workspace
rules, run across every workspace instead of one -
1. Jobs in any workspace where the user holds a role with `job.view_all`
   (same check `WorkspaceService`/`JobService` already do per-workspace).
2. Jobs reachable via `ScopedAccessService.AccessibleJobIds(userId)` (direct
   job-scope grants).

Each result tagged `AccessType: "Member" | "JobOnly"` - is the user a
workspace-scope member of that job's workspace or not. Include workspace
name/id and job fields needed for the list (number, title, status).

**New endpoint** `GET /api/jobs/{jobId}` (not nested under `/workspace/{id}`)
- resolves the job's `WorkspaceId` internally, runs the same
`EnsureJobAccessAsync` check `JobService.GetByIdAsync` already does (already
proven to work for a job-only Client - no `workspace.view` involved), returns
the job plus its workspace name for display context.

**New endpoint** `GET /api/jobs/mine` - wraps `GetMyJobsAsync`, backs the
dashboard's Jobs section/filtered view.

## Frontend

**Dashboard component**: fetch `GET /api/jobs/mine` alongside the existing
workspace list. Render the two-section default view; wire the Jobs/Workspace
filter toggle and the within-Jobs-view sub-filters (workspace, status, access
type) as client-side filtering over the already-fetched list (no refetch per
filter change - the full set is small enough per user).

**New route** `/app/job/:jobId`: new `jobAccessGuard` (calls the new
`GET /api/jobs/{jobId}`, redirects to dashboard with an error param on
failure, same pattern as `workspaceResolveGuard`'s `catchError`). Renders a
minimal layout: top bar (workspace name, "Back to dashboard") + the existing
`JobDetailComponent`, no sidebar.

**Row routing** (both the direct-access section and the flattened Jobs
view): `AccessType === 'Member'` → `/app/workspace/:workspaceId/jobs/:jobId`
(existing route, full shell). `AccessType === 'JobOnly'` → `/app/job/:jobId`
(new route, minimal shell).

**Invite-accept redirect** (`accept-invite.component.ts`, from the earlier
session fix): job-scope accept now routes to `/app/job/:jobId` instead of
`/app/workspace/:id/jobs/:jobId` - this is what actually closes out that
redirect bug, since the workspace-prefixed route was never reachable for a
job-only accepter regardless of which id it carried.

## Out of scope

- Org level entity/table/UI.
- Pagination on the Jobs list (dataset small enough today - revisit if a
  single user's accessible-job count grows large).
- Search/text-filter on the Jobs list (only the three agreed filters: workspace,
  status, access type).
