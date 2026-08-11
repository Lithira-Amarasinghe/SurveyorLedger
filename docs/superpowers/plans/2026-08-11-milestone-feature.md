# Milestone Feature (+ Manager Role Removal) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add job-scoped Milestones (Admin/Surveyor create+manage, Client views only) to the API/DB, and remove the unused Manager role from the RBAC system.

**Architecture:** `Milestone` is a new entity FK'd to `Job` (tenant isolation transitive through `Job.WorkspaceId`, same pattern as `LandSurvey` → `Land`). `MilestoneService` reuses the existing `job.view`/`job.edit` Casbin permissions and the same job-assignment scoping rule `JobService` already applies — zero new Permission/RolePermission rows. Manager role removal is a separate, ordered-first migration deleting seed data (RolePermission grants + the Role row) plus one stray inactive test-data UserAccess row.

**Tech Stack:** .NET 9, EF Core 9 (SQL Server LocalDB), Casbin.NET 2.0, xUnit (LocalDB-backed integration tests via `WorkspaceIntegrationTestBase`).

## Global Constraints

- Migrations are generated via `dotnet ef migrations add`, never hand-edited (project rule) — exception: the one `migrationBuilder.Sql(...)` call for deleting the stray non-seeded UserAccess row, added inside the generated migration file after generation, per Task 6.
- Every tenant-scoped query goes through `WorkspaceId` filtering — for Milestone this is transitive via `Job` (`FindJobAsync(workspaceId, jobId)` first, always).
- Auth/RBAC changes (Manager removal) go through this reviewed plan — no ad hoc edits outside these tasks.
- Soft delete via `IsActive`, not hard delete, matching `Job`/`JobLand`/`Land`.
- No new dependencies — everything here uses what's already installed.

---

## Part 1 — Remove Manager Role

### Task 1: Strip Manager from constants, seed data, and validators

**Files:**
- Modify: `api/src/SurveyorLedger.Core/Constants.cs:54`
- Modify: `api/src/SurveyorLedger.Data/Configurations/RoleConfiguration.cs:12,31`
- Modify: `api/src/SurveyorLedger.Data/Configurations/RolePermissionConfiguration.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Workspace/UpdateMemberRoleRequest.cs:8`
- Modify: `api/src/SurveyorLedger.API/Models/Invitation/InvitationRequest.cs:19`
- Modify: `api/src/SurveyorLedger.API/Models/Workspace/MemberResponse.cs:22`
- Modify: `api/src/SurveyorLedger.API/Services/JobService.cs:84,275`
- Modify: `api/src/SurveyorLedger.API/Services/WorkspaceService.cs:182`

**Interfaces:**
- Consumes: nothing new.
- Produces: `RoleConfiguration` no longer exposes `ManagerRoleId`; `RolePermissionConfiguration.Configure` no longer grants anything to it. Later tasks (migration generation) depend on these edits being in place first — EF diffs the model against these configs to produce the `DeleteData` calls.

- [ ] **Step 1: Remove the `Manager` constant**

In `api/src/SurveyorLedger.Core/Constants.cs`, delete this line from `SystemRoles`:

```csharp
        public const string Manager = "Manager";
```

- [ ] **Step 2: Remove Manager from `RoleConfiguration`**

In `api/src/SurveyorLedger.Data/Configurations/RoleConfiguration.cs`, delete:

```csharp
    public static readonly Guid ManagerRoleId = new("00000000-0000-0000-0000-000000000002");
```

and delete this line from the `HasData(...)` call:

```csharp
            new Role { Id = ManagerRoleId, Name = Constants.SystemRoles.Manager, Description = "Manages jobs and surveyors within a workspace.", WorkspaceId = null, IsSystem = true, CreatedAt = seededAt, UpdatedAt = seededAt },
```

- [ ] **Step 3: Remove every Manager grant from `RolePermissionConfiguration`**

In `api/src/SurveyorLedger.Data/Configurations/RolePermissionConfiguration.cs`, delete every `Grant(..., RoleConfiguration.ManagerRoleId, ...)` line and its now-orphaned comment lines. The file's `HasData(...)` call becomes:

