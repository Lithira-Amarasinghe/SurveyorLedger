# Milestone Feature — Design Spec

Date: 2026-08-11
Status: Approved

## Purpose

Track survey progress within a Job as a sequence of milestones (e.g. Site Visit,
Survey Complete, Deed Verified, Handover). Admin/Manager/Surveyor create and manage
them; Client sees them read-only, scoped to jobs the client is linked to.

## Scope

Backend only (API, DB, service logic). No UI in this pass.

## Entity: `Milestone`

New file `api/src/SurveyorLedger.Data/Entities/Milestone.cs`, modeled on the existing
`LandSurvey` "historical record hanging off a parent" pattern.

```csharp
public class Milestone
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string Title { get; set; }
    public string? Description { get; set; }
    public DateTime? DueDate { get; set; }
    public string Status { get; set; } = "Pending"; // Pending | InProgress | Completed
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedBy { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Job Job { get; set; }
    public User CreatedByUser { get; set; }
    public User? CompletedByUser { get; set; }
}
```

No `WorkspaceId` column — tenant isolation is transitive through `JobId → Job.WorkspaceId`,
identical to how `LandSurvey` relies on `LandId → Land.WorkspaceId`. Every query loads via
`FindJobAsync(workspaceId, jobId)` first, which already enforces the workspace boundary.

Soft delete via `IsActive`, matching `Job`/`JobLand` convention (not a hard delete).

`Status` is a plain string, not an enum column, matching `Job.Status` — no DB-level enum
type exists elsewhere in this schema, so introducing one here would be inconsistent.

## EF Configuration

New `MilestoneConfiguration : IEntityTypeConfiguration<Milestone>` under
`api/src/SurveyorLedger.Data/Configurations/`, following `LandSurveyConfiguration`'s shape:
- `HasKey(x => x.Id)`
- `Title` required, max length 200 (matches `Job.Title`)
- `Status` required, max length 20
- FK to `Job` with `OnDelete(DeleteBehavior.Cascade)` (milestones die with their job,
  same as `JobLand`)
- FK to `CreatedByUser`/`CompletedByUser` with `OnDelete(DeleteBehavior.Restrict)`
  (matches `Job.CreatedByUser` — don't cascade-delete a milestone because a user record
  changes)
- Index on `JobId` (list-by-job is the only query pattern)

Register in `ApplicationDbContext`: `DbSet<Milestone> Milestones` + apply configuration,
same pattern as existing `DbSet<LandSurvey>`.

## Migration

One EF Core migration (`dotnet ef migrations add AddMilestones`), generated — never
hand-edited. Adds the `Milestones` table and its FKs only. **No** Permission/RolePermission
seed migration — see Access Control below for why none is needed.

## Access Control — reuses existing `job` permissions, zero new rows

Per updated scope (see "Remove Manager role" below), only three roles remain: Admin,
Surveyor, Client.
- `job.edit` is granted to Admin, Surveyor (not Client)
- `job.view` is granted to Admin, Surveyor, Client

This already matches the requirement exactly ("admin and surveyor can create milestones,
client only views"). No new `Permission`/`RolePermission` seed data is needed.

Milestone actions map to job actions:
| Milestone action | Required permission | Note |
|---|---|---|
| List / Get | `job.view` | |
| Create | `job.edit` | "Edit jobs, participants, and land links" already covers job sub-resources |
| Update (title/desc/due date) | `job.edit` | |
| Update status (incl. complete) | `job.edit` | |
| Delete (soft) | `job.edit` | |

Job-level assignment check is reused unchanged: unless the caller holds `job.view_all`
(Admin/Manager), they must hold a job-scoped `UserAccess` row for that specific `jobId`.
This is what makes "client sees milestones only for jobs they're linked to" fall out for
free — it's the exact same rule `JobService.EnsureJobAccessAsync` already applies to job
view/edit/delete, applied at the milestone layer too.

## Service: `MilestoneService`

New `api/src/SurveyorLedger.API/Services/MilestoneService.cs`, same shape as `JobService`.

```csharp
public interface IMilestoneService
{
    Task<List<Milestone>> GetMilestonesAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
    Task<Milestone> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId);
    Task<Milestone> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, MilestoneRequest request);
    Task<Milestone> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, MilestoneRequest request);
    Task<Milestone> UpdateStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, string status);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId);
}
```

Each method:
1. `FindJobAsync(workspaceId, jobId)` — 404 if job doesn't exist in this workspace (reuses
   the same private helper shape `JobService` has; duplicated here rather than extracted
   to a shared base, since it's ~4 lines and a shared base class for two services isn't
   justified yet).
2. `EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view"|"edit")` — duplicated
   from `JobService` (same ~10-line method). Not extracted to a shared helper class in
   this pass: only two call sites exist post-change, and premature extraction for two
   users adds an interface/DI registration for no real reuse benefit yet. Revisit if a
   third job-sub-resource service appears.
3. Load/mutate `Milestone`, always filtered by `JobId == jobId && IsActive`.

`UpdateStatusAsync`: when `status == "Completed"`, stamp `CompletedAt = UtcNow` and
`CompletedBy = callerUserId`. When status changes away from `Completed` back to
`Pending`/`InProgress`, clear both fields back to null (reopening a milestone shouldn't
leave stale completion metadata).

