# Policy-Driven Access Chaining — Design

## Problem

`UserAccess(UserId, RoleId, ScopeType, ScopeId)` is flat today — no relationship between a Job-scope grant and the Workspace it lives under. Assigning someone to a Job never touches Workspace access, and there's no way to require it for some roles but not others without hardcoding a switch statement per role name.

Need: assigning a role at one scope can require presence of a (different, least-privilege) role at ancestor scopes, driven by data so the rule can change — or a new hierarchy level (Organization, later) can be added — without touching the engine code.

## Scope hierarchy today

`Workspace` → `Job` (single level). `Organization` is planned above `Workspace` but out of scope for this work — the design must not require code changes when it arrives.

## Entities

```
ScopeParentType
  ScopeType         string (PK)              e.g. "Job"
  ParentScopeType    string? (nullable)       e.g. "Workspace" (null = root)

AssignmentPolicy
  Id            Guid (PK)
  Name          string
  RulesJson     string                        see shape below

Role  (existing, modified)
  ...existing fields unchanged...
  PolicyId      Guid  (FK -> AssignmentPolicy, required)
  -- no RoleType field added; policy alone drives chain behavior
```

Seed: `ScopeParentType` gets one row `(Job, Workspace)`. When Organization ships, one more row `(Workspace, Organization)` — no other change.

## Policy JSON shape

```json
{
  "ancestors": [
    { "scopeType": "Workspace", "grantRoleId": "<WorkspaceMemberRoleId>" }
  ]
}
```

`ancestors` is an ordered list, outermost-first is nearest ancestor. Empty list = SingleScope (Job-only, today's Client/Finance behavior). One entry = today's chain need (grant `WorkspaceMember` at the Workspace). A future Org-aware policy adds a second entry — no engine change.

Seed two policies for v1:
- **SingleScope** (`ancestors: []`) — assigned to Client, Finance. Preserves current job-scoped-billing behavior (deliberately no workspace membership) — do not regress this.
- **FullChain** (`ancestors: [{Workspace, WorkspaceMemberRoleId}]`) — assigned to Admin, Manager, Surveyor.

New system role: **WorkspaceMember** — least-privilege, view-only permission (`workspace.view`), `IsSystem = true`, its own `PolicyId` = SingleScope (it never itself triggers further chaining).

## Scope ID resolvers (two small registries, DI-registered)

```csharp
// Job's actual parent Workspace ID — one entry per non-root scope type.
Dictionary<string, Func<Guid, IServiceProvider, Task<Guid?>>> parentIdResolvers = new() {
  ["Job"] = async (jobId, sp) => (await sp.GetRequiredService<ApplicationDbContext>().Jobs.FindAsync(jobId))?.WorkspaceId,
};

// Reverse: all child-scope IDs of a given type living under a parent scope ID.
// Needed for cascade delete (see below).
Dictionary<string, Func<Guid, IServiceProvider, Task<List<Guid>>>> childIdsResolvers = new() {
  ["Workspace"] = async (workspaceId, sp) => await sp.GetRequiredService<ApplicationDbContext>()
      .Jobs.Where(j => j.WorkspaceId == workspaceId).Select(j => j.Id).ToListAsync(),
};
```

Adding Organization: one more entry in each dictionary. Nothing else in the engine changes.

## Assignment engine — extends the existing choke point, not a parallel service

`UserAccessGrantService` (`api/src/SurveyorLedger.API/Services/UserAccessGrantService.cs`) is **already** the single place every grant/revoke in the codebase goes through — `JobService.AddParticipantAsync`, `InvitationService`, `WorkspaceController.RemoveMember` all call its `GrantAsync`/`RevokeAsync`. Chaining is added inside this service. No call site changes needed — every existing caller gets chaining for free.

`GrantAsync(userId, roleId, scopeType, scopeId, assignedBy)` keeps its exact signature and existing dedupe/reactivate behavior. Add one step at the end, after the target row is resolved (new or reactivated):

```csharp
await EnsureAncestorChainAsync(userId, role, scopeType, scopeId, assignedBy);
return access;
```

```
EnsureAncestorChainAsync(userId, role, scopeType, scopeId, assignedBy):
    policy = LoadPolicy(role.PolicyId)
    scope, curScopeType = scopeId, scopeType
    foreach ancestorStep in policy.ancestors (nearest-first):
        parentId = parentIdResolvers[curScopeType](scope)
        if parentId == null: break
        hasAny = any active UserAccess row for (userId, ancestorStep.scopeType, parentId) — ANY role, not just ancestorStep's role
        if !hasAny:
            await GrantAsync(userId, ancestorStep.grantRoleId, ancestorStep.scopeType, parentId, assignedBy)
            // recursive: this call re-enters EnsureAncestorChainAsync for the granted ancestor
            // role too, so a future Org-aware WorkspaceMember policy chains further for free.
        scope, curScopeType = parentId, ancestorStep.scopeType
```

The "has no active UserAccess at parent" check is scope-presence, not role-match — if the user is already Admin at that Workspace, don't also stack WorkspaceMember on top.

**Concurrency**: unique index `(UserId, RoleId, ScopeType, ScopeId) WHERE IsActive = 1` on UserAccess. `GrantAsync`'s existing dedupe query already protects the common case; the recursive ancestor call reuses the same method, same protection.

## Removal — cascade

Extend `RevokeAsync(userId, scopeType, scopeId, roleId = null)`. After the existing soft-revoke loop:

```
childIdsResolvers[scopeType]?.Invoke(scopeId) — if present:
    foreach childId in child scope ids:
        await RevokeAsync(userId, childScopeType, childId)   // recursive, cascades multiple levels once Org exists
```

Only runs when `roleId == null` (full removal from that scope) — revoking a single role at a scope while the user keeps another role there should not cascade.

No prevent-removal option in v1 — cascade only.

## Policy changes over time

Changing `Role.PolicyId` (or a policy's `RulesJson`) affects new `GrantAsync` calls only. Existing `UserAccess` rows are never retroactively migrated. No background job in v1.

## Call sites

None need to change. `JobService.AddParticipantAsync`, `InvitationService`'s grant paths, and `WorkspaceController.RemoveMember` already call `IUserAccessGrantService.GrantAsync`/`RevokeAsync` — chaining and cascade activate transparently underneath them.

## Migration / backfill

1. New migration: `ScopeParentType`, `AssignmentPolicy` tables, `Role.PolicyId` (FK, required), unique index on `UserAccess`.
2. Seed: `ScopeParentType(Job, Workspace)`, policies `SingleScope`/`FullChain`, role `WorkspaceMember` + its `RolePermission` (view-only) + `RoleScope(Workspace)`.
3. Backfill existing roles' `PolicyId`: Client/Finance → SingleScope, Admin/Manager/Surveyor → FullChain.

## Out of scope (this work)

- Organization scope itself — only the extension points (registries, hierarchy table) are guaranteed ready for it.
- Auto-migrating existing UserAccess rows when a policy changes.
- Blocking-removal mode (only cascade is implemented).