```csharp
        builder.HasData(
            // Admin: full workspace control
            Grant(new Guid("00000000-0000-0000-0000-000000000201"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewWorkspaceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000202"), RoleConfiguration.AdminRoleId, PermissionConfiguration.EditWorkspaceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000203"), RoleConfiguration.AdminRoleId, PermissionConfiguration.DeleteWorkspaceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000204"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ManageMembersId),
            // Surveyor, Client: view only
            Grant(new Guid("00000000-0000-0000-0000-000000000206"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewWorkspaceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000207"), RoleConfiguration.ClientRoleId, PermissionConfiguration.ViewWorkspaceId),
            // Land - Admin: full access
            Grant(new Guid("00000000-0000-0000-0000-000000000208"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000209"), RoleConfiguration.AdminRoleId, PermissionConfiguration.CreateLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000210"), RoleConfiguration.AdminRoleId, PermissionConfiguration.EditLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000211"), RoleConfiguration.AdminRoleId, PermissionConfiguration.DeleteLandId),
            // Land - Surveyor: view/create/edit, not delete (captures/updates land data in the field)
            Grant(new Guid("00000000-0000-0000-0000-000000000216"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000217"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.CreateLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000218"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.EditLandId),
            // Land - Client: view only
            Grant(new Guid("00000000-0000-0000-0000-000000000219"), RoleConfiguration.ClientRoleId, PermissionConfiguration.ViewLandId),
            // Job - Admin: full access
            Grant(new Guid("00000000-0000-0000-0000-000000000220"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000221"), RoleConfiguration.AdminRoleId, PermissionConfiguration.CreateJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000222"), RoleConfiguration.AdminRoleId, PermissionConfiguration.EditJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000223"), RoleConfiguration.AdminRoleId, PermissionConfiguration.DeleteJobId),
            // Job - Surveyor: view/create/edit, not delete
            Grant(new Guid("00000000-0000-0000-0000-000000000228"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000229"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.CreateJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000230"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.EditJobId),
            // Job - Client: view only (further scoped to their own jobs in JobService, not Casbin)
            Grant(new Guid("00000000-0000-0000-0000-000000000231"), RoleConfiguration.ClientRoleId, PermissionConfiguration.ViewJobId),
            // Client contacts - Admin/Surveyor: view+create (whoever can field the
            // call and capture a client). The Client role gets nothing here - a client
            // doesn't manage other clients.
            Grant(new Guid("00000000-0000-0000-0000-000000000232"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewClientId),
            Grant(new Guid("00000000-0000-0000-0000-000000000233"), RoleConfiguration.AdminRoleId, PermissionConfiguration.CreateClientId),
            Grant(new Guid("00000000-0000-0000-0000-000000000236"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewClientId),
            Grant(new Guid("00000000-0000-0000-0000-000000000237"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.CreateClientId),
            // Job view-all - Admin sees every job in the workspace; Surveyor/Client
            // are scoped to jobs they've been explicitly assigned (job-scoped UserAccess).
            Grant(new Guid("00000000-0000-0000-0000-000000000238"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewAllJobId)
        );
```

Note IDs `205, 212-215, 224-227, 234-235, 239` (all Manager grants) are gone; every remaining ID is untouched so their `RolePermission.Id` stays stable across the diff.

- [ ] **Step 4: Drop Manager from the two role-validation regexes**

In `api/src/SurveyorLedger.API/Models/Workspace/UpdateMemberRoleRequest.cs:8`, change:

```csharp
    [RegularExpression("^(Admin|Manager|Surveyor|Client)$", ErrorMessage = "Role must be Admin, Manager, Surveyor, or Client.")]
```

to:

```csharp
    [RegularExpression("^(Admin|Surveyor|Client)$", ErrorMessage = "Role must be Admin, Surveyor, or Client.")]
```

In `api/src/SurveyorLedger.API/Models/Invitation/InvitationRequest.cs:19`, make the identical change.

- [ ] **Step 5: Reword stale "Admin/Manager" doc comments**

In `api/src/SurveyorLedger.API/Models/Workspace/MemberResponse.cs:22`, change:

```csharp
    /// <summary>Scope types this member's role has blanket access to (e.g. "Job" for Admin/Manager).</summary>
```

to:

```csharp
    /// <summary>Scope types this member's role has blanket access to (e.g. "Job" for Admin).</summary>
```

In `api/src/SurveyorLedger.API/Services/JobService.cs:84`, change:

```csharp
    /// Admin/Manager) sees every job; everyone else sees only jobs they hold a job-scoped
```

to:

```csharp
    /// Admin) sees every job; everyone else sees only jobs they hold a job-scoped
```

In `api/src/SurveyorLedger.API/Services/JobService.cs:275`, change:

```csharp
    /// job.view_all (full workspace visibility - Admin/Manager), the caller must hold an
```

to:

```csharp
    /// job.view_all (full workspace visibility - Admin), the caller must hold an
```

In `api/src/SurveyorLedger.API/Services/WorkspaceService.cs:182`, change:

```csharp
        // Admin/Manager hold job.view_all, so they implicitly see every job without an
```

to:

```csharp
        // Admin holds job.view_all, so they implicitly see every job without an
```

- [ ] **Step 6: Build to confirm no dangling references**

Run: `cd api && dotnet build`
Expected: Build succeeded, 0 errors. (If anything still references `Constants.SystemRoles.Manager` or `RoleConfiguration.ManagerRoleId`, the build fails here — fix before moving on.)

- [ ] **Step 7: Commit**

```bash
git add api/src/SurveyorLedger.Core/Constants.cs api/src/SurveyorLedger.Data/Configurations/RoleConfiguration.cs api/src/SurveyorLedger.Data/Configurations/RolePermissionConfiguration.cs api/src/SurveyorLedger.API/Models/Workspace/UpdateMemberRoleRequest.cs api/src/SurveyorLedger.API/Models/Invitation/InvitationRequest.cs api/src/SurveyorLedger.API/Models/Workspace/MemberResponse.cs api/src/SurveyorLedger.API/Services/JobService.cs api/src/SurveyorLedger.API/Services/WorkspaceService.cs
git commit -m "refactor: drop Manager role from seed data, validators, and docs"
```