`DeleteAsync`: soft delete (`IsActive = false`), matching `Job`/`JobLand`.

No milestone-number generation (unlike `Job.JobNumber`) — milestones aren't referenced
externally by a human-readable code, so skip that machinery entirely.

## Validation

`MilestoneRequest.Title` required, trimmed, max 200 chars — same rule `JobRequest.Title`
already applies, enforced the same way (trim in service, `[Required]`/`[MaxLength]` data
annotation on the DTO for the model-binding-level check).

`Status` on `UpdateStatusAsync` must be one of `Pending`/`InProgress`/`Completed` — reject
anything else with a 400 (`AppException`), mirroring how `Job.Status` values are validated
today (check `JobService.UpdateStatusAsync` / `JobStatusRequest` for the exact existing
pattern and match it, including whatever status whitelist mechanism already exists there —
add one if it doesn't).

## API: `MilestoneController`

New `api/src/SurveyorLedger.API/Controllers/MilestoneController.cs`, nested under job,
matching `JobController`'s route style:

```
[Route("api/workspace/{workspaceId}/job/{jobId}/milestone")]

GET    /                     List
GET    /{id}                 Get
POST   /                     Create
PUT    /{id}                 Update
PUT    /{id}/status          Update status
DELETE /{id}                 Delete
```

DTOs in `api/src/SurveyorLedger.API/Models/Milestone/`: `MilestoneRequest`,
`MilestoneStatusRequest` (mirrors `JobStatusRequest`), `MilestoneResponse` — same
shape/placement as the existing `Models/Job/` folder.

`CallerId()` extraction and `ApiResponse<T>` wrapping follow `JobController` exactly —
no new response envelope.

## Testing

One test file `api/tests/SurveyorLedger.API.Tests/Services/MilestoneServiceTests.cs`
covering the access-control matrix (the part with real risk of a bug):
- Admin/Manager/Surveyor with job.edit can create/update/delete
- Client (job.view only) gets 403 on create/update/delete
- Client assigned to the job can view; client NOT assigned to the job gets 403 on view
- Admin/Manager (job.view_all) can view any job's milestones without an assignment row
- Completing a milestone stamps CompletedAt/CompletedBy; reopening clears them
- Cross-workspace jobId returns 404, not a milestone from another tenant

No controller-level tests — `JobController` has none either; the service layer is where
this codebase puts its test weight.

## Remove Manager role (bundled RBAC cleanup)

Not milestone-specific, but requested alongside this work: the Manager role is unused
in practice and should be removed from the system entirely, leaving Admin, Surveyor,
Client as the only workspace roles.

**Touch points found (grep across `api/src`):**
- `SurveyorLedger.Core/Constants.cs` — `SystemRoles.Manager` constant → remove.
- `RoleConfiguration.cs` — `ManagerRoleId` static + its `Role` seed row (`HasData`) →
  remove.
- `RolePermissionConfiguration.cs` — every `Grant(..., RoleConfiguration.ManagerRoleId, ...)`
  line (workspace/land/job/client permissions + `job.view_all`) → remove.
- `Models/Workspace/UpdateMemberRoleRequest.cs` and `Models/Invitation/InvitationRequest.cs`
  — `[RegularExpression("^(Admin|Manager|Surveyor|Client)$", ...)]` → drop `Manager` from
  the pattern and the error message.
- Comments referencing "Admin/Manager" in `JobService.cs`, `WorkspaceService.cs`,
  `InvitationService.cs` — reword to "Admin" only where they describe who holds
  `job.view_all` (Admin becomes the sole holder).
- `MemberResponse.cs` doc-comment example — reword away from "e.g. Admin/Manager".

**Migration** (separate from `AddMilestones`, applied first): delete seed data only —
`RolePermission` rows where `RoleId = ManagerRoleId`, then the `Role` row itself
(`00000000-0000-0000-0000-000000000002`). Generated via `dotnet ef migrations add
RemoveManagerRole`, not hand-written, per project migration rules.

**Existing data checked:** one `UserAccess` row references `RoleId = ManagerRoleId`
(`F14EA6F7-CF89-4987-B878-766DC5146282`, `regressiontest@example.com`, `IsActive = 0`,
`ScopeType = Workspace`). Inactive test data, not a live member — the removal migration
deletes this row (by Id) before deleting `RolePermission` rows and the `Role` row itself,
so the FK chain (`UserAccesses.RoleId → Roles.Id`) doesn't block. No `Invitation` rows
reference Manager (checked, zero rows).

**Job permission table after removal:**
| Role | job.view | job.edit | job.delete | job.view_all |
|---|---|---|---|---|
| Admin | ✓ | ✓ | ✓ | ✓ |
| Surveyor | ✓ | ✓ | ✗ | ✗ |
| Client | ✓ | ✗ | ✗ | ✗ |

## Out of scope (explicitly deferred)

- UI (Angular pages) — separate pass per project's Phase 2 UI convention
- Milestone ordering/sequence field — no requirement for a fixed sequence yet
- Milestone-specific participant/assignee — job-level assignment already scopes visibility
- Milestone templates / auto-creation on job creation — no request for this
- Notifications on milestone completion — no request for this
