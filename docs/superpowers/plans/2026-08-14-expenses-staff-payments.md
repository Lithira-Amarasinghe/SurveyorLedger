# Expenses & Staff Payments Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the backend (DB + Services + API) for `Expense` and
`StaffPayment` — phase 2 of the billing feature, the cost side that
Profitability/Dashboard (phase 3) depend on — per
`docs/superpowers/specs/2026-08-14-expenses-staff-payments-design.md`.

**Architecture:** Two new entities, each a required sub-resource of `Job`
(`JobId` mandatory, tenant isolation transitive through `JobId → Job.WorkspaceId`,
same as `Milestone`). Own service + controller each, following
`Controller → Service → Data` layering. Unlike `Milestone` (which reuses
`job.view`/`job.edit` Casbin permissions), these get their own RBAC resources
(`expense`, `staffpayment`) — necessary because a job-scoped Client member must
see zero financial data even on jobs they're assigned to, and reusing
`job.view` would leak it to them.

**Tech Stack:** .NET 9, EF Core 9, SQL Server LocalDB, xUnit integration tests
(same `WorkspaceIntegrationTestBase` pattern as the billing-core tests).

## Global Constraints

- Tenant isolation: every query filters by workspace via the parent `Job`
  (`FindJobAsync(workspaceId, jobId)` first, exactly like `MilestoneService`) —
  no exceptions.
- Migrations generated via `dotnet ef migrations add`. RBAC seed data goes
  through `PermissionConfiguration`/`RolePermissionConfiguration` `HasData`
  (NOT a hand-written migration) — `EnsureCreatedAsync` in tests reads `HasData`,
  not raw `InsertData` migrations; this was the root cause of every RBAC test
  failure in the billing-core phase, verified against the real seeding
  mechanism this time before writing any migration.
- Both entities require `JobId` (no nullable option) and hard-delete (no
  `IsActive`) — wrong entries are deleted outright, same reasoning as
  `LandSurvey`/`LandDeed`.
- Route pattern matches `MilestoneController` exactly: singular segments,
  `api/workspace/{workspaceId}/job/{jobId}/expense` and `.../staff-payment` —
  not `jobs`/`expenses` (checked the actual existing route before assuming).