---

### Task 2: Generate and finish the Manager-removal migration

**Files:**
- Create: `api/src/SurveyorLedger.Data/Migrations/<timestamp>_RemoveManagerRole.cs` (generated, then manually edited to add one `Sql()` call)
- Create: `api/src/SurveyorLedger.Data/Migrations/<timestamp>_RemoveManagerRole.Designer.cs` (generated)
- Modify: `api/src/SurveyorLedger.Data/Migrations/ApplicationDbContextModelSnapshot.cs` (generated)

**Interfaces:**
- Consumes: the trimmed `RoleConfiguration`/`RolePermissionConfiguration` from Task 1.
- Produces: a clean LocalDB schema with no Manager `Role` row, no Manager `RolePermission` rows, and no stray `UserAccess` row pointing at the deleted role — required before Task 6 (Milestone migration) so both migrations apply cleanly in sequence and existing test suites (which run against a freshly-migrated schema) don't hit an FK violation.

- [ ] **Step 1: Generate the migration**

Run: `cd api && dotnet ef migrations add RemoveManagerRole --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`
Expected: two new files under `src/SurveyorLedger.Data/Migrations/`, and `ApplicationDbContextModelSnapshot.cs` updated. The generated `Up()` method contains `DeleteData` calls for the removed `RolePermission` rows and the removed `Role` row (EF diffs `HasData` automatically — no manual `DeleteData` writing needed).

- [ ] **Step 2: Add the one manual `Sql()` call for the stray UserAccess row**

