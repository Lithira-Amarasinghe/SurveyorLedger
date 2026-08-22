# Organization switching — design

## Purpose

Build the UI for the organization layer (backend already shipped): an Azure-style
tenant switcher so a user working across multiple organizations can pick which one
they're operating in, have that choice remembered, and see everything (dashboard,
workspaces, jobs) scoped to it. Also close a gap discovered during design: invited
users currently never get organization membership at all, breaking the invariant
"every user belongs to at least one org."

## Invariant: every user belongs to ≥1 organization

**Backend additions (small, on top of the already-shipped org layer):**

1. **`InvitationService.GrantAndMarkAcceptedAsync`** — after the existing
   workspace/job role grant (unchanged, still gated by the role's assignment
   policy), unconditionally also grants `OrgMember` at the workspace's
   `OrganizationId`, regardless of role or policy. This is deliberate: workspace
   access stays internal and policy-gated (Client/Finance still never get
   `WorkspaceMember` — that boundary is unchanged and intentional), but org
   membership is just "which company are you working for," which an external
   party already knows. Skip the grant if the user already holds `OrgMember` on
   that org.
2. **`OrganizationBackfillService`** — extended with a second pass for
   pre-existing data: for every active Workspace-scope or Job-scope `UserAccess`
   row whose user has no Organization-scope grant on that workspace's org yet,
   grant `OrgMember`. Same idempotent, no-op-forever-after pattern as the
   existing owner-org backfill pass; runs in the same startup hook.
3. **Net effect:** every user is guaranteed ≥1 org from this point forward. The
   UI never needs a "no org" fallback path — single code path throughout.

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

- Backend: invite-accept test confirming `OrgMember` granted regardless of
  role policy (Client role case specifically — asserts no `WorkspaceMember`
  grant, but `OrgMember` present). Backfill test for pre-existing
  workspace/job-scope-only members.
- Frontend: `CurrentOrganizationService` persistence/restore (valid id,
  stale/invalid id, empty storage). Dashboard filtering by active org for both
  workspace grid and direct-access jobs.