- Resource names `expense`/`staffpayment` — verified against every existing
  `Permission.Resource` value in `PermissionConfiguration.cs` before use, no
  collision (unlike phase 1's `client`/`billingclient` collision).
- Receipt upload reuses `IFileStorageService`, extensions `.pdf/.jpg/.jpeg/.png`,
  size cap `DocumentService.MaxFileSizeBytes` (25MB) — same constants Document/Land
  photo upload already use, not reinvented.

---

### Task 1: Expense, StaffPayment entities + EF configuration + migration

**Files:**
- Create: `api/src/SurveyorLedger.Data/Entities/Expense.cs`
- Create: `api/src/SurveyorLedger.Data/Entities/StaffPayment.cs`
- Create: `api/src/SurveyorLedger.Data/Configurations/ExpenseConfiguration.cs`
- Create: `api/src/SurveyorLedger.Data/Configurations/StaffPaymentConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/ApplicationDbContext.cs`
- Create (generated): `api/src/SurveyorLedger.Data/Migrations/<timestamp>_AddExpensesAndStaffPayments.cs`

**Interfaces:**
- Produces: `Expense { Id, WorkspaceId, JobId, Category (string), Amount, Description, IncurredDate, ReceiptFilePath?, RecordedBy, CreatedAt }`
- Produces: `StaffPayment { Id, WorkspaceId, JobId, UserId, Type (string), Amount, PaidDate, Notes?, RecordedBy, CreatedAt }`

- [ ] **Step 1: Write the entities**

`api/src/SurveyorLedger.Data/Entities/Expense.cs`:
```csharp
namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A cost incurred doing a Job (travel, equipment, printing, third-party/government
/// fees, misc). Tenant isolation is transitive through JobId -> Job.WorkspaceId, same
/// as Milestone. Hard delete, no IsActive - corrects a mis-entered record, not
/// meaningful history to preserve once wrong (same reasoning as LandSurvey/LandDeed).
/// No approval workflow - recorded directly, matching this app's flat RBAC.
/// </summary>
public class Expense
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid JobId { get; set; }
    public string Category { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime IncurredDate { get; set; }
    public string? ReceiptFilePath { get; set; }
    public Guid RecordedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Job Job { get; set; }
    public User RecordedByUser { get; set; }
}
```

`api/src/SurveyorLedger.Data/Entities/StaffPayment.cs`:
```csharp
namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A payout to a staff member for work on a Job (salary/commission/bonus/profit
/// share). Amount is always manually entered - no percentage-of-revenue
/// auto-calculation. Tenant isolation transitive through JobId, same as Expense.
/// </summary>
public class StaffPayment
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid JobId { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidDate { get; set; }
    public string? Notes { get; set; }
    public Guid RecordedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Job Job { get; set; }
    public User User { get; set; }
    public User RecordedByUser { get; set; }
}
```

- [ ] **Step 2: Write the EF configurations**

`api/src/SurveyorLedger.Data/Configurations/ExpenseConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Category).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.ReceiptFilePath).HasMaxLength(500);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.WorkspaceId);
        builder.HasIndex(x => x.JobId);

        builder.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RecordedByUser).WithMany().HasForeignKey(x => x.RecordedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
```

`api/src/SurveyorLedger.Data/Configurations/StaffPaymentConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class StaffPaymentConfiguration : IEntityTypeConfiguration<StaffPayment>
{
    public void Configure(EntityTypeBuilder<StaffPayment> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.WorkspaceId);
        builder.HasIndex(x => x.JobId);
        builder.HasIndex(x => x.UserId);

        builder.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RecordedByUser).WithMany().HasForeignKey(x => x.RecordedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
```

Note: `StaffPayment` has two FKs to `User` (`UserId` and `RecordedBy`). Both use
`.WithMany()` (no back-reference collection on `User`), so EF will not try to
merge them into one relationship - same pattern already used by `Payment`
(`RecordedByUser`) and `Land` (`Owner` vs the job's `CreatedByUser`), so no new
risk here.

- [ ] **Step 3: Register DbSets**

In `api/src/SurveyorLedger.Data/ApplicationDbContext.cs`, alongside the
`DbSet<Payment> Payments` line added in the billing-core phase, add:
```csharp
public DbSet<Expense> Expenses { get; set; }
public DbSet<StaffPayment> StaffPayments { get; set; }
```

- [ ] **Step 4: Generate and apply the migration**

Run:
```bash
cd api && dotnet ef migrations add AddExpensesAndStaffPayments --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
cd api && dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
cd api && dotnet build
```
Expected: migration creates `Expenses` and `StaffPayments` tables; build succeeds
with 0 errors.

- [ ] **Step 5: Run the migration-check skill**

Invoke `migration-check` against the new migration. Confirm indexes on
`WorkspaceId`/`JobId`, no hand-edits, descriptive name.

- [ ] **Step 6: Commit**

```bash
git add api/src/SurveyorLedger.Data/Entities/Expense.cs api/src/SurveyorLedger.Data/Entities/StaffPayment.cs api/src/SurveyorLedger.Data/Configurations/ExpenseConfiguration.cs api/src/SurveyorLedger.Data/Configurations/StaffPaymentConfiguration.cs api/src/SurveyorLedger.Data/ApplicationDbContext.cs api/src/SurveyorLedger.Data/Migrations/
git commit -m "feat: add Expense and StaffPayment entities"
```

---

### Task 2: Seed RBAC permissions for expense/staffpayment resources

**Files:**
- Modify: `api/src/SurveyorLedger.Data/Configurations/PermissionConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/RolePermissionConfiguration.cs`
- Create (generated): `api/src/SurveyorLedger.Data/Migrations/<timestamp>_SeedExpenseStaffPaymentPermissions.cs`

**Interfaces:**
- Produces: permission strings `expense.view`/`.create`/`.edit`/`.delete`,
  `staffpayment.view`/`.create`/`.edit`/`.delete`/`.view_all` — used as the
  `resource`/`action` pair in `EnsureAllowedAsync` calls in Tasks 3-4.

- [ ] **Step 1: Add permission constants and HasData entries**

In `api/src/SurveyorLedger.Data/Configurations/PermissionConfiguration.cs`, add
after the existing `DeleteInvoiceId` constant (Id `128`, added in the billing-core
phase):
```csharp
public static readonly Guid ViewExpenseId = new("00000000-0000-0000-0000-000000000129");
public static readonly Guid CreateExpenseId = new("00000000-0000-0000-0000-000000000130");
public static readonly Guid EditExpenseId = new("00000000-0000-0000-0000-000000000131");
public static readonly Guid DeleteExpenseId = new("00000000-0000-0000-0000-000000000132");
public static readonly Guid ViewStaffPaymentId = new("00000000-0000-0000-0000-000000000133");
public static readonly Guid CreateStaffPaymentId = new("00000000-0000-0000-0000-000000000134");
public static readonly Guid EditStaffPaymentId = new("00000000-0000-0000-0000-000000000135");
public static readonly Guid DeleteStaffPaymentId = new("00000000-0000-0000-0000-000000000136");
public static readonly Guid ViewAllStaffPaymentId = new("00000000-0000-0000-0000-000000000137");
```

Add to the `builder.HasData(...)` call, after the existing `DeleteInvoiceId` row:
```csharp
new Permission { Id = ViewExpenseId, Name = "expense.view", Description = "View job expenses.", Resource = "expense", Action = "view", Scope = null, CreatedAt = seededAt },
new Permission { Id = CreateExpenseId, Name = "expense.create", Description = "Record job expenses.", Resource = "expense", Action = "create", Scope = null, CreatedAt = seededAt },
new Permission { Id = EditExpenseId, Name = "expense.edit", Description = "Edit job expenses.", Resource = "expense", Action = "edit", Scope = null, CreatedAt = seededAt },
new Permission { Id = DeleteExpenseId, Name = "expense.delete", Description = "Delete job expenses.", Resource = "expense", Action = "delete", Scope = null, CreatedAt = seededAt },
new Permission { Id = ViewStaffPaymentId, Name = "staffpayment.view", Description = "View staff payments.", Resource = "staffpayment", Action = "view", Scope = null, CreatedAt = seededAt },
new Permission { Id = CreateStaffPaymentId, Name = "staffpayment.create", Description = "Record staff payments.", Resource = "staffpayment", Action = "create", Scope = null, CreatedAt = seededAt },
new Permission { Id = EditStaffPaymentId, Name = "staffpayment.edit", Description = "Edit staff payments.", Resource = "staffpayment", Action = "edit", Scope = null, CreatedAt = seededAt },
new Permission { Id = DeleteStaffPaymentId, Name = "staffpayment.delete", Description = "Delete staff payments.", Resource = "staffpayment", Action = "delete", Scope = null, CreatedAt = seededAt },
new Permission { Id = ViewAllStaffPaymentId, Name = "staffpayment.view_all", Description = "View every staff payment on a job, not just the caller's own.", Resource = "staffpayment", Action = "view_all", Scope = null, CreatedAt = seededAt }
```

- [ ] **Step 2: Add RolePermission HasData entries**

In `api/src/SurveyorLedger.Data/Configurations/RolePermissionConfiguration.cs`,
add to the `builder.HasData(...)` call, after the last existing row (Id `241`,
`MemberRoleId` + `ViewWorkspaceId`) — wait, the billing-core phase already
appended rows `242`-`268` here; add after those, starting at `269`:

```csharp
// Expense - Admin: full CRUD. Surveyor: view/create/edit (field staff record
// their own costs), no delete. Client: nothing (financial data).
Grant(new Guid("00000000-0000-0000-0000-000000000269"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewExpenseId),
Grant(new Guid("00000000-0000-0000-0000-000000000270"), RoleConfiguration.AdminRoleId, PermissionConfiguration.CreateExpenseId),
Grant(new Guid("00000000-0000-0000-0000-000000000271"), RoleConfiguration.AdminRoleId, PermissionConfiguration.EditExpenseId),
Grant(new Guid("00000000-0000-0000-0000-000000000272"), RoleConfiguration.AdminRoleId, PermissionConfiguration.DeleteExpenseId),
Grant(new Guid("00000000-0000-0000-0000-000000000273"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewExpenseId),
Grant(new Guid("00000000-0000-0000-0000-000000000274"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.CreateExpenseId),
Grant(new Guid("00000000-0000-0000-0000-000000000275"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.EditExpenseId),
// StaffPayment - Admin: full CRUD + view_all (payroll is a stricter surface than
// expenses). Surveyor: view only, and only their own (view_all withheld - the
// service layer filters to UserId == callerUserId without it). Client: nothing.
Grant(new Guid("00000000-0000-0000-0000-000000000276"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewStaffPaymentId),
Grant(new Guid("00000000-0000-0000-0000-000000000277"), RoleConfiguration.AdminRoleId, PermissionConfiguration.CreateStaffPaymentId),
Grant(new Guid("00000000-0000-0000-0000-000000000278"), RoleConfiguration.AdminRoleId, PermissionConfiguration.EditStaffPaymentId),
Grant(new Guid("00000000-0000-0000-0000-000000000279"), RoleConfiguration.AdminRoleId, PermissionConfiguration.DeleteStaffPaymentId),
Grant(new Guid("00000000-0000-0000-0000-000000000280"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewAllStaffPaymentId),
Grant(new Guid("00000000-0000-0000-0000-000000000281"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewStaffPaymentId)
```

Before writing this, run
`grep -oE "00000000-0000-0000-0000-0000000002[0-9][0-9]" api/src/SurveyorLedger.Data/Configurations/RolePermissionConfiguration.cs | sort -u | tail -3`
to confirm `268` is still the highest existing id (it was at the time this plan
was written) - if the billing-core phase's commit history has moved since, start
this block at the next free id instead of hardcoding `269`.

- [ ] **Step 3: Generate the migration**

Run:
```bash
cd api && dotnet ef migrations add SeedExpenseStaffPaymentPermissions --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```
Expected: EF detects the `HasData` diff and generates real `InsertData` calls
for both `Permissions` and `RolePermissions` automatically - do not hand-write
this migration's body.

- [ ] **Step 4: Apply and verify**

Run:
```bash
cd api && dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
cd api && dotnet build
```
Expected: both succeed.

- [ ] **Step 5: Run the migration-check skill**

Invoke `migration-check` against this migration.

- [ ] **Step 6: Commit**

```bash
git add api/src/SurveyorLedger.Data/Configurations/PermissionConfiguration.cs api/src/SurveyorLedger.Data/Configurations/RolePermissionConfiguration.cs api/src/SurveyorLedger.Data/Migrations/
git commit -m "feat: seed RBAC permissions for expense/staffpayment resources"
```

---

### Task 3: ExpenseService + ExpensesController (CRUD + receipt upload/download)

**Files:**
- Create: `api/src/SurveyorLedger.API/Models/Expense/ExpenseDtos.cs`
- Create: `api/src/SurveyorLedger.API/Services/ExpenseService.cs`
- Create: `api/src/SurveyorLedger.API/Controllers/ExpenseController.cs`
- Modify: `api/src/SurveyorLedger.API/Program.cs`
- Create: `api/tests/SurveyorLedger.API.Tests/Services/ExpenseServiceTests.cs`

**Interfaces:**
- Consumes: `Expense` entity (Task 1), `IScopedAccessService.EnsureAllowedAsync`,
  `IFileStorageService.SaveAsync`/`OpenAsync` (same pattern as
  `LandService.UploadPhotoAsync`), `DocumentService.MaxFileSizeBytes`.
- Produces: `IExpenseService` with `CreateAsync`, `GetAllAsync`, `GetByIdAsync`,
  `UpdateAsync`, `DeleteAsync`, `UploadReceiptAsync`, `GetReceiptFileAsync`.

- [ ] **Step 1: Write the DTOs**

```csharp
namespace SurveyorLedger.API.Models.Expense;

public class ExpenseRequest
{
    public string Category { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime IncurredDate { get; set; }
}

public class ExpenseResponse
{
    public Guid ExpenseId { get; set; }
    public Guid JobId { get; set; }
    public string Category { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime IncurredDate { get; set; }
    public bool HasReceipt { get; set; }
    public string RecordedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 2: Write the failing service tests**

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Expense;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class ExpenseServiceTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IExpenseService _expenseService = null!;
    private Guid _jobId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-expense-test-{Guid.NewGuid():N}")
                })
                .Build());
    }

    private async Task SeedJobAsync()
    {
        _jobService = GetService<IJobService>();
        _expenseService = GetService<IExpenseService>();
        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Survey Job" });
        _jobId = job.Id;
    }

    [Fact]
    public async Task CreateAsync_PersistsExpense()
    {
        await SeedJobAsync();
        var expense = await _expenseService.CreateAsync(WorkspaceId, AdminId, _jobId, new ExpenseRequest
        {
            Category = "Travel",
            Amount = 5000m,
            Description = "Fuel",
            IncurredDate = DateTime.UtcNow
        });

        Assert.Equal("Travel", expense.Category);
        var fetched = await _expenseService.GetByIdAsync(WorkspaceId, AdminId, _jobId, expense.Id);
        Assert.Equal(expense.Id, fetched.Id);
    }

    [Fact]
    public async Task Client_CannotCreateExpense()
    {
        await SeedJobAsync();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _expenseService.CreateAsync(WorkspaceId, ClientId, _jobId, new ExpenseRequest { Category = "Travel", Amount = 100m, IncurredDate = DateTime.UtcNow }));
    }

    [Fact]
    public async Task Surveyor_CannotDeleteExpense()
    {
        await SeedJobAsync();
        var expense = await _expenseService.CreateAsync(WorkspaceId, AdminId, _jobId, new ExpenseRequest { Category = "Travel", Amount = 100m, IncurredDate = DateTime.UtcNow });
        await Assert.ThrowsAsync<ForbiddenException>(() => _expenseService.DeleteAsync(WorkspaceId, SurveyorId, _jobId, expense.Id));
    }

    [Fact]
    public async Task JobFromOtherWorkspace_ThrowsNotFound()
    {
        await SeedJobAsync();
        var otherWorkspaceId = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(
            () => _expenseService.CreateAsync(otherWorkspaceId, AdminId, _jobId, new ExpenseRequest { Category = "Travel", Amount = 100m, IncurredDate = DateTime.UtcNow }));
    }

    private static IFormFile MakeReceipt(string name = "receipt.jpg", string contentType = "image/jpeg")
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("fake-receipt-bytes");
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    [Fact]
    public async Task UploadReceiptAsync_PersistsReceipt()
    {
        await SeedJobAsync();
        var expense = await _expenseService.CreateAsync(WorkspaceId, AdminId, _jobId, new ExpenseRequest { Category = "Equipment", Amount = 2000m, IncurredDate = DateTime.UtcNow });

        var updated = await _expenseService.UploadReceiptAsync(WorkspaceId, AdminId, _jobId, expense.Id, MakeReceipt());
        Assert.True(updated.HasReceipt);
    }

    [Fact]
    public async Task DeleteAsync_RemovesExpense()
    {
        await SeedJobAsync();
        var expense = await _expenseService.CreateAsync(WorkspaceId, AdminId, _jobId, new ExpenseRequest { Category = "Miscellaneous", Amount = 50m, IncurredDate = DateTime.UtcNow });
        await _expenseService.DeleteAsync(WorkspaceId, AdminId, _jobId, expense.Id);

        var all = await _expenseService.GetAllAsync(WorkspaceId, AdminId, _jobId);
        Assert.DoesNotContain(all, e => e.Id == expense.Id);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter ExpenseServiceTests`
Expected: FAIL — `IExpenseService`/`ExpenseService` do not exist yet.

- [ ] **Step 4: Write the service**

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Expense;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IExpenseService
{
    Task<Expense> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, ExpenseRequest request);
    Task<List<Expense>> GetAllAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
    Task<Expense> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId);
    Task<Expense> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId, ExpenseRequest request);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId);
    Task<Expense> UploadReceiptAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId, IFormFile file);
    Task<(Expense expense, Stream content)> GetReceiptFileAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId);
}