The generated migration only handles seeded (`HasData`) rows. It does **not** know about the one real `UserAccess` row in the dev database that references the Manager role (`Id = F14EA6F7-CF89-4987-B878-766DC5146282`, inactive test data — see spec). Open the generated `<timestamp>_RemoveManagerRole.cs` and add a `Sql()` call as the **first** statement in `Up()`, before the generated `DeleteData` calls (it must run first so the `RolePermission`/`Role` deletes that follow don't hit the same FK from a different angle):

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Stray inactive test-data row referencing the Manager role being removed below.
    // Not HasData-seeded, so EF's diff doesn't generate this automatically.
    migrationBuilder.Sql("DELETE FROM UserAccesses WHERE Id = 'F14EA6F7-CF89-4987-B878-766DC5146282'");

    migrationBuilder.DeleteData(
        // ...generated calls follow, unchanged...
```

Leave `Down()` as generated — re-inserting the deleted test-data row on rollback isn't necessary (it was dead data).

- [ ] **Step 3: Apply the migration to LocalDB**

Run: `cd api && dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`
Expected: `Done.` with no errors. If it fails with an FK violation, another row references the Manager role that wasn't caught in Task 1's spec check — query `SELECT * FROM UserAccesses WHERE RoleId = '00000000-0000-0000-0000-000000000002'` and `SELECT * FROM Invitations WHERE RoleId = '00000000-0000-0000-0000-000000000002'` to find it, then extend Step 2's `Sql()` call.

- [ ] **Step 4: Verify Manager is gone**

Run: `sqlcmd -S "(localdb)\mssqllocaldb" -d SurveyorLedger -Q "SELECT COUNT(*) FROM Roles WHERE Name = 'Manager'; SELECT COUNT(*) FROM RolePermissions WHERE RoleId = '00000000-0000-0000-0000-000000000002';"`
Expected: both counts `0`.

- [ ] **Step 5: Run the full existing test suite**

Run: `cd api && dotnet test`
Expected: all tests pass (they never reference Manager — confirmed by reading `WorkspaceIntegrationTestBase`, which seeds only Admin/Surveyor/Client). This is the regression check that removing Manager didn't break Admin/Surveyor/Client-based RBAC elsewhere.

- [ ] **Step 6: Commit**

```bash
git add api/src/SurveyorLedger.Data/Migrations/
git commit -m "feat: remove Manager role via migration"
```

---

## Part 2 — Milestone Feature

### Task 3: `Milestone` entity + EF configuration + DbContext registration

**Files:**
- Create: `api/src/SurveyorLedger.Data/Entities/Milestone.cs`
- Create: `api/src/SurveyorLedger.Data/Configurations/MilestoneConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/ApplicationDbContext.cs`

**Interfaces:**
- Produces: `Milestone` entity with fields `Id, JobId, Title, Description?, DueDate?, Status, CompletedAt?, CompletedBy?, CreatedBy, CreatedAt, UpdatedAt, IsActive`, navigation `Job`, `CreatedByUser`, `CompletedByUser`. `ApplicationDbContext.Milestones` DbSet. These are consumed by `MilestoneService` in Task 5.

- [ ] **Step 1: Create the entity**

```csharp
namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A progress checkpoint within a Job (e.g. Site Visit, Survey Complete, Handover).
/// Tenant isolation is transitive through JobId -&gt; Job.WorkspaceId, same as
/// LandSurvey relies on LandId -&gt; Land.WorkspaceId - callers always resolve the
/// parent Job within the caller's workspace first.
/// </summary>
public class Milestone
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string Title { get; set; }
    public string? Description { get; set; }
    public DateTime? DueDate { get; set; }
    public string Status { get; set; } = "Pending";
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

Save as `api/src/SurveyorLedger.Data/Entities/Milestone.cs`.

- [ ] **Step 2: Create the EF configuration**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class MilestoneConfiguration : IEntityTypeConfiguration<Milestone>
{
    public void Configure(EntityTypeBuilder<Milestone> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.JobId);

        builder.HasOne(x => x.Job)
            .WithMany()
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CompletedByUser)
            .WithMany()
            .HasForeignKey(x => x.CompletedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

Save as `api/src/SurveyorLedger.Data/Configurations/MilestoneConfiguration.cs`.

- [ ] **Step 3: Register the DbSet and query filter**

In `api/src/SurveyorLedger.Data/ApplicationDbContext.cs`, add after `public DbSet<JobLand> JobLands { get; set; }`:

```csharp
    public DbSet<Milestone> Milestones { get; set; }
```

and add after `modelBuilder.Entity<Job>().HasQueryFilter(x => x.IsActive);`:

```csharp
        modelBuilder.Entity<Milestone>().HasQueryFilter(x => x.IsActive);
```

- [ ] **Step 4: Build**

Run: `cd api && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add api/src/SurveyorLedger.Data/Entities/Milestone.cs api/src/SurveyorLedger.Data/Configurations/MilestoneConfiguration.cs api/src/SurveyorLedger.Data/ApplicationDbContext.cs
git commit -m "feat: add Milestone entity and EF configuration"
```

---

### Task 4: Generate the `AddMilestones` migration

**Files:**
- Create: `api/src/SurveyorLedger.Data/Migrations/<timestamp>_AddMilestones.cs` (generated)
- Create: `api/src/SurveyorLedger.Data/Migrations/<timestamp>_AddMilestones.Designer.cs` (generated)
- Modify: `api/src/SurveyorLedger.Data/Migrations/ApplicationDbContextModelSnapshot.cs` (generated)

**Interfaces:**
- Consumes: `Milestone` entity + `MilestoneConfiguration` from Task 3.
- Produces: `Milestones` table in LocalDB, required before any test in Task 8 can run (`EnsureCreatedAsync` in `WorkspaceIntegrationTestBase` builds schema straight from the current model, so this task doesn't strictly gate the *tests* — but it does gate manually verifying against the real dev DB in Task 9).

- [ ] **Step 1: Generate the migration**

Run: `cd api && dotnet ef migrations add AddMilestones --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`
Expected: two new files, creating table `Milestones` with columns matching Task 3's entity, FKs to `Jobs` (cascade) and `Users` x2 (restrict), index on `JobId`.

- [ ] **Step 2: Apply to LocalDB**

Run: `cd api && dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`
Expected: `Done.`

- [ ] **Step 3: Verify the table exists**

Run: `sqlcmd -S "(localdb)\mssqllocaldb" -d SurveyorLedger -Q "SELECT TOP 1 * FROM Milestones"`
Expected: empty result set (0 rows), no error — confirms the table and columns exist.

- [ ] **Step 4: Commit**

```bash
git add api/src/SurveyorLedger.Data/Migrations/
git commit -m "feat: add Milestones table migration"
```

---

### Task 5: Milestone DTOs

**Files:**
- Create: `api/src/SurveyorLedger.API/Models/Milestone/MilestoneRequest.cs`
- Create: `api/src/SurveyorLedger.API/Models/Milestone/MilestoneStatusRequest.cs`
- Create: `api/src/SurveyorLedger.API/Models/Milestone/MilestoneResponse.cs`

**Interfaces:**
- Produces: `MilestoneRequest { Title, Description?, DueDate? }`, `MilestoneStatusRequest { Status }`, `MilestoneResponse { MilestoneId, JobId, Title, Description?, DueDate?, Status, CompletedAt?, CompletedBy?, CreatedBy, CreatedAt, UpdatedAt }`. Consumed by `MilestoneController` (Task 7) and `MilestoneService` (Task 6, for the request types).

- [ ] **Step 1: Create `MilestoneRequest`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Milestone;

/// <summary>
/// Request model for creating or updating a Milestone. Mirrors JobRequest's shape -
/// Title is the only required field.
/// </summary>
public class MilestoneRequest
{
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Title must be between 1 and 200 characters.")]
    public required string Title { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    public DateTime? DueDate { get; set; }
}
```

Save as `api/src/SurveyorLedger.API/Models/Milestone/MilestoneRequest.cs`.

- [ ] **Step 2: Create `MilestoneStatusRequest`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Milestone;

public class MilestoneStatusRequest
{
    [Required(ErrorMessage = "Status is required.")]
    [RegularExpression("^(Pending|InProgress|Completed)$",
        ErrorMessage = "Status must be Pending, InProgress, or Completed.")]
    public required string Status { get; set; }
}
```

Save as `api/src/SurveyorLedger.API/Models/Milestone/MilestoneStatusRequest.cs`.

- [ ] **Step 3: Create `MilestoneResponse`**

```csharp
namespace SurveyorLedger.API.Models.Milestone;

public class MilestoneResponse
{
    public Guid MilestoneId { get; set; }
    public Guid JobId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateTime? DueDate { get; set; }
    public required string Status { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedBy { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

Save as `api/src/SurveyorLedger.API/Models/Milestone/MilestoneResponse.cs`.

- [ ] **Step 4: Build**

Run: `cd api && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add api/src/SurveyorLedger.API/Models/Milestone/
git commit -m "feat: add Milestone request/response DTOs"
```

---

### Task 6: `MilestoneService`

**Files:**
- Create: `api/src/SurveyorLedger.API/Services/MilestoneService.cs`
- Modify: `api/src/SurveyorLedger.API/Program.cs`

**Interfaces:**
- Consumes: `Milestone` entity (Task 3), `MilestoneRequest`/`MilestoneStatusRequest` (Task 5), `ApplicationDbContext`, `ICasbinService.EnforceAsync(string subject, string resource, string action, string scope)`, `Constants.ScopeTypes.Job`, `NotFoundException`, `ForbiddenException`, `AppException`.
- Produces: `IMilestoneService` with methods `GetMilestonesAsync(Guid workspaceId, Guid callerUserId, Guid jobId) -> Task<List<Milestone>>`, `GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId) -> Task<Milestone>`, `CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, MilestoneRequest request) -> Task<Milestone>`, `UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, MilestoneRequest request) -> Task<Milestone>`, `UpdateStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, string status) -> Task<Milestone>`, `DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId) -> Task`. Consumed by `MilestoneController` (Task 7) and `MilestoneServiceTests` (Task 8).

- [ ] **Step 1: Write `MilestoneService`**

```csharp
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Milestone;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IMilestoneService
{
    Task<List<Milestone>> GetMilestonesAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
    Task<Milestone> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId);
    Task<Milestone> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, MilestoneRequest request);
    Task<Milestone> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, MilestoneRequest request);
    Task<Milestone> UpdateStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, string status);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId);
}

