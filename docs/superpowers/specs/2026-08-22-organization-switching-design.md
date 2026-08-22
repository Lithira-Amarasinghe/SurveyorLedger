# Organization switching — design

## Purpose

Build the UI for the organization layer (backend already shipped): an Azure-style
tenant switcher so a user working across multiple organizations can pick which one
they're operating in, have that choice remembered, and see everything (dashboard,
workspaces, jobs) scoped to it. Also close a gap discovered during design: invited
users currently never get organization membership at all, breaking the invariant
"every user belongs to at least one org."

## Invariant: every user belongs to ≥1 organization

The codebase already has a declarative engine for exactly this — an
`AssignmentPolicy.RulesJson` ancestor chain, walked by
`UserAccessGrantService.GrantAncestorRolesAsync`/`ResolveTopAncestorAsync`, resolving
parent scopes through registered `IScopeLinkProvider`s (see
`JobWorkspaceScopeLinkProvider`). `ScopeParentType.cs` has an existing comment
anticipating this exact extension: *"Adding Organization above Workspace later is one
new row here, nothing else changes."* This design reuses that engine rather than
adding a special-case grant in `InvitationService` — three additions:

1. **`ScopeParentType` row**: `{ ScopeType: "Workspace", ParentScopeType:
   "Organization" }`.
2. **New `IScopeLinkProvider`**: `WorkspaceOrganizationScopeLinkProvider`
   (`ChildScopeType = "Workspace"`, `ParentScopeType = "Organization"`),
   resolving via `Workspace.OrganizationId` — same shape as
   `JobWorkspaceScopeLinkProvider`.
3. **Policy schema gets one small, backward-compatible extension**: an
   ancestor-rule entry may now omit `grantRoleId` (or set it `null`) to mean
   "resolve this hop's parent id to keep walking the chain, but grant nothing
   here" — a *transit* hop. Existing policies are untouched (they always
   specify a real `grantRoleId`), so this is additive, not a breaking format
   change.

**Policy changes:**

- **`FullChain`** (Admin, Surveyor, Member — roles that legitimately get
  workspace-wide presence) gains a second ancestor entry:
  `[{ "scopeType": "Workspace", "grantRoleId": WorkspaceMemberRoleId }, { "scopeType": "Organization", "grantRoleId": OrgMemberRoleId }]`.
  Job→Workspace→Organization, granting at both hops.
- **New `OrgOnly` policy**, replacing `SingleScope` for Client and Finance:
  `[{ "scopeType": "Workspace", "grantRoleId": null }, { "scopeType": "Organization", "grantRoleId": OrgMemberRoleId }]`.
  Transits through Workspace (resolves the parent id, grants nothing —
  Client/Finance still never get `WorkspaceMember`, that boundary is
  unchanged and intentional) and grants only at Organization. Reasoning:
  workspace internals stay closed to externals, but org identity is just
  "which company you're working for," which an external party already knows.
  (`SingleScope` itself — `{"ancestors":[]}` — stays as-is for any future role
  that genuinely needs zero ancestor presence.)

**Regression guard — invitation targeting must not shift:**
`JobService.ResolveInvitationTargetAsync` uses `ResolveTopAncestorAsync` to
decide an invitation's *primary* scope (e.g. a chaining Surveyor invite
targets Workspace, not Job, with Job as the descendant). Naively walking the
now-longer `FullChain` array would shift that primary target from Workspace
to Organization, and `InvitationService.GetPendingInvitationsForWorkspaceAsync`
filters pending invitations by `ScopeType == Workspace` — such an invite
would silently vanish from the workspace's pending-invitations list. Fix:
`ResolveTopAncestorAsync` gains an optional `stopAtScopeType` parameter;
`JobService` calls it with `stopAtScopeType: "Workspace"`, capping the walk
exactly where it stops today. Invitation targeting behavior is unchanged
byte-for-byte. The *grant-time* walk (`GrantAncestorRolesAsync`, used on
accept) is uncapped and reaches Organization as designed — it's a different
method, not affected by the cap.

**`OrganizationBackfillService`** gets a second pass for pre-existing data:
for every active Workspace-scope or Job-scope `UserAccess` row whose user has
no Organization-scope grant on that workspace's org yet, call the same
`GrantAsync` path (which triggers `GrantAncestorRolesAsync` under the policy
above) rather than reimplementing the grant logic — reuses the exact same
code the invite-accept path uses. Idempotent, no-op-forever-after, runs in
the same startup hook as the existing owner-org backfill pass.