public class ExpenseService : IExpenseService
{
    private static readonly HashSet<string> ValidCategories = new()
        { "Travel", "Equipment", "Printing", "ThirdPartyFees", "GovernmentCharges", "Miscellaneous" };
    private static readonly HashSet<string> AllowedReceiptExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".jpg", ".jpeg", ".png" };

    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<ExpenseService> _logger;

    public ExpenseService(ApplicationDbContext context, IScopedAccessService access, IFileStorageService fileStorage, ILogger<ExpenseService> logger)
    {
        _context = context;
        _access = access;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<Expense> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, ExpenseRequest request)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "expense", "create", workspaceId);
        ValidateCategory(request.Category);

        var expense = new Expense
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            JobId = jobId,
            Category = request.Category,
            Amount = request.Amount,
            Description = request.Description,
            IncurredDate = request.IncurredDate,
            RecordedBy = callerUserId,
            CreatedAt = DateTime.UtcNow
        };

        await _context.Expenses.AddAsync(expense);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Expense {ExpenseId} recorded for job {JobId} by {UserId}", expense.Id, jobId, callerUserId);
        return expense;
    }

    public async Task<List<Expense>> GetAllAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "expense", "view", workspaceId);

        return await _context.Expenses.Include(e => e.RecordedByUser)
            .Where(e => e.JobId == jobId)
            .OrderByDescending(e => e.IncurredDate)
            .ToListAsync();
    }

    public async Task<Expense> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "expense", "view", workspaceId);
        return await FindExpenseAsync(jobId, expenseId);
    }

    public async Task<Expense> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId, ExpenseRequest request)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "expense", "edit", workspaceId);
        ValidateCategory(request.Category);
        var expense = await FindExpenseAsync(jobId, expenseId);

        expense.Category = request.Category;
        expense.Amount = request.Amount;
        expense.Description = request.Description;
        expense.IncurredDate = request.IncurredDate;

        await _context.SaveChangesAsync();
        return expense;
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "expense", "delete", workspaceId);
        var expense = await FindExpenseAsync(jobId, expenseId);

        if (expense.ReceiptFilePath != null)
            await _fileStorage.DeleteAsync(expense.ReceiptFilePath, CancellationToken.None);

        _context.Expenses.Remove(expense);
        await _context.SaveChangesAsync();
    }

    public async Task<Expense> UploadReceiptAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId, IFormFile file)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "expense", "edit", workspaceId);
        var expense = await FindExpenseAsync(jobId, expenseId);

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedReceiptExtensions.Contains(extension))
            throw new ValidationException($"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", AllowedReceiptExtensions)}.");
        if (file.Length > DocumentService.MaxFileSizeBytes)
            throw new ValidationException($"File exceeds the {DocumentService.MaxFileSizeBytes / (1024 * 1024)}MB size limit.");

        var storedFileName = $"{Guid.NewGuid():N}_{file.FileName}";
        var relativePath = $"{workspaceId}/jobs/{jobId}/expenses/{expenseId}/{storedFileName}";

        await using (var stream = file.OpenReadStream())
        {
            await _fileStorage.SaveAsync(stream, relativePath, CancellationToken.None);
        }

        expense.ReceiptFilePath = relativePath;
        await _context.SaveChangesAsync();
        return expense;
    }

    public async Task<(Expense expense, Stream content)> GetReceiptFileAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid expenseId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "expense", "view", workspaceId);
        var expense = await FindExpenseAsync(jobId, expenseId);

        if (expense.ReceiptFilePath == null)
            throw new NotFoundException("No receipt uploaded for this expense.");

        var content = await _fileStorage.OpenAsync(expense.ReceiptFilePath, CancellationToken.None);
        return (expense, content);
    }

    private static void ValidateCategory(string category)
    {
        if (!ValidCategories.Contains(category))
            throw new ValidationException($"Category must be one of: {string.Join(", ", ValidCategories)}.");
    }

    private async Task<Job> FindJobAsync(Guid workspaceId, Guid jobId)
    {
        return await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Job not found");
    }

    private async Task<Expense> FindExpenseAsync(Guid jobId, Guid expenseId)
    {
        return await _context.Expenses.Include(e => e.RecordedByUser)
            .FirstOrDefaultAsync(e => e.Id == expenseId && e.JobId == jobId)
            ?? throw new NotFoundException("Expense not found");
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter ExpenseServiceTests`
Expected: PASS (all 6 tests).

- [ ] **Step 6: Write the controller**

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Expense;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/job/{jobId}/expense")]
    [Authorize]
    public class ExpenseController : ControllerBase
    {
        private readonly IExpenseService _expenseService;

        public ExpenseController(IExpenseService expenseService)
        {
            _expenseService = expenseService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<ExpenseResponse>>>> GetAll(Guid workspaceId, Guid jobId)
        {
            var expenses = await _expenseService.GetAllAsync(workspaceId, CallerId(), jobId);
            return Ok(ApiResponse<List<ExpenseResponse>>.Ok(expenses.Select(ToResponse).ToList()));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<ExpenseResponse>>> GetById(Guid workspaceId, Guid jobId, Guid id)
        {
            var expense = await _expenseService.GetByIdAsync(workspaceId, CallerId(), jobId, id);
            return Ok(ApiResponse<ExpenseResponse>.Ok(ToResponse(expense)));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<ExpenseResponse>>> Create(Guid workspaceId, Guid jobId, [FromBody] ExpenseRequest request)
        {
            var expense = await _expenseService.CreateAsync(workspaceId, CallerId(), jobId, request);
            return CreatedAtAction(nameof(GetById), new { workspaceId, jobId, id = expense.Id }, ApiResponse<ExpenseResponse>.Ok(ToResponse(expense)));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<ExpenseResponse>>> Update(Guid workspaceId, Guid jobId, Guid id, [FromBody] ExpenseRequest request)
        {
            var expense = await _expenseService.UpdateAsync(workspaceId, CallerId(), jobId, id, request);
            return Ok(ApiResponse<ExpenseResponse>.Ok(ToResponse(expense)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid workspaceId, Guid jobId, Guid id)
        {
            await _expenseService.DeleteAsync(workspaceId, CallerId(), jobId, id);
            return NoContent();
        }

        [HttpPost("{id}/receipt")]
        [RequestSizeLimit(DocumentService.MaxFileSizeBytes)]
        public async Task<ActionResult<ApiResponse<ExpenseResponse>>> UploadReceipt(Guid workspaceId, Guid jobId, Guid id, IFormFile file)
        {
            var expense = await _expenseService.UploadReceiptAsync(workspaceId, CallerId(), jobId, id, file);
            return Ok(ApiResponse<ExpenseResponse>.Ok(ToResponse(expense)));
        }

        [HttpGet("{id}/receipt")]
        public async Task<IActionResult> GetReceipt(Guid workspaceId, Guid jobId, Guid id)
        {
            var (expense, content) = await _expenseService.GetReceiptFileAsync(workspaceId, CallerId(), jobId, id);
            return File(content, "application/octet-stream", Path.GetFileName(expense.ReceiptFilePath!));
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        private static ExpenseResponse ToResponse(Expense e) => new()
        {
            ExpenseId = e.Id,
            JobId = e.JobId,
            Category = e.Category,
            Amount = e.Amount,
            Description = e.Description,
            IncurredDate = e.IncurredDate,
            HasReceipt = e.ReceiptFilePath != null,
            RecordedByName = $"{e.RecordedByUser.FirstName} {e.RecordedByUser.LastName}",
            CreatedAt = e.CreatedAt
        };
    }
}
```

- [ ] **Step 7: Register the service**

In `api/src/SurveyorLedger.API/Program.cs`, add next to the billing service
registrations:
```csharp
builder.Services.AddScoped<IExpenseService, ExpenseService>();
```

- [ ] **Step 8: Build and test**

Run:
```bash
cd api && dotnet build
cd api && dotnet test --filter ExpenseServiceTests
```
Expected: build succeeds, all 6 tests pass.

- [ ] **Step 9: Commit**

```bash
git add api/src/SurveyorLedger.API/Models/Expense/ api/src/SurveyorLedger.API/Services/ExpenseService.cs api/src/SurveyorLedger.API/Controllers/ExpenseController.cs api/src/SurveyorLedger.API/Program.cs api/tests/SurveyorLedger.API.Tests/Services/ExpenseServiceTests.cs
git commit -m "feat: add Expense CRUD service, receipt upload, and API"
```

---

### Task 4: StaffPaymentService + StaffPaymentController (CRUD, own-only visibility)

**Files:**
- Create: `api/src/SurveyorLedger.API/Models/StaffPayment/StaffPaymentDtos.cs`
- Create: `api/src/SurveyorLedger.API/Services/StaffPaymentService.cs`
- Create: `api/src/SurveyorLedger.API/Controllers/StaffPaymentController.cs`
- Modify: `api/src/SurveyorLedger.API/Program.cs`
- Create: `api/tests/SurveyorLedger.API.Tests/Services/StaffPaymentServiceTests.cs`

**Interfaces:**
- Consumes: `StaffPayment` entity (Task 1), `IScopedAccessService.EnsureAllowedAsync`,
  `IScopedAccessService.HasViewAllAsync` (for the own-only filter).
- Produces: `IStaffPaymentService` with `CreateAsync`, `GetAllAsync`,
  `GetByIdAsync`, `UpdateAsync`, `DeleteAsync`.

- [ ] **Step 1: Write the DTOs**

```csharp
namespace SurveyorLedger.API.Models.StaffPayment;

public class StaffPaymentRequest
{
    public Guid UserId { get; set; }
    public string Type { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidDate { get; set; }
    public string? Notes { get; set; }
}

public class StaffPaymentResponse
{
    public Guid StaffPaymentId { get; set; }
    public Guid JobId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; }
    public string Type { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidDate { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 2: Write the failing service tests**

```csharp
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.StaffPayment;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class StaffPaymentServiceTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IStaffPaymentService _staffPaymentService = null!;
    private Guid _jobId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IStaffPaymentService, StaffPaymentService>();
    }

    private async Task SeedJobAsync()
    {
        _jobService = GetService<IJobService>();
        _staffPaymentService = GetService<IStaffPaymentService>();
        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Survey Job" });
        _jobId = job.Id;
    }

    [Fact]
    public async Task CreateAsync_PersistsStaffPayment()
    {
        await SeedJobAsync();
        var payment = await _staffPaymentService.CreateAsync(WorkspaceId, AdminId, _jobId, new StaffPaymentRequest
        {
            UserId = SurveyorId,
            Type = "Salary",
            Amount = 30000m,
            PaidDate = DateTime.UtcNow
        });

        Assert.Equal("Salary", payment.Type);
    }

    [Fact]
    public async Task CreateAsync_UnknownUserId_Rejected()
    {
        await SeedJobAsync();
        await Assert.ThrowsAsync<ValidationException>(() => _staffPaymentService.CreateAsync(WorkspaceId, AdminId, _jobId, new StaffPaymentRequest
        {
            UserId = Guid.NewGuid(),
            Type = "Bonus",
            Amount = 1000m,
            PaidDate = DateTime.UtcNow
        }));
    }

    [Fact]
    public async Task Surveyor_CannotCreateStaffPayment()
    {
        await SeedJobAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => _staffPaymentService.CreateAsync(WorkspaceId, SurveyorId, _jobId, new StaffPaymentRequest
        {
            UserId = SurveyorId,
            Type = "Salary",
            Amount = 1000m,
            PaidDate = DateTime.UtcNow
        }));
    }

    [Fact]
    public async Task Surveyor_SeesOnlyOwnPayments()
    {
        await SeedJobAsync();
        await _staffPaymentService.CreateAsync(WorkspaceId, AdminId, _jobId, new StaffPaymentRequest { UserId = SurveyorId, Type = "Salary", Amount = 30000m, PaidDate = DateTime.UtcNow });
        await _staffPaymentService.CreateAsync(WorkspaceId, AdminId, _jobId, new StaffPaymentRequest { UserId = AdminId, Type = "Bonus", Amount = 5000m, PaidDate = DateTime.UtcNow });

        var surveyorView = await _staffPaymentService.GetAllAsync(WorkspaceId, SurveyorId, _jobId);
        Assert.Single(surveyorView);
        Assert.All(surveyorView, p => Assert.Equal(SurveyorId, p.UserId));

        var adminView = await _staffPaymentService.GetAllAsync(WorkspaceId, AdminId, _jobId);
        Assert.Equal(2, adminView.Count);
    }

    [Fact]
    public async Task DeleteAsync_RemovesStaffPayment()
    {
        await SeedJobAsync();
        var payment = await _staffPaymentService.CreateAsync(WorkspaceId, AdminId, _jobId, new StaffPaymentRequest { UserId = SurveyorId, Type = "Commission", Amount = 1000m, PaidDate = DateTime.UtcNow });
        await _staffPaymentService.DeleteAsync(WorkspaceId, AdminId, _jobId, payment.Id);

        var all = await _staffPaymentService.GetAllAsync(WorkspaceId, AdminId, _jobId);
        Assert.DoesNotContain(all, p => p.Id == payment.Id);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter StaffPaymentServiceTests`
Expected: FAIL — `IStaffPaymentService`/`StaffPaymentService` do not exist yet.

- [ ] **Step 4: Write the service**

```csharp
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.StaffPayment;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IStaffPaymentService
{
    Task<StaffPayment> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, StaffPaymentRequest request);
    Task<List<StaffPayment>> GetAllAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
    Task<StaffPayment> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid staffPaymentId);
    Task<StaffPayment> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid staffPaymentId, StaffPaymentRequest request);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid staffPaymentId);
}

