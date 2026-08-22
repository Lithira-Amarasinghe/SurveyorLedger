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
adding a special-case grant in `InvitationService`.

**A real conflict surfaced while designing this, worth recording:** `Surveyor` isn't
only granted at Job scope — `WorkspaceService.AddMemberRoleAsync` also grants it
*directly at Workspace scope* (a workspace member invited straight in as Surveyor,
no job). The original single-hop `FullChain` policy's sequential ancestor array
(`[{"scopeType":"Workspace","grantRoleId":...}]`) only worked for both starting
depths *by accident*: `GetParentIdAsync("Workspace", ...)` returned `null` (no
provider registered for Workspace's parent), so a Workspace-start grant's ancestor
walk broke immediately and harmlessly. Once a Workspace→Organization provider
exists, that same call stops returning `null` — a naive extension of the array
would then try to grant `WorkspaceMemberRoleId` using the *organization's* guid as
the scope id, corrupting `UserAccess` data for every Surveyor granted directly at
Workspace scope (a path several existing tests exercise). **Fix:** the policy shape
changes from a sequential array to a scope-type-keyed map, and the walk resolves
the *real* parent scope type at each hop (via `IScopeIdResolver.GetParentScopeType`,
which already exists) rather than trusting a fixed array position — self-adjusting
for either starting depth.

**Three additions to the engine:**

1. **`ScopeParentType` row**: `{ ScopeType: "Workspace", ParentScopeType:
   "Organization" }`.
2. **New `IScopeLinkProvider`**: `WorkspaceOrganizationScopeLinkProvider`
   (`ChildScopeType = "Workspace"`, `ParentScopeType = "Organization"`),
   resolving via `Workspace.OrganizationId` — same shape as
   `JobWorkspaceScopeLinkProvider`.
3. **`AssignmentPolicy.RulesJson` schema change**: `{"ancestors":[...]}` (array,
   position-dependent) becomes `{"grants":{"<ScopeType>": "<RoleId or omitted>"}}`
   (map, keyed by the actual scope type reached). The walk now always follows the
   *real* hierarchy (`GetParentScopeType`/`GetParentIdAsync`) all the way to its
   top, granting only at scope types present in the map; a scope type absent from
   the map is a transit hop — resolved to keep walking, nothing granted there.
   This is a breaking schema-shape change (not additive), but every existing
   policy row is reseeded in the same migration, so no stale-shape row survives.

**Policy reassignment (all seeded, no app code reads `RulesJson` directly except
the two engine methods above):**

| Policy | RulesJson (`grants` map) | Roles |
|---|---|---|
| `SingleScope` | `{}` (empty) | OrgOwner, OrgMember — top of the hierarchy, no ancestor to reach |
| `FullChain` | `{"Workspace": WorkspaceMemberRoleId, "Organization": OrgMemberRoleId}` | Admin, Surveyor, Member, **WorkspaceMember** (was `SingleScope` — see below) |
| `OrgOnly` (new) | `{"Organization": OrgMemberRoleId}` | Client, Finance (was `SingleScope`) |

- Job-start walk (Surveyor via job assignment): Job→Workspace (grants
  `WorkspaceMember`, map has a `"Workspace"` entry) →Organization (grants
  `OrgMember`) →no further parent, stop.
- Workspace-start walk (Admin/Surveyor/Member via direct workspace grant): first
  hop resolves straight to Organization (Workspace's only registered parent) —
  the map's `"Workspace"` key is simply never looked up in this case, since a role
  never grants at its *own* starting scope, only at ancestors. Grants `OrgMember`
  directly. No corruption, no accidental double-grant.
- `WorkspaceMember` moves from `SingleScope` to `FullChain` too: whether it's
  chain-granted (from a Job-start Surveyor walk) or granted directly (an Admin
  hand-picks "WorkspaceMember" for someone at Workspace scope — a valid pick per
  `RoleScope`), its own grant now also reaches Organization. Reusing `FullChain`
  rather than adding a fourth policy works because the map only ever grants at
  *ancestors* of wherever the walk starts — never at the starting scope itself.
- Job-start walk (Client/Finance, `OrgOnly`): Job→Workspace (no `"Workspace"` key
  in the map — transit, resolves the parent id, grants nothing, so `WorkspaceMember`
  is never created) →Organization (grants `OrgMember`) →stop. Workspace internals
  stay closed to externals; org identity is just "which company you're working
  for," which an external party already knows.

**Regression guard — invitation targeting must not shift:**
`JobService.ResolveInvitationTargetAsync` uses `ResolveTopAncestorAsync` to
decide an invitation's *primary* scope (e.g. a chaining Surveyor invite
targets Workspace, not Job, with Job as the descendant). Now that the walk
reaches all the way to Organization, that primary target would silently shift
from Workspace to Organization, and `InvitationService.GetPendingInvitationsAsync`
filters pending invitations by `ScopeType == Workspace` — such an invite
would vanish from the workspace's pending-invitations list. Fix:
`ResolveTopAncestorAsync` gains an optional `stopAtScopeType` parameter —
the walk still visits every hop up to and including that scope type (so a
transit-only hop below it is still correctly skipped, same as today's `null`
result for Client/Finance), but stops immediately after reaching it, never
considering anything above. `JobService` calls it with
`stopAtScopeType: "Workspace"`. Invitation targeting behavior is unchanged
byte-for-byte. The *grant-time* walk (`GrantAncestorRolesAsync`, used on
accept) has no such cap and reaches Organization as designed — it's a
different method, unaffected.

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
- Each card has a "Manage" link to `/app/organizations/:id` — members
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
- No organization rename — the shipped `OrganizationService` has no update-name
  endpoint; adding one is a separate small backend addition, not bundled here.

## Testing

- Backend:
  - `WorkspaceOrganizationScopeLinkProvider` resolves a workspace's
    organization id correctly.
  - `GrantAncestorRolesAsync` golden path: granting `Surveyor` at Job scope
    ends up with both `WorkspaceMember` (Workspace) and `OrgMember`
    (Organization) active. Edge case: granting `Surveyor` directly at
    Workspace scope (the `WorkspaceService.AddMemberRoleAsync` path) ends up
    with only `OrgMember` — no `UserAccess` row is (re-)created at Workspace,
    since a role never grants at its own starting scope.
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
