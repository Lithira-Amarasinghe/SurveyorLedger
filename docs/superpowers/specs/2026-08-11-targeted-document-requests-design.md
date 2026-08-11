# Targeted Document Requests — Design Spec

Date: 2026-08-11

## Purpose

Let a document request be aimed at a specific role or a specific person on the job, instead of always being open to anyone assigned. Extends `docs/superpowers/specs/2026-08-11-document-requests-design.md`.

## Scope

- Adds `TargetRole` and `TargetUserId` to the existing `DocumentRequest` entity — no new table.
- Both null = open (today's behavior, unchanged).
- Mutually exclusive: at most one of the two set, enforced at both the application layer and the database layer (a CHECK constraint — see Data Model below).
- Targeting is a hard lock on fulfillment, including for Admin/Surveyor — no staff override on a targeted request, per explicit decision during brainstorming (this does trade away the "staff scans a handed-over paper document" convenience for targeted requests specifically; open requests keep that behavior unchanged).

## Data Model

### `DocumentRequest` additions (`api/src/SurveyorLedger.Data/Entities/DocumentRequest.cs`)

```csharp
public string? TargetRole { get; set; }
public Guid? TargetUserId { get; set; }
public User? TargetUser { get; set; }
```

### EF configuration (`DocumentRequestConfiguration.cs`)

```csharp
builder.Property(x => x.TargetRole).HasMaxLength(20);

builder.HasOne(x => x.TargetUser)
    .WithMany()
    .HasForeignKey(x => x.TargetUserId)
    .OnDelete(DeleteBehavior.Restrict);

// Two nullable columns following this entity's existing pattern (FulfilledDocumentId/
// FulfilledAt/FulfilledBy are already nullable-until-set the same way). The one real gap
// app-level validation alone can't close is "both set" - a bug or a direct write could
// still produce that, so it's enforced at the DB level too.
builder.ToTable(t => t.HasCheckConstraint(
    "CK_DocumentRequests_TargetExclusive",
    "[TargetRole] IS NULL OR [TargetUserId] IS NULL"));
```

Considered and rejected:
- **Discriminator + single value column** (`TargetType` enum + string `TargetValue` holding either a role name or a user-id string): fewer nulls, but loses the FK constraint on the user-id case, loses type safety, adds parse/cast logic on every read. Worse quality for fewer nulls.
- **Child table** (`DocumentRequestTarget`, 1:0..1): textbook-normalized, but adds a join to every list/fulfill call for two columns that aren't sparse enough (two optional columns, not twenty) to justify the join. Real over-engineering at this scale.

### Migration

`dotnet ef migrations add AddDocumentRequestTargeting` — adds the two columns, the FK, and the CHECK constraint.

## Validation (Create)

`DocumentRequestService.CreateAsync` gains two optional parameters, `string? targetRole`, `Guid? targetUserId`:

- Both set → `ValidationException("A request can target a role or a person, not both.")`.
- `targetRole` set → must be one of `Constants.SystemRoles` (Admin, Surveyor, Client).
- `targetUserId` set → must be an existing job participant, checked the same way `EnsureJobAccessAsync`'s assignment check already queries `UserAccesses` (`ScopeType == Job && ScopeId == jobId`) — no new query shape, reuses the existing one.

## Fulfillment Authorization

`FulfillAsync` gains the entitlement check, applied after the existing `EnsureJobAccessAsync(..., "view")` gate (unchanged — still required regardless of targeting):

```csharp
if (request.TargetUserId.HasValue && request.TargetUserId != callerUserId)
    throw new ForbiddenException("This request is for a specific person.");

if (request.TargetRole != null)
{
    var callerRole = await GetCallerRoleAsync(callerUserId, workspaceId);
    if (callerRole != request.TargetRole)
        throw new ForbiddenException($"This request is for the {request.TargetRole} role.");
}
```

`GetCallerRoleAsync` is a small private helper duplicated from `DocumentService`'s identical one (same reasoning `MilestoneService`'s doc comment already gives for its own duplication in this codebase: two call sites, a shared abstraction isn't justified yet) — resolves the caller's role from `UserAccess.Role.Name` at workspace scope.

Open requests (both null) skip both checks — unchanged behavior, any job-assigned caller including staff-on-behalf-of-client.

## API

`DocumentRequestCreateRequest` gains `string? TargetRole` and `Guid? TargetUserId`. `DocumentRequestResponse` gains the same two fields (plus nothing else — the fulfillment lock is enforced server-side on the fulfill call itself, not exposed as a separate "can I fulfill this" flag, since that would just be re-deriving the same rule the fulfill endpoint already enforces).

## UI

- Request-creation form gets a "Target" selector: *Anyone* (default) / *By role* (dropdown: Admin, Surveyor, Client) / *Specific person* (dropdown sourced from `participants()`, already loaded on the page — no new lookup widget).
- Each pending request row shows a small target badge when set: "for Client" or "for {firstName} {lastName}".
- The Upload button stays visible to everyone on every pending row, targeted or not. If the caller isn't entitled, the backend rejects and the message ("This request is for a specific person." / "...for the Client role.") surfaces through the existing `documentError` signal — same error-handling path every other action on this page already uses. No client-side current-user-id plumbing needed; the backend is the actual authority, and building a parallel client-side entitlement check would just be logic to keep in sync with the server rule for a friendlier button state, not a security need.

## Testing

Service tests (extending `DocumentRequestServiceTests.cs`):
- Create with both `targetRole` and `targetUserId` set → `ValidationException`.
- Create with `targetUserId` pointing at a non-participant → `ValidationException`.
- Fulfill a `TargetRole`-locked request as the wrong role → `ForbiddenException`, including as Admin.
- Fulfill a `TargetUserId`-locked request as a different user → `ForbiddenException`, including as Admin/Surveyor.
- Fulfill a targeted request as the correct target → succeeds.
- Open request (no targeting) → unchanged existing behavior, still fulfillable by any job-assigned caller.

## Out of Scope (v1)

- Notifying the targeted person (matches the parent Document Requests spec's existing "no notifications in v1" decision).
- Multi-target (e.g. "any of these two people") — single role or single person only.