/// <summary>
/// Own-only visibility for callers without staffpayment.view_all: filtered here in C#
/// (same shape as ScopedAccessService.AccessibleLandIds), not in Casbin, which can only
/// answer "may this role do this action" - not "which specific rows".
/// </summary>
public class StaffPaymentService : IStaffPaymentService
{
    private static readonly HashSet<string> ValidTypes = new() { "Salary", "Commission", "Bonus", "ProfitShare" };

    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly ILogger<StaffPaymentService> _logger;

    public StaffPaymentService(ApplicationDbContext context, IScopedAccessService access, ILogger<StaffPaymentService> logger)
    {
        _context = context;
        _access = access;
        _logger = logger;
    }

    public async Task<StaffPayment> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, StaffPaymentRequest request)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "staffpayment", "create", workspaceId);
        ValidateType(request.Type);
        await ValidateUserAsync(request.UserId);

        var payment = new StaffPayment
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            JobId = jobId,
            UserId = request.UserId,
            Type = request.Type,
            Amount = request.Amount,
            PaidDate = request.PaidDate,
            Notes = request.Notes,
            RecordedBy = callerUserId,
            CreatedAt = DateTime.UtcNow
        };

        await _context.StaffPayments.AddAsync(payment);
        await _context.SaveChangesAsync();

        _logger.LogInformation("StaffPayment {StaffPaymentId} recorded for job {JobId} user {UserId} by {CallerId}", payment.Id, jobId, request.UserId, callerUserId);
        return payment;
    }

    public async Task<List<StaffPayment>> GetAllAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "staffpayment", "view", workspaceId);

        var query = _context.StaffPayments.Include(p => p.User).Where(p => p.JobId == jobId);

        if (!await _access.HasViewAllAsync(callerUserId, "staffpayment", workspaceId))
            query = query.Where(p => p.UserId == callerUserId);

        return await query.OrderByDescending(p => p.PaidDate).ToListAsync();
    }

    public async Task<StaffPayment> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid staffPaymentId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "staffpayment", "view", workspaceId);
        var payment = await FindStaffPaymentAsync(jobId, staffPaymentId);

        if (payment.UserId != callerUserId && !await _access.HasViewAllAsync(callerUserId, "staffpayment", workspaceId))
            throw new NotFoundException("Staff payment not found");

        return payment;
    }

    public async Task<StaffPayment> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid staffPaymentId, StaffPaymentRequest request)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "staffpayment", "edit", workspaceId);
        ValidateType(request.Type);
        await ValidateUserAsync(request.UserId);
        var payment = await FindStaffPaymentAsync(jobId, staffPaymentId);

        payment.UserId = request.UserId;
        payment.Type = request.Type;
        payment.Amount = request.Amount;
        payment.PaidDate = request.PaidDate;
        payment.Notes = request.Notes;

        await _context.SaveChangesAsync();
        return payment;
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid staffPaymentId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureAllowedAsync(callerUserId, "staffpayment", "delete", workspaceId);
        var payment = await FindStaffPaymentAsync(jobId, staffPaymentId);

        _context.StaffPayments.Remove(payment);
        await _context.SaveChangesAsync();
    }

    private static void ValidateType(string type)
    {
        if (!ValidTypes.Contains(type))
            throw new ValidationException($"Type must be one of: {string.Join(", ", ValidTypes)}.");
    }

    private async Task ValidateUserAsync(Guid userId)
    {
        var exists = await _context.Users.AnyAsync(u => u.Id == userId && u.IsActive);
        if (!exists)
            throw new ValidationException("UserId does not match an existing account.");
    }

    private async Task<Job> FindJobAsync(Guid workspaceId, Guid jobId)
    {
        return await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Job not found");
    }

    private async Task<StaffPayment> FindStaffPaymentAsync(Guid jobId, Guid staffPaymentId)
    {
        return await _context.StaffPayments.Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == staffPaymentId && p.JobId == jobId)
            ?? throw new NotFoundException("Staff payment not found");
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter StaffPaymentServiceTests`
Expected: PASS (all 5 tests).

- [ ] **Step 6: Write the controller**

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Models.StaffPayment;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/job/{jobId}/staff-payment")]
    [Authorize]
    public class StaffPaymentController : ControllerBase
    {
        private readonly IStaffPaymentService _staffPaymentService;

        public StaffPaymentController(IStaffPaymentService staffPaymentService)
        {
            _staffPaymentService = staffPaymentService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<StaffPaymentResponse>>>> GetAll(Guid workspaceId, Guid jobId)
        {
            var payments = await _staffPaymentService.GetAllAsync(workspaceId, CallerId(), jobId);
            return Ok(ApiResponse<List<StaffPaymentResponse>>.Ok(payments.Select(ToResponse).ToList()));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<StaffPaymentResponse>>> GetById(Guid workspaceId, Guid jobId, Guid id)
        {
            var payment = await _staffPaymentService.GetByIdAsync(workspaceId, CallerId(), jobId, id);
            return Ok(ApiResponse<StaffPaymentResponse>.Ok(ToResponse(payment)));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<StaffPaymentResponse>>> Create(Guid workspaceId, Guid jobId, [FromBody] StaffPaymentRequest request)
        {
            var payment = await _staffPaymentService.CreateAsync(workspaceId, CallerId(), jobId, request);
            return CreatedAtAction(nameof(GetById), new { workspaceId, jobId, id = payment.Id }, ApiResponse<StaffPaymentResponse>.Ok(ToResponse(payment)));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<StaffPaymentResponse>>> Update(Guid workspaceId, Guid jobId, Guid id, [FromBody] StaffPaymentRequest request)
        {
            var payment = await _staffPaymentService.UpdateAsync(workspaceId, CallerId(), jobId, id, request);
            return Ok(ApiResponse<StaffPaymentResponse>.Ok(ToResponse(payment)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid workspaceId, Guid jobId, Guid id)
        {
            await _staffPaymentService.DeleteAsync(workspaceId, CallerId(), jobId, id);
            return NoContent();
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        private static StaffPaymentResponse ToResponse(StaffPayment p) => new()
        {
            StaffPaymentId = p.Id,
            JobId = p.JobId,
            UserId = p.UserId,
            UserName = $"{p.User.FirstName} {p.User.LastName}",
            Type = p.Type,
            Amount = p.Amount,
            PaidDate = p.PaidDate,
            Notes = p.Notes,
            CreatedAt = p.CreatedAt
        };
    }
}
```