/// <summary>
/// Milestones are a job sub-resource: every action reuses JobService's job.view /
/// job.edit Casbin permissions and the same job-assignment scoping rule (unless the
/// caller holds job.view_all, they must hold a job-scoped UserAccess row for this
/// specific job). This is intentionally duplicated from JobService rather than
/// extracted to a shared base - see the design spec's reasoning: only two call sites
/// exist, and a shared abstraction for two users isn't justified yet.
/// </summary>
public class MilestoneService : IMilestoneService
{
    private static readonly HashSet<string> ValidStatuses = new() { "Pending", "InProgress", "Completed" };

    private readonly ApplicationDbContext _context;
    private readonly ICasbinService _casbinService;
    private readonly ILogger<MilestoneService> _logger;

    public MilestoneService(ApplicationDbContext context, ICasbinService casbinService, ILogger<MilestoneService> logger)
    {
        _context = context;
        _casbinService = casbinService;
        _logger = logger;
    }

    public async Task<List<Milestone>> GetMilestonesAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

        return await _context.Milestones
            .Where(m => m.JobId == jobId)
            .OrderBy(m => m.DueDate ?? DateTime.MaxValue)
            .ThenBy(m => m.CreatedAt)
            .ToListAsync();
    }

    public async Task<Milestone> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");
        return await FindMilestoneAsync(jobId, milestoneId);
    }

    public async Task<Milestone> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, MilestoneRequest request)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var milestone = new Milestone
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            Title = request.Title.Trim(),
            Description = request.Description,
            DueDate = request.DueDate,
            Status = "Pending",
            CreatedBy = callerUserId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Milestones.AddAsync(milestone);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Milestone {MilestoneId} created for job {JobId} by {UserId}", milestone.Id, jobId, callerUserId);
        return milestone;
    }

    public async Task<Milestone> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, MilestoneRequest request)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var milestone = await FindMilestoneAsync(jobId, milestoneId);
        milestone.Title = request.Title.Trim();
        milestone.Description = request.Description;
        milestone.DueDate = request.DueDate;
        milestone.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return milestone;
    }

    public async Task<Milestone> UpdateStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, string status)
    {
        if (!ValidStatuses.Contains(status))
            throw new ValidationException($"Status must be one of: {string.Join(", ", ValidStatuses)}.");

        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var milestone = await FindMilestoneAsync(jobId, milestoneId);
        milestone.Status = status;
        milestone.UpdatedAt = DateTime.UtcNow;

        if (status == "Completed")
        {
            milestone.CompletedAt = DateTime.UtcNow;
            milestone.CompletedBy = callerUserId;
        }
        else
        {
            // Reopening a milestone clears stale completion metadata rather than
            // leaving a CompletedAt/CompletedBy that no longer matches its status.
            milestone.CompletedAt = null;
            milestone.CompletedBy = null;
        }

        await _context.SaveChangesAsync();
        return milestone;
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var milestone = await FindMilestoneAsync(jobId, milestoneId);
        milestone.IsActive = false;
        milestone.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    private async Task<Job> FindJobAsync(Guid workspaceId, Guid jobId)
    {
        return await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Job not found");
    }

    private async Task<Milestone> FindMilestoneAsync(Guid jobId, Guid milestoneId)
    {
        return await _context.Milestones.FirstOrDefaultAsync(m => m.Id == milestoneId && m.JobId == jobId)
            ?? throw new NotFoundException("Milestone not found");
    }

    private Task<bool> HasFullJobAccessAsync(Guid callerUserId, Guid workspaceId) =>
        _casbinService.EnforceAsync(callerUserId.ToString(), "job", "view_all", workspaceId.ToString());

    private Task<bool> IsAssignedToJobAsync(Guid callerUserId, Guid jobId) =>
        _context.UserAccesses.AnyAsync(ua =>
            ua.UserId == callerUserId && ua.IsActive &&
            ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == jobId);

    private async Task EnsureJobAccessAsync(Guid callerUserId, Guid workspaceId, Guid jobId, string action)
    {
        await EnsureAllowedAsync(callerUserId, action, workspaceId);
        if (await HasFullJobAccessAsync(callerUserId, workspaceId))
            return;
        if (!await IsAssignedToJobAsync(callerUserId, jobId))
            throw new ForbiddenException($"You do not have permission to {action} milestones on this job.");
    }

    private async Task EnsureAllowedAsync(Guid callerUserId, string action, Guid workspaceId)
    {
        var allowed = await _casbinService.EnforceAsync(callerUserId.ToString(), "job", action, workspaceId.ToString());
        if (!allowed)
            throw new ForbiddenException($"You do not have permission to {action} milestones in this workspace.");
    }
}
```

Save as `api/src/SurveyorLedger.API/Services/MilestoneService.cs`.

- [ ] **Step 2: Register in DI**

In `api/src/SurveyorLedger.API/Program.cs`, add after `builder.Services.AddScoped<IJobService, JobService>();` (line 96):

```csharp
builder.Services.AddScoped<IMilestoneService, MilestoneService>();
```

- [ ] **Step 3: Build**

Run: `cd api && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add api/src/SurveyorLedger.API/Services/MilestoneService.cs api/src/SurveyorLedger.API/Program.cs
git commit -m "feat: add MilestoneService with job-scoped access control"
```

---

### Task 7: `MilestoneController`

**Files:**
- Create: `api/src/SurveyorLedger.API/Controllers/MilestoneController.cs`

**Interfaces:**
- Consumes: `IMilestoneService` (Task 6), `MilestoneRequest`/`MilestoneStatusRequest`/`MilestoneResponse` (Task 5), `ApiResponse<T>`.
- Produces: HTTP routes under `api/workspace/{workspaceId}/job/{jobId}/milestone`.

- [ ] **Step 1: Write the controller**

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Milestone;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/job/{jobId}/milestone")]
    [Authorize]
    public class MilestoneController : ControllerBase
    {
        private readonly IMilestoneService _milestoneService;

        public MilestoneController(IMilestoneService milestoneService)
        {
            _milestoneService = milestoneService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<MilestoneResponse>>>> List(Guid workspaceId, Guid jobId)
        {
            var milestones = await _milestoneService.GetMilestonesAsync(workspaceId, CallerId(), jobId);
            return Ok(ApiResponse<List<MilestoneResponse>>.Ok(milestones.Select(ToResponse).ToList()));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<MilestoneResponse>>> GetById(Guid workspaceId, Guid jobId, Guid id)
        {
            var milestone = await _milestoneService.GetByIdAsync(workspaceId, CallerId(), jobId, id);
            return Ok(ApiResponse<MilestoneResponse>.Ok(ToResponse(milestone)));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<MilestoneResponse>>> Create(Guid workspaceId, Guid jobId, [FromBody] MilestoneRequest request)
        {
            var milestone = await _milestoneService.CreateAsync(workspaceId, CallerId(), jobId, request);
            return CreatedAtAction(nameof(GetById), new { workspaceId, jobId, id = milestone.Id }, ApiResponse<MilestoneResponse>.Ok(ToResponse(milestone)));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<MilestoneResponse>>> Update(Guid workspaceId, Guid jobId, Guid id, [FromBody] MilestoneRequest request)
        {
            var milestone = await _milestoneService.UpdateAsync(workspaceId, CallerId(), jobId, id, request);
            return Ok(ApiResponse<MilestoneResponse>.Ok(ToResponse(milestone)));
        }

        [HttpPut("{id}/status")]
        public async Task<ActionResult<ApiResponse<MilestoneResponse>>> UpdateStatus(Guid workspaceId, Guid jobId, Guid id, [FromBody] MilestoneStatusRequest request)
        {
            var milestone = await _milestoneService.UpdateStatusAsync(workspaceId, CallerId(), jobId, id, request.Status);
            return Ok(ApiResponse<MilestoneResponse>.Ok(ToResponse(milestone)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid workspaceId, Guid jobId, Guid id)
        {
            await _milestoneService.DeleteAsync(workspaceId, CallerId(), jobId, id);
            return NoContent();
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        private static MilestoneResponse ToResponse(Milestone m) => new()
        {
            MilestoneId = m.Id,
            JobId = m.JobId,
            Title = m.Title,
            Description = m.Description,
            DueDate = m.DueDate,
            Status = m.Status,
            CompletedAt = m.CompletedAt,
            CompletedBy = m.CompletedBy,
            CreatedBy = m.CreatedBy,
            CreatedAt = m.CreatedAt,
            UpdatedAt = m.UpdatedAt
        };
    }
}
```

