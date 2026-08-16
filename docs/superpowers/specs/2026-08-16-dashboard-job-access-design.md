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
  narrow further by workspace, status, and access scope type (`Workspace` /
  `Job`, whatever values are actually present - see "Scaling mechanism").
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
hardcoded switch) earlier this session.

**Definition, stated once, reused everywhere below: a "qualifying grant" at
scope level L means the user holds a role at L whose permissions include
viewing this job** (`job.view_all` at a level above Job, or simply existing
at Job level, since every Job-scope role - Surveyor, Client - already
carries `job:view` on its own grant). This is deliberately *not* "holds any
`UserAccess` row at L" - a workspace `Member` role has a workspace-scope row
but no `job.view_all`, so it does not qualify a job as Workspace-level
access unless the user also holds `job.view_all` specifically. Getting this
wrong would either hide jobs a real member can see, or claim access to jobs
they can't actually open - either way, this single definition is the thing
every level's query must implement correctly, not a detail left to
per-level judgment.

1. **`AccessScopeType` is the real `Constants.ScopeTypes` value the grant was
   found at - `"Job"`, `"Workspace"`, or (later) `"Organization"` - not a
   synthetic `Member`/`JobOnly` label.** `Constants.ScopeTypes.Organization`
   already exists in `Constants.cs`, unused - this feature is the first
   consumer that gives it meaning. Reusing the real scope-type string instead
   of inventing a parallel label means there's only ever one vocabulary for
   "what level is this" in the codebase, and it's already the field returned
   everywhere else (`UserAccess.ScopeType`, `Invitation.ScopeType`, `RoleScope.ScopeType`).
2. **A declared root-to-leaf order, not an implicit one.** Add
   `Constants.ScopeHierarchy = [Organization, Workspace, Job]` (root → leaf) -
   the single place hierarchy order is written down. `GetAccessibleJobsAsync`
   walks it broadest-first: for each level, find jobs visible via a
   qualifying grant *at that level*, record them with `AccessScopeType =
   that level`, skip jobs a broader level already claimed. Concretely today
   (`Organization` skipped, no data behind it yet):
   - `Workspace`: jobs in any workspace where the user holds a role with
     `job.view_all` (the same check `WorkspaceService`/`JobService` already
     do per-workspace, just run across all workspaces).
   - `Job`: `ScopedAccessService.AccessibleJobIds(userId)` (direct job-scope
     grants), minus jobs already claimed at `Workspace`.
   Adding `Organization` later means writing the one query for "jobs visible
   via an Org-level grant" and it slots into the existing loop at its
   declared position - no change to the `Workspace`/`Job` branches, no change
   to the dedupe logic, no change to callers.
3. **The dashboard's Jobs-view "access type" filter is populated from
   whatever `AccessScopeType` values are actually present in the fetched
   data**, not a hardcoded toggle list. An `Organization` value showing up
   later is a third filter chip with no UI code change.
4. **Row routing is a leaf-vs-not-leaf rule, not a level-name check.**
   `AccessScopeType === Constants.ScopeTypes.Job` (the leaf, narrowest
   possible grant) → `/app/job/:jobId` (minimal view - nothing above Job
   confirmed accessible). Anything else (`Workspace` today, `Organization`
   later) → the existing `/app/workspace/:id/jobs/:jobId` full-shell route,
   since holding a grant at any non-leaf level already implies
   `workspace.view` under this codebase's existing role seeding (every
   workspace-scope role carries `workspace:view`). This rule needs zero
   changes when Org is added - it was never level-name-specific.

Everything else in this feature (the `/app/job/:jobId` route, the guard, the
two-section dashboard layout) is already level-count-agnostic as designed -
they operate on "does this job have a level above it the user can't see" as a
boolean, which holds regardless of how many levels exist above Job.

## Backend

**`Constants.cs`**: add
```csharp
public static readonly string[] ScopeHierarchy =
    { ScopeTypes.Organization, ScopeTypes.Workspace, ScopeTypes.Job };
```
next to the existing `ScopeTypes` class (root → leaf order, the one place
this is declared).

**New method** `ScopedAccessService.GetAccessibleJobsAsync(Guid userId)` -
homed here rather than `JobService` because every other cross-cutting access
question already lives in this service (`HasConsentCoverageAsync`,
`AccessibleJobIds`), and this method is workspace-agnostic by nature (no
`workspaceId` parameter, unlike everything on `JobService`). Returns
`List<AccessibleJob>` where:
```csharp
public record AccessibleJob(
    Guid JobId, string JobNumber, string Title, string Status,
    Guid WorkspaceId, string WorkspaceName, string AccessScopeType);
```
Implementation walks `Constants.ScopeHierarchy` broadest-first, skipping
`Organization` (no backing data yet) until that level exists. **Dedupe by
JobId as it goes - once a job is claimed at a broader level, narrower levels
never re-add or overwrite it**, so a job never appears twice and always
reports its true broadest access level:
1. **Workspace-level**: reuse the existing `job.view_all`-role-per-workspace
   check (already written 2-3 times this session in `WorkspaceService`/
   `JobService` - worth factoring into one shared helper here as part of
   this change, not just copied a fourth time) → every job in each such
   workspace, `AccessScopeType = Constants.ScopeTypes.Workspace`.
2. **Job-level**: `AccessibleJobIds(userId)` minus job ids already claimed at
   Workspace level → `AccessScopeType = Constants.ScopeTypes.Job`.

**Not a tenant-isolation violation, despite querying across workspaces**:
this endpoint is deliberately user-scoped, not workspace-scoped - every job
it returns is independently permission-checked via the level walk above, the
same category of exception `GetUserWorkspacesAsync` and `GetMyInvitationsAsync`
already are (both also query across every workspace for "what does this
specific caller have"). The hard rule from `.claude/rules.md` ("every
tenant-scoped query goes through `WorkspaceId` filtering") is about
*request-scoped* endpoints that take a workspace as context - this one
takes a user as context and filters by their access instead. Worth a code
comment at the query site saying exactly this, so a future reviewer doesn't
mistake it for a missed `WorkspaceId` filter.

**No schema/migration changes** - `ScopeHierarchy` is a code constant, no DB
column. Reuses existing indexes (`UserAccesses.UserId`, `RolePermissions.RoleId`)
for both queries - no new index needed at current scale.

**New endpoint** `GET /api/jobs/{jobId}` (not nested under `/workspace/{id}`)
- resolves the job's `WorkspaceId` internally, runs the same
`EnsureJobAccessAsync` check `JobService.GetByIdAsync` already does (already
proven to work for a job-only Client - no `workspace.view` involved), returns
the job plus its workspace name for display context. **Same 404-vs-403
behavior as the existing nested route**: unknown `jobId` → 404 (job doesn't
exist); real job, no access → 403 (exists, not yours) - matches
`JobService.GetByIdAsync`'s existing `FindJobAsync`-then-`EnsureJobAccessAsync`
order, so this new entry point doesn't introduce a second, inconsistent
information-disclosure behavior for the same resource.

**New endpoint** `GET /api/jobs/mine` - wraps `GetAccessibleJobsAsync`, backs
the dashboard's Jobs section/filtered view.

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
view): `accessScopeType === 'Job'` (leaf-level grant, nothing above
confirmed) → `/app/job/:jobId` (new route, minimal shell). Any other value
(`'Workspace'` today, `'Organization'` later) → `/app/workspace/:workspaceId/jobs/:jobId`
(existing route, full shell) - same leaf-vs-not-leaf rule from "Scaling
mechanism" above, no per-level branching in the component.

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
