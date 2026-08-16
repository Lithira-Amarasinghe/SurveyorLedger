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
- **Org-readiness, not Org itself.** Not building the Org level now. Every
  piece of this feature that would otherwise hardcode "two levels" is instead
  expressed as a walk over whatever hierarchy chain exists - see "Scaling
  mechanism" below. Adding Org later means adding one entry to the existing
  chain resolver (already done once this session for `HasConsentCoverageAsync`)
  and nothing else in this feature changes shape.

## Scaling mechanism

This is the part that has to be right for Org to slot in later without a
rewrite - three places in this feature would naturally get hardcoded to
"Workspace vs Job" if built carelessly. Each is instead built on the generic
ancestor-chain walk already established by `HasConsentCoverageAsync`
(`ScopedAccessService`) and `RoleScopes` (DB-driven role↔scope mapping, no
hardcoded switch) earlier this session:

1. **`AccessType` is not a fixed two-value enum.** It's "the highest scope
   level in this job's ancestor chain at which the user holds a qualifying
   grant" - computed by walking the chain (`Job → Workspace → [Org, when it
   exists]`) top-down and returning the first match. Today that walk only has
   two rungs, so the practical values are `Workspace` (member at-or-above the
   job) or `Job` (direct grant only, nothing above). Adding Org means the walk
   gains a third rung and a third possible value (`Org`) - the field, the
   query, and the UI that renders it don't change, they just start seeing a
   value they already knew was theoretically possible.
2. **`GetMyJobsAsync`'s union is a chain walk, not two hardcoded branches.**
   Expressed as: "for each level above and including Job, does the user hold
   a qualifying grant there" - resolve top-down, stop at the first hit,
   dedupe by job. Concretely today that's still two checks (workspace
   `job.view_all` role, direct job-scope grant) because that's all the chain
   has - but written as a loop/resolver over the chain list, not two
   independently-hand-written LINQ branches, so a third level is one more
   iteration, not new code.
3. **The dashboard's Jobs-view sub-filter list ("access type") is populated
   from whatever `AccessType` values are actually present in the fetched
   data, not a hardcoded two-item toggle.** An Org value showing up later
   just becomes a third filter chip with no UI code change.

Everything else in this feature (the `/app/job/:jobId` route, the guard, the
two-section dashboard layout) is already level-count-agnostic as designed -
they operate on "does this job have a level above it the user can't see" as a
boolean, which holds regardless of how many levels exist above Job.

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