Save as `api/src/SurveyorLedger.API/Controllers/MilestoneController.cs`.

- [ ] **Step 2: Build**

Run: `cd api && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add api/src/SurveyorLedger.API/Controllers/MilestoneController.cs
git commit -m "feat: add MilestoneController REST endpoints"
```

---

### Task 8: `MilestoneServiceTests` — access-control matrix

**Files:**
- Create: `api/tests/SurveyorLedger.API.Tests/Services/MilestoneServiceTests.cs`

**Interfaces:**
- Consumes: `WorkspaceIntegrationTestBase` (`WorkspaceId`, `AdminId`, `SurveyorId`, `ClientId`, `Context`, `GrantService`, `GetService<T>()`), `IJobService`/`JobService` (to seed a job), `IMilestoneService`/`MilestoneService` (Task 6), `MilestoneRequest` (Task 5), `ForbiddenException`/`NotFoundException`.
- Produces: nothing consumed elsewhere — this is the terminal verification for Part 2.

- [ ] **Step 1: Write the test file**

```csharp
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.Milestone;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// Milestone access mirrors JobAccessScopingTests: job.edit (Admin/Surveyor) is needed
/// to mutate, job.view (everyone incl. Client) to read, and unless the caller holds
/// job.view_all (Admin), they must hold a job-scoped UserAccess row for the specific
/// job the milestone belongs to.
/// </summary>
public class MilestoneServiceTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IMilestoneService _milestoneService = null!;
    private Guid _jobAId;
    private Guid _jobBId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IMilestoneService, MilestoneService>();
    }

    private async Task SeedJobsAsync()
    {
        _jobService = GetService<IJobService>();
        _milestoneService = GetService<IMilestoneService>();

        var jobA = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        var jobB = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job B" });
        _jobAId = jobA.Id;
        _jobBId = jobB.Id;

        // Surveyor and Client both assigned to Job A only; neither is assigned to Job B.
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobAId, SurveyorId);
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobAId, ClientId);
    }

    [Fact]
    public async Task Admin_CanCreateMilestone_OnAnyJob_WithoutExplicitAssignment()
    {
        await SeedJobsAsync();
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobBId, new MilestoneRequest { Title = "Site Visit" });
        Assert.Equal("Site Visit", milestone.Title);
        Assert.Equal("Pending", milestone.Status);
    }

    [Fact]
    public async Task Surveyor_CanCreateMilestone_OnAssignedJob()
    {
        await SeedJobsAsync();
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, SurveyorId, _jobAId, new MilestoneRequest { Title = "Survey Complete" });
        Assert.Equal("Survey Complete", milestone.Title);
    }

    [Fact]
    public async Task Surveyor_CannotCreateMilestone_OnUnassignedJob()
    {
        // Regression guard: Surveyor's role grants job.edit workspace-wide in Casbin,
        // but that alone must not be enough to add a milestone to a job they aren't
        // assigned to.
        await SeedJobsAsync();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _milestoneService.CreateAsync(WorkspaceId, SurveyorId, _jobBId, new MilestoneRequest { Title = "Hijacked" }));
    }

    [Fact]
    public async Task Client_CannotCreateMilestone_EvenOnAssignedJob()
    {
        await SeedJobsAsync();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _milestoneService.CreateAsync(WorkspaceId, ClientId, _jobAId, new MilestoneRequest { Title = "Not allowed" }));
    }

    [Fact]
    public async Task Client_CanViewMilestones_OnAssignedJob()
    {
        await SeedJobsAsync();
        await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "Site Visit" });

        var milestones = await _milestoneService.GetMilestonesAsync(WorkspaceId, ClientId, _jobAId);
        var milestone = Assert.Single(milestones);
        Assert.Equal("Site Visit", milestone.Title);
    }

    [Fact]
    public async Task Client_CannotViewMilestones_OnUnassignedJob()
    {
        await SeedJobsAsync();
        await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobBId, new MilestoneRequest { Title = "Site Visit" });

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _milestoneService.GetMilestonesAsync(WorkspaceId, ClientId, _jobBId));
    }

    [Fact]
    public async Task Admin_CanViewMilestones_OnAnyJob_WithoutExplicitAssignment()
    {
        await SeedJobsAsync();
        await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobBId, new MilestoneRequest { Title = "Site Visit" });

        var milestones = await _milestoneService.GetMilestonesAsync(WorkspaceId, AdminId, _jobBId);
        Assert.Single(milestones);
    }

    [Fact]
    public async Task CompletingMilestone_StampsCompletedAtAndCompletedBy()
    {
        await SeedJobsAsync();
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "Deed Verified" });

        var completed = await _milestoneService.UpdateStatusAsync(WorkspaceId, SurveyorId, _jobAId, milestone.Id, "Completed");

        Assert.Equal("Completed", completed.Status);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(SurveyorId, completed.CompletedBy);
    }

    [Fact]
    public async Task ReopeningMilestone_ClearsCompletionMetadata()
    {
        await SeedJobsAsync();
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "Deed Verified" });
        await _milestoneService.UpdateStatusAsync(WorkspaceId, AdminId, _jobAId, milestone.Id, "Completed");

        var reopened = await _milestoneService.UpdateStatusAsync(WorkspaceId, AdminId, _jobAId, milestone.Id, "InProgress");

        Assert.Equal("InProgress", reopened.Status);
        Assert.Null(reopened.CompletedAt);
        Assert.Null(reopened.CompletedBy);
    }

    [Fact]
    public async Task InvalidStatus_ThrowsValidationException()
    {
        await SeedJobsAsync();
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "Deed Verified" });

        await Assert.ThrowsAsync<ValidationException>(
            () => _milestoneService.UpdateStatusAsync(WorkspaceId, AdminId, _jobAId, milestone.Id, "Bogus"));
    }

    [Fact]
    public async Task DeletedMilestone_IsSoftDeleted_AndExcludedFromList()
    {
        await SeedJobsAsync();
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "Site Visit" });

        await _milestoneService.DeleteAsync(WorkspaceId, AdminId, _jobAId, milestone.Id);

        var milestones = await _milestoneService.GetMilestonesAsync(WorkspaceId, AdminId, _jobAId);
        Assert.Empty(milestones);
    }

    [Fact]
    public async Task MilestoneFromDifferentJob_Returns404()
    {
        await SeedJobsAsync();
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "Site Visit" });

        await Assert.ThrowsAsync<NotFoundException>(
            () => _milestoneService.GetByIdAsync(WorkspaceId, AdminId, _jobBId, milestone.Id));
    }

    [Fact]
    public async Task JobFromDifferentWorkspace_Returns404()
    {
        await SeedJobsAsync();
        await Assert.ThrowsAsync<NotFoundException>(
            () => _milestoneService.GetMilestonesAsync(Guid.NewGuid(), AdminId, _jobAId));
    }
}
```

