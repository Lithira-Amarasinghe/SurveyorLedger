# Person / UserAccount split

## Context

`User` today conflates two different concepts: a real-world identity (name,
email, phone, address) and a login credential (password hash, verification
state, lockout counters). This blocks a real need - a billing `Client` today
is a wholly separate entity from `User`, so the same real person known as a
client can't later be invited to a job without re-entering their identity as
a second, disconnected row.

This spec splits `User` into `Person` (the "who") and `UserAccount` (the
"how they sign in"), retires the standalone `Client` entity into `Person`,
and repoints every existing FK to whichever half it actually means. A
follow-up spec (job-scoped billing redesign - `Invoice`/`Quotation` keyed to
`JobId`, a billing-only role, email delivery) builds on this but is out of
scope here; this spec only needs `Invoice.ClientId` to point somewhere valid,
not to solve billing access control.

Dev-only database - no real data to preserve, so this is a clean-slate
migration (drop and regenerate), not a backfill.

## Decisions

- **`Person`**: `Id, FirstName, LastName, Email, Phone, Address, IsActive,
  CreatedAt, UpdatedAt`. Global identity, no `WorkspaceId` - the same real
  person can be a billing client of one workspace and a job participant of
  another without duplicating their name/address.
- **`UserAccount`**: `Id, PersonId (FK, required, unique), PasswordHash,
  EmailVerified, EmailVerifiedAt, HasCompletedSignup, FailedLoginAttempts,
  LockoutEndsAt, IsActive, CreatedAt, UpdatedAt`. A `Person` may have zero or
  one `UserAccount`. `Email` lives on `Person` only - `UserAccount` login
  lookups join through `Person.Email`.
- **`Client` entity deleted.** `Invoice.ClientId` / `Quotation.ClientId`
  point straight at `Person.Id` now. This is a transitional shape - the
  billing follow-up spec will properly scope invoice access through
  `JobId`, the same way every other job-scoped resource already works. This
  spec's job is only to not lose the identity data or break the build.
- **Split rule for every other FK**: anything that means "this real person
  is associated with this business record" (creator, uploader, payee,
  requester, invitee) → `Person`. Anything that is inherently about a login
  session or a permission grant → `UserAccount`.
- **No behavior change to the invitation flow.** Today, `CreateScopedInvitationAsync`
  eagerly creates a `User` row with `PasswordHash = null` so the invitee has
  somewhere to attach a job-scope grant to before they've signed up. Under
  the split this becomes: eagerly create a `Person`, no `UserAccount` yet.
  `UserAccess.UserId` can only point at a real `UserAccount`, so a job-scope
  grant now requires one to exist first - but tracing the actual accept flow
  shows this was already true: `AcceptInvitationAsync`/`GrantAndMarkAcceptedAsync`
  only ever runs after `CompleteInvitationAsync` (password set), and the
  instant-grant path (`HasConsentCoverageAsync`) only fires for someone who
  already holds an active `UserAccess` row elsewhere, which itself could only
  have been created through that same accept-after-password-set path. There
  is no code path today where a grant is created before a password exists.
- **Casbin subject id and JWT `NameIdentifier` become `UserAccount.Id`** -
  both are inherently about "who is authenticated right now", never about
  the underlying person.

## FK repointing (full sweep)

| Entity.Field | Today | Becomes |
|---|---|---|
| `UserAccess.UserId` | `User` | `UserAccount` |
| `AuthToken.UserId` | `User` | `UserAccount` |
| `AuditLog.UserId` (nullable) | `User` | `UserAccount` |
| `Workspace.Owner`/`OwnerId` | `User` | `UserAccount` |
| `Invitation.UserId` (invitee) | `User` | `Person` |
| `Invitation.InvitedByUser`/`InvitedByUserId` | `User` | `UserAccount` (inviter is always already logged in) |
| `Job.CreatedBy` | `User` (via `CreatedByUser`) | `Person` |
| `Milestone.CreatedByUser` | `User` | `Person` |
| `Expense.RecordedByUser` | `User` | `Person` |
| `Document.UploadedByUser` | `User` | `Person` |
| `LandPhoto.UploadedByUser` | `User` | `Person` |
| `DocumentRequest.RequestedByUser` | `User` | `Person` |
| `DocumentRequest.FulfilledByUser` (nullable) | `User` | `Person` |
| `DocumentRequest.TargetUser`/`TargetUserId` (nullable) | `User` | `Person` |
| `StaffPayment.UserId`/`User` (payee) | `User` | `Person` |
| `StaffPayment.RecordedByUser` | `User` | `Person` |
| `Invoice.ClientId` | `Client` | `Person` (transitional - see Decisions) |
| `Quotation.ClientId` | `Client` | `Person` (transitional - see Decisions) |

Every controller's `CallerId()` helper (`Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!)`)
now resolves a `UserAccount.Id`. Anywhere that id is used to look up
"the caller's name/email for display" needs an added join through
`UserAccount.Person`.

## Backend

- New entities `Person`, `UserAccount` (`SurveyorLedger.Data/Entities/`),
  configurations mirroring today's `UserConfiguration` (unique index on
  `Person.Email`, unique index on `UserAccount.PersonId`).
- Delete `User.cs`, `Client.cs`, their configurations.
- `AuthService`: every `_context.Users` query becomes a `UserAccount`
  query joined to `Person` (login lookup by email goes through
  `Person.Email` → `UserAccount`). Registration creates both rows in one
  transaction. `VerifyOtpAsync`'s `HasCompletedSignup` set stays on
  `UserAccount`.
- `InvitationService`: `CreateScopedInvitationAsync`'s "not found" branch
  creates a `Person` only. `CompleteInvitationAsync` creates the
  `UserAccount` for that `Person` (this is the one new codepath - today it
  updated the eagerly-created `User` in place; now it inserts a new
  `UserAccount` row alongside the existing `Person`).
- `ScopedAccessService`, `WorkspaceService`, `JobService`, `CasbinService`:
  every `userId` parameter now means `UserAccount.Id` - no signature
  changes, just what the `Guid` refers to. Everywhere these services need a
  display name, resolve through `UserAccount.Person`.
- `GrantAsync`/`UserAccessGrantService`: `UserAccess.UserId` foreign key
  target changes to `UserAccount` - Casbin's `g(user, role, scope)` grouping
  still keys off `.ToString()` of that same id, so Casbin itself needs no
  change.
- Every DTO/response currently exposing `FirstName`/`LastName`/`Email` off a
  `User` join now joins through `Person` instead (mechanical - same shape,
  different source table).

## Migration

Single EF Core migration (`dotnet ef migrations add SplitUserIntoPersonAndUserAccount`),
generated normally, not hand-edited. Since this is dev-only data, the
migration drops the old `Users`/`Clients` tables and FKs rather than
attempting an in-place data migration - LocalDB gets reset and reseeded
after applying.

## Out of scope

- Billing/invoice redesign (`Invoice`/`Quotation.JobId` required, `Client`'s
  workspace-scoping replacement, new billing-only role, email delivery to
  `Person`s without a `UserAccount`) - separate spec, builds on `Person` as
  the identity primitive established here.
- Any UI change beyond what's needed to keep the app compiling against the
  renamed types (person pickers, display name lookups) - a dedicated pass on
  UI copy/labels for "Person" vs "Account" is not part of this spec.