**Net effect:** every user is guaranteed ≥1 org from this point forward,
achieved entirely through the existing declarative RBAC engine. The UI never
needs a "no org" fallback path — single code path throughout.

## DTO additions

- `WorkspaceWithAccess` → `WorkspaceResponse`: add `organizationId` (Guid) and
  `organizationName` (string).
- `AccessibleJob` (job.service.ts's backing DTO, `GetMine` response): add
  `organizationId` so the dashboard's "Jobs (direct access)" section — the one
  Client/Finance actually see — can also be scoped to the active org.

## Frontend

### `CurrentOrganizationService` (new, mirrors `CurrentWorkspaceService`)

- Signal holding the active org: `{ organizationId, name, tier, role }`.
- `set()` persists `selectedOrganizationId` to `localStorage`.
- A route resolver on `/app` (same pattern as `workspaceResolveGuard`) runs
  once per session: reads the persisted id, validates it against
  `GET /api/organization` (still a member?), restores it if valid, else falls
  back to the first org in the list. No forced re-picker on login — restores
  silently.

### Topbar quick-switch dropdown

- Org-name button replaces the current static area next to the search bar in
  `topbar.component.ts`.
- Click opens a dropdown: every org the user belongs to (name + tier badge,
  checkmark on active), a "Create organization" row, a "Manage organizations"
  link to the full page.
- Selecting an org calls `CurrentOrganizationService.set()` and navigates to
  `/app/dashboard`.

### `/app/organizations` page (new)

- Grid like the existing dashboard workspace grid: name, tier, workspace
  count/limit (`workspaceCount`/`maxWorkspaces` from `OrganizationInfo`), role.
  Click switches (same as topbar) and navigates to dashboard.
- "New organization" button — name field, `POST /api/organization`.
- Each card has a "Manage" link to `/app/organizations/:id` — rename, members
  list (add/remove via existing `GetMembersAsync`/`AddMemberAsync`/
  `RemoveMemberAsync`), subscription tier display + change (`PUT
  /api/organization/{id}/subscription`). Mirrors the existing workspace
  settings/members pages, much smaller (only Owner/Member roles, no job/land
  concepts).

### Dashboard scoping

- Workspace grid filters to `workspace.organizationId === currentOrg.id`.
- "Jobs (direct access)" section filters to `job.organizationId ===
  currentOrg.id` (covers Client/Finance, who have no workspace access at all).
- "New workspace" button passes the active org into the create modal.

### Create-workspace modal extension

- Tier dropdown removed (dead field against the current API contract — tier
  now lives on the organization, not the workspace).
- Org picker added at the top: dropdown of the user's orgs (defaulting to the
  active one) plus "+ Create new organization" revealing an inline name field.
  Submit creates the org first if new, then the workspace under it — one form,
  one submit.

## Out of scope

- No `OrgAdmin` role UI (backend doesn't have it either — Owner/Member only).
- No payment gateway UI — subscription tier change stays an admin field-flip.
- No change to how Client/Finance job-scope access itself works — only the
  org-membership grant is new for them.

## Testing

- Backend:
  - `WorkspaceOrganizationScopeLinkProvider` resolves a workspace's
    organization id correctly.
  - `GrantAncestorRolesAsync` golden path: granting `Surveyor` at Job scope
    ends up with both `WorkspaceMember` (Workspace) and `OrgMember`
    (Organization) active. Edge case: a transit hop (`grantRoleId: null`)
    resolves the parent without creating a `UserAccess` row there.
  - Client invite-accept: `OrgMember` granted at Organization, **no**
    `WorkspaceMember` row created at Workspace — the two-boundary guarantee.
  - `ResolveTopAncestorAsync` with `stopAtScopeType: "Workspace"` still
    returns the Workspace-level ancestor for a chaining role (unchanged from
    today), not Organization — regression guard for invitation targeting.
  - `GetPendingInvitationsForWorkspaceAsync` still lists a chaining role's
    pending invite (confirms the targeting regression didn't happen).
  - Backfill: pre-existing workspace/job-scope-only members without an org
    grant get one on the next startup pass.
- Frontend: `CurrentOrganizationService` persistence/restore (valid id,
  stale/invalid id, empty storage). Dashboard filtering by active org for both
  workspace grid and direct-access jobs.