Save as `api/tests/SurveyorLedger.API.Tests/Services/MilestoneServiceTests.cs`.

- [ ] **Step 2: Run the new tests**

Run: `cd api && dotnet test --filter "FullyQualifiedName~MilestoneServiceTests"`
Expected: all 13 tests pass. If `CompletingMilestone_StampsCompletedAtAndCompletedBy` or `ReopeningMilestone_ClearsCompletionMetadata` fail, check `MilestoneService.UpdateStatusAsync`'s completion-stamp/clear branches from Task 6 Step 1. If any Forbidden/NotFound test fails, check `EnsureJobAccessAsync`/`FindJobAsync`/`FindMilestoneAsync` filtering.

- [ ] **Step 3: Run the full test suite**

Run: `cd api && dotnet test`
Expected: all tests pass, including the pre-existing suite from Part 1 Task 2 Step 5 — confirms the Milestone addition didn't regress anything.

- [ ] **Step 4: Commit**

```bash
git add api/tests/SurveyorLedger.API.Tests/Services/MilestoneServiceTests.cs
git commit -m "test: add Milestone access-control and lifecycle tests"
```

---

## Self-Review Notes

- **Spec coverage:** entity/config/migration (Task 3-4), DTOs (Task 5), service with reused job.edit/job.view + assignment scoping (Task 6), controller routes matching the spec's table (Task 7), test matrix covering the spec's "Testing" section bullet points (Task 8), Manager removal with the exact touch points and the one stray-row deletion from the spec (Task 1-2). All spec sections have a task.
- **Type consistency:** `MilestoneRequest.Title`/`Description`/`DueDate` (Task 5) match what `MilestoneService.CreateAsync`/`UpdateAsync` read (Task 6). `IMilestoneService` method signatures (Task 6) match exactly what `MilestoneController` (Task 7) and `MilestoneServiceTests` (Task 8) call. `ValidStatuses` set in Task 6 matches the regex whitelist in `MilestoneStatusRequest` (Task 5) and the test's `"Bogus"` rejection case (Task 8).
- **Ordering:** Manager removal (Part 1) is sequenced before Milestone (Part 2) only because the design spec defined Milestone's permission table against the post-removal role set — there's no technical dependency between the two migrations, but running them in this order matches the spec's own reasoning.