- [ ] **Step 7: Register the service**

In `api/src/SurveyorLedger.API/Program.cs`, add:
```csharp
builder.Services.AddScoped<IStaffPaymentService, StaffPaymentService>();
```

- [ ] **Step 8: Build and run the full new-test suite**

Run:
```bash
cd api && dotnet build
cd api && dotnet test --filter "ExpenseServiceTests|StaffPaymentServiceTests"
```
Expected: build succeeds, all 11 tests pass.

- [ ] **Step 9: Commit**

```bash
git add api/src/SurveyorLedger.API/Models/StaffPayment/ api/src/SurveyorLedger.API/Services/StaffPaymentService.cs api/src/SurveyorLedger.API/Controllers/StaffPaymentController.cs api/src/SurveyorLedger.API/Program.cs api/tests/SurveyorLedger.API.Tests/Services/StaffPaymentServiceTests.cs
git commit -m "feat: add StaffPayment CRUD service with own-only visibility, and API"
```

- [ ] **Step 10: Run the api-layer-review skill**

Invoke `api-layer-review` against the full diff (Tasks 1-4) before considering
the phase done.

- [ ] **Step 11: Run the full test suite**

Run: `cd api && dotnet test`
Expected: 0 unexpected failures (any pre-existing flaky test, if it reproduces,
should be re-run in isolation to confirm it's not a real regression, same as the
billing-core phase's `InvitationFlowTests` flake).

---

## Self-Review Notes

**Spec coverage:** fixed expense categories (Task 1's `Expense.Category` +
`ExpenseService.ValidCategories`), no approval workflow (no `Status` field
anywhere), manual staff payment amount with type label (Task 1's
`StaffPayment.Type`/`Amount`, no calc logic), optional receipt upload (Task 3
Step 4/6), required `JobId` on both entities, RBAC resources `expense`/
`staffpayment` distinct from job permissions, Surveyor own-only StaffPayment
visibility via `view_all` - all covered.

**Route convention correction applied during planning:** the design spec said
`/jobs/{jobId}/expenses` (plural) — verified against the actual
`MilestoneController` route and corrected to singular
`/job/{jobId}/expense` / `/staff-payment` throughout.
