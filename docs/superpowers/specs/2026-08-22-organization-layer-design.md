# Organization layer — design

## Purpose

Introduce an `Organization` entity above `Workspace` as the monetization unit. A subscription
tier attaches to the Organization and caps how many Workspaces it can hold. Backend + DB only —
no UI in this pass.

## Entities

### Organization (new)
```
Id            Guid
Name          string
OwnerId       Guid (FK -> UserAccount)
CreatedAt     DateTime
UpdatedAt     DateTime
IsActive      bool
```

### OrganizationSubscription (rename of Subscription)
```
Id              Guid
OrganizationId  Guid (FK -> Organization)   -- replaces WorkspaceId
Tier            string ("Free" | "Pro" | "Business")
Status          string ("Active" | ...)
StartDate       DateTime
EndDate         DateTime?
CreatedAt       DateTime
UpdatedAt       DateTime
```
One active subscription per Organization.

### Workspace (modified)
```
+ OrganizationId   Guid (FK -> Organization, required)
- SubscriptionTier (removed — resolve via Organization.Subscription.Tier)
```

## Tiers

Hardcoded constants (no Plan table):
```
Free      -> 1 workspace
Pro       -> 5 workspaces
Business  -> unlimited
```
Lives in `SurveyorLedger.Core.Constants` alongside `SystemRoles` / `ScopeTypes`.

## RBAC

Reuse the existing generic `UserAccess` (ScopeType/ScopeId) + Casbin pattern — no new
membership table.

- New `ScopeTypes.Organization`.
- New system roles: `OrgOwner`, `OrgMember`, seeded with `RoleScope` rows for
  `ScopeTypes.Organization`.
- `OrgOwner` permissions: `organization.manage_members`, `organization.manage_subscription`,
  `organization.create_workspace`, `organization.view`.
- `OrgMember` permissions: `organization.view` only. Org membership does NOT grant workspace
  access — that still flows through workspace-level `UserAccess` as today.

## Service changes

**New `OrganizationService` / `OrganizationController`:**
- `CreateOrganizationAsync(userId, name)` — creates org, grants caller `OrgOwner`, creates a
  Free-tier `OrganizationSubscription`.
- `GetUserOrganizationsAsync(userId)`
- `GetOrganizationAsync(orgId, callerId)`
- `AddMemberAsync` / `RemoveMemberAsync` (OrgOwner-gated, mirrors
  `WorkspaceService` member methods)
- `GetSubscriptionAsync` / `UpdateSubscriptionAsync` (tier field flip — no payment gateway
  wired up yet; flag this explicitly as a stub)

**`WorkspaceService.CreateWorkspaceAsync`:**
- Takes `organizationId`.
- Requires caller holds `organization.create_workspace` on that org (OrgOwner).
- Checks active workspace count for the org against the tier limit; throws new error code
  `WorkspaceLimitReached` (409) if exceeded.
- Drops `SubscriptionTier` from the created `Workspace`; `WorkspaceWithAccess.Tier` now reads
  through `Organization.Subscription.Tier`.

**Registration flow:**
- Insert Organization creation before Workspace creation. Name defaults to
  `"{FirstName}'s Organization"` unless caller supplies one.
- First Workspace created is attached to this new Organization; caller becomes both `OrgOwner`
  and workspace `Admin` (unchanged workspace-level behavior).

## Migration (existing data)

EF Core migration + one-time data backfill:
1. For each distinct `Workspace.OwnerId`, create one `Organization` (owned by them), grant
   `OrgOwner`.
2. Set `Workspace.OrganizationId` for every workspace owned by that user to the new org.
3. Collapse existing `Subscription` rows for those workspaces into one
   `OrganizationSubscription` — take the highest tier among them if they differ.
4. Drop `Workspace.SubscriptionTier` column and old `Subscription` table (after data moved).

## Error handling

- `WorkspaceLimitReached` (409) — creating a workspace past the org's tier cap.
- Existing `ForbiddenException` reused for non-`OrgOwner` attempting org-scoped mutations.

## Out of scope

- No payment gateway integration — tier is an admin/self-service field flip.
- No DB-driven `Plan` table — tiers are hardcoded constants.
- No `OrgAdmin` role — only Owner + Member for this pass.
- No UI — backend/DB only.

## Testing

- Migration: verify one org per existing owner, workspace count matches, tier collapsed
  correctly (highest tier wins on conflict).
- `CreateWorkspaceAsync`: golden path (under limit succeeds) + edge case (at limit throws
  `WorkspaceLimitReached`).
- RBAC: non-`OrgOwner` cannot create workspace / manage org members.
