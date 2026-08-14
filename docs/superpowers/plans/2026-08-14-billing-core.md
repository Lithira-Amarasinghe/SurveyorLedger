# Billing Core (Revenue & Collection) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the backend (DB + Services + API) for Client, Quotation, Invoice, and
Payment — the foundation phase for SurveyorLedger's billing feature, per
`docs/superpowers/specs/2026-08-14-billing-core-design.md`.

**Architecture:** Four new EF Core entities (`Client`, `Quotation`, `Invoice`,
`Payment`), each workspace-scoped directly via `WorkspaceId`. Follows the existing
Controller → Service → Data layering used by `Land`/`Job`. Line items are owned
collections (no separate table exposed via API). Status and money totals
(`Total`, `AmountPaid`, `Balance`, `DaysOverdue`) are computed in the service layer,
never stored redundantly.

**Tech Stack:** .NET 9, EF Core 9, SQL Server LocalDB, xUnit (integration tests
against a throwaway LocalDB per test class, via `WorkspaceIntegrationTestBase`).

## Global Constraints

- Tenant isolation: every query filters by `WorkspaceId` — no exceptions.
- Migrations are generated via `dotnet ef migrations add`, never hand-edited, except
  for pure data-seed migrations (permissions), which follow the existing convention
  of an empty generated migration with hand-written `InsertData`/`DeleteData` in
  `Up`/`Down` (see `20260809162247_SeedLandPermissions.cs`).
- Cross-entity references not belonging to the caller's `WorkspaceId` return 404
  (`NotFoundException`), not 403.
- All new service methods take `(Guid workspaceId, Guid callerUserId, ...)` as the
  first two parameters and call `IScopedAccessService.EnsureAllowedAsync` /
  `EnsureListAllowedAsync` first, mirroring `LandService`.
- Money fields use `decimal(18,2)`. Tax/discount rates use `decimal(5,2)`.
- No UI in this plan — DB, Services, API only.

---

### Task 1: Client, Quotation, Invoice, Payment entities + EF configuration + migration

**Files:**
- Create: `api/src/SurveyorLedger.Data/Entities/Client.cs`
- Create: `api/src/SurveyorLedger.Data/Entities/Quotation.cs`
- Create: `api/src/SurveyorLedger.Data/Entities/QuotationLineItem.cs`
- Create: `api/src/SurveyorLedger.Data/Entities/Invoice.cs`
- Create: `api/src/SurveyorLedger.Data/Entities/InvoiceLineItem.cs`
- Create: `api/src/SurveyorLedger.Data/Entities/Payment.cs`
- Create: `api/src/SurveyorLedger.Data/Configurations/ClientConfiguration.cs`
- Create: `api/src/SurveyorLedger.Data/Configurations/QuotationConfiguration.cs`
- Create: `api/src/SurveyorLedger.Data/Configurations/InvoiceConfiguration.cs`
- Create: `api/src/SurveyorLedger.Data/Configurations/PaymentConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/ApplicationDbContext.cs`
- Create (generated): `api/src/SurveyorLedger.Data/Migrations/<timestamp>_AddBillingCore.cs`

**Interfaces:**
- Produces: `Client { Id, WorkspaceId, Name, Phone, Email, Address, IsActive, CreatedAt, UpdatedAt }`
- Produces: `Quotation { Id, WorkspaceId, ClientId, JobId?, Number, LineItems: List<QuotationLineItem>, TaxRatePercent, Status (string), ValidUntil?, RevisionNumber, IsActive, CreatedAt, UpdatedAt }`
- Produces: `QuotationLineItem { Id, Description, Quantity, UnitPrice }`
- Produces: `Invoice { Id, WorkspaceId, ClientId, JobId?, QuotationId?, Number, LineItems: List<InvoiceLineItem>, TaxRatePercent, DiscountAmount, Status (string), DueDate?, IsActive, CreatedAt, UpdatedAt, Payments: ICollection<Payment> }`
- Produces: `InvoiceLineItem { Id, Description, Quantity, UnitPrice }`
- Produces: `Payment { Id, WorkspaceId, InvoiceId, Amount, Method (string), ReceivedAt, ReferenceNumber?, ProofFilePath?, ReceiptNumber, RecordedBy, CreatedAt }`

- [ ] **Step 1: Write the entities**

`api/src/SurveyorLedger.Data/Entities/Client.cs`:
```csharp
namespace SurveyorLedger.Data.Entities;

public class Client
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public Address Address { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Workspace Workspace { get; set; }
}
```

`api/src/SurveyorLedger.Data/Entities/QuotationLineItem.cs`:
```csharp
namespace SurveyorLedger.Data.Entities;

public class QuotationLineItem
{
    public Guid Id { get; set; }
    public string Description { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
```

`api/src/SurveyorLedger.Data/Entities/Quotation.cs`:
```csharp
namespace SurveyorLedger.Data.Entities;

/// <summary>
/// Draft/Sent/Accepted/Rejected/Expired. RevisionNumber bumps whenever line items are
/// edited after Status has reached Sent - covers "revision charges" without a new entity.
/// </summary>
public class Quotation
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ClientId { get; set; }
    public Guid? JobId { get; set; }
    public string Number { get; set; }
    public List<QuotationLineItem> LineItems { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime? ValidUntil { get; set; }
    public int RevisionNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Workspace Workspace { get; set; }
    public Client Client { get; set; }
    public Job? Job { get; set; }
}
```

`api/src/SurveyorLedger.Data/Entities/InvoiceLineItem.cs`:
```csharp
namespace SurveyorLedger.Data.Entities;

public class InvoiceLineItem
{
    public Guid Id { get; set; }
    public string Description { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
```

`api/src/SurveyorLedger.Data/Entities/Invoice.cs`:
```csharp
namespace SurveyorLedger.Data.Entities;

/// <summary>
/// Draft/Sent/PartiallyPaid/Paid/Overdue/Cancelled. Total/AmountPaid/Balance/DaysOverdue
/// are computed by InvoiceService from LineItems and Payments, never stored - see
/// InvoiceService.ToComputed for the single source of truth.
/// </summary>
public class Invoice
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ClientId { get; set; }
    public Guid? JobId { get; set; }
    public Guid? QuotationId { get; set; }
    public string Number { get; set; }
    public List<InvoiceLineItem> LineItems { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Workspace Workspace { get; set; }
    public Client Client { get; set; }
    public Job? Job { get; set; }
    public Quotation? Quotation { get; set; }
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
```

`api/src/SurveyorLedger.Data/Entities/Payment.cs`:
```csharp
namespace SurveyorLedger.Data.Entities;

public class Payment
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? ProofFilePath { get; set; }
    public string ReceiptNumber { get; set; }
    public Guid RecordedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Invoice Invoice { get; set; }
    public User RecordedByUser { get; set; }
}
```

- [ ] **Step 2: Write the EF configurations**

`api/src/SurveyorLedger.Data/Configurations/ClientConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(30);
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.OwnsOne(x => x.Address, a =>
        {
            a.Property(p => p.Street).HasMaxLength(255).HasColumnName("Street");
            a.Property(p => p.City).HasMaxLength(100).HasColumnName("City");
            a.Property(p => p.District).HasMaxLength(100).HasColumnName("District");
            a.Property(p => p.PostalCode).HasMaxLength(20).HasColumnName("PostalCode");
            a.Property(p => p.Country).HasMaxLength(100).HasColumnName("Country");
        });

        builder.HasIndex(x => x.WorkspaceId);
        builder.HasIndex(x => x.IsActive);

        builder.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

`api/src/SurveyorLedger.Data/Configurations/QuotationConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class QuotationConfiguration : IEntityTypeConfiguration<Quotation>
{
    public void Configure(EntityTypeBuilder<Quotation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TaxRatePercent).HasColumnType("decimal(5,2)");
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.OwnsMany(x => x.LineItems, li =>
        {
            li.ToTable("QuotationLineItems");
            li.WithOwner().HasForeignKey("QuotationId");
            li.HasKey(x => x.Id);
            li.Property(x => x.Description).HasMaxLength(500).IsRequired();
            li.Property(x => x.Quantity).HasColumnType("decimal(18,2)");
            li.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
        });

        builder.HasIndex(x => x.WorkspaceId);
        builder.HasIndex(x => new { x.WorkspaceId, x.Number }).IsUnique();
        builder.HasIndex(x => x.IsActive);

        builder.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

`api/src/SurveyorLedger.Data/Configurations/InvoiceConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TaxRatePercent).HasColumnType("decimal(5,2)");
        builder.Property(x => x.DiscountAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.OwnsMany(x => x.LineItems, li =>
        {
            li.ToTable("InvoiceLineItems");
            li.WithOwner().HasForeignKey("InvoiceId");
            li.HasKey(x => x.Id);
            li.Property(x => x.Description).HasMaxLength(500).IsRequired();
            li.Property(x => x.Quantity).HasColumnType("decimal(18,2)");
            li.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
        });

        builder.HasIndex(x => x.WorkspaceId);
        builder.HasIndex(x => new { x.WorkspaceId, x.Number }).IsUnique();
        builder.HasIndex(x => x.IsActive);

        builder.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Quotation).WithMany().HasForeignKey(x => x.QuotationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Payments).WithOne(x => x.Invoice).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

`api/src/SurveyorLedger.Data/Configurations/PaymentConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Method).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ReferenceNumber).HasMaxLength(100);
        builder.Property(x => x.ProofFilePath).HasMaxLength(500);
        builder.Property(x => x.ReceiptNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.WorkspaceId);
        builder.HasIndex(x => new { x.WorkspaceId, x.ReceiptNumber }).IsUnique();

        builder.HasOne(x => x.RecordedByUser).WithMany().HasForeignKey(x => x.RecordedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 3: Register DbSets on ApplicationDbContext**

In `api/src/SurveyorLedger.Data/ApplicationDbContext.cs`, alongside the existing
`DbSet<Land> Lands` / `DbSet<Job> Jobs` block, add:
```csharp
public DbSet<Client> Clients { get; set; }
public DbSet<Quotation> Quotations { get; set; }
public DbSet<Invoice> Invoices { get; set; }
public DbSet<Payment> Payments { get; set; }
```

- [ ] **Step 4: Generate the migration**

Run:
```bash
cd api && dotnet ef migrations add AddBillingCore --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```
Expected: a new migration file under `api/src/SurveyorLedger.Data/Migrations/`
creating `Clients`, `Quotations`, `QuotationLineItems`, `Invoices`,
`InvoiceLineItems`, `Payments` tables. Do not hand-edit the generated file — if it's
wrong, fix the entity/configuration and regenerate.

- [ ] **Step 5: Apply the migration and verify it builds**

Run:
```bash
cd api && dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
cd api && dotnet build
```
Expected: both commands succeed with no errors.

- [ ] **Step 6: Run the migration-check skill**

Invoke the `migration-check` skill against the new migration to confirm it matches
the entities, tenant filtering is intact, and naming follows convention. Fix any
findings before proceeding.

- [ ] **Step 7: Commit**

```bash
git add api/src/SurveyorLedger.Data/Entities/Client.cs api/src/SurveyorLedger.Data/Entities/Quotation.cs api/src/SurveyorLedger.Data/Entities/QuotationLineItem.cs api/src/SurveyorLedger.Data/Entities/Invoice.cs api/src/SurveyorLedger.Data/Entities/InvoiceLineItem.cs api/src/SurveyorLedger.Data/Entities/Payment.cs api/src/SurveyorLedger.Data/Configurations/ClientConfiguration.cs api/src/SurveyorLedger.Data/Configurations/QuotationConfiguration.cs api/src/SurveyorLedger.Data/Configurations/InvoiceConfiguration.cs api/src/SurveyorLedger.Data/Configurations/PaymentConfiguration.cs api/src/SurveyorLedger.Data/ApplicationDbContext.cs api/src/SurveyorLedger.Data/Migrations/
git commit -m "feat: add Client, Quotation, Invoice, Payment entities"
```

---

### Task 2: Seed RBAC permissions for billing resources

**Files:**
- Modify: `api/src/SurveyorLedger.Core/Constants.cs`
- Create (generated + hand-edited data): `api/src/SurveyorLedger.Data/Migrations/<timestamp>_SeedBillingPermissions.cs`

**Interfaces:**
- Consumes: `RoleConfiguration.AdminRoleId`/`SurveyorRoleId`/`ClientRoleId`/`MemberRoleId`
  (`api/src/SurveyorLedger.Data/Configurations/RoleConfiguration.cs`)
- Produces: permission strings `client.view`/`client.create`/`client.edit`/`client.delete`,
  `quotation.view`/`.create`/`.edit`/`.delete`, `invoice.view`/`.create`/`.edit`/`.delete`
  — used as the `resource`/`action` pair in every new service's
  `EnsureAllowedAsync(userId, "client"|"quotation"|"invoice", action, workspaceId)` call.

- [ ] **Step 1: Generate the empty migration**

Run:
```bash
cd api && dotnet ef migrations add SeedBillingPermissions --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```
Expected: an empty migration (no schema changes — no entity changed) is created.

- [ ] **Step 2: Hand-write the seed data**

Replace the generated `Up`/`Down` bodies in
`api/src/SurveyorLedger.Data/Migrations/<timestamp>_SeedBillingPermissions.cs`,
following the exact pattern of `20260809162247_SeedLandPermissions.cs`. Permission
IDs `117`–`128` (next free after `116`), RolePermission IDs `242`–`268` (next free
after `241`):

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedBillingPermissions : Migration
    {
        private static readonly DateTime SeededAt = new(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        // (Id, Action, Description, Name, Resource)
        private static readonly (int Id, string Action, string Description, string Name, string Resource)[] Perms =
        {
            (117, "view",   "View clients.",              "client.view",    "client"),
            (118, "create", "Create clients.",             "client.create",  "client"),
            (119, "edit",   "Edit clients.",                "client.edit",    "client"),
            (120, "delete", "Delete clients.",              "client.delete",  "client"),
            (121, "view",   "View quotations.",             "quotation.view",   "quotation"),
            (122, "create", "Create quotations.",           "quotation.create", "quotation"),
            (123, "edit",   "Edit quotations.",              "quotation.edit",   "quotation"),
            (124, "delete", "Delete quotations.",            "quotation.delete", "quotation"),
            (125, "view",   "View invoices and payments.",   "invoice.view",   "invoice"),
            (126, "create", "Create invoices and record payments.", "invoice.create", "invoice"),
            (127, "edit",   "Edit invoices.",                 "invoice.edit",   "invoice"),
            (128, "delete", "Delete/cancel invoices.",        "invoice.delete", "invoice"),
        };

        private static Guid PermId(int n) => new($"00000000-0000-0000-0000-{n:000000000000}");
        private static Guid RolePermId(int n) => new($"00000000-0000-0000-0000-{n:000000000000}");

        private static readonly Guid AdminRoleId = new("00000000-0000-0000-0000-000000000001");
        private static readonly Guid SurveyorRoleId = new("00000000-0000-0000-0000-000000000003");
        private static readonly Guid ClientRoleId = new("00000000-0000-0000-0000-000000000004");
        private static readonly Guid MemberRoleId = new("00000000-0000-0000-0000-000000000005");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Action", "CreatedAt", "Description", "Name", "Resource", "Scope" },
                values: Perms.Select(p => new object[] { PermId(p.Id), p.Action, SeededAt, p.Description, p.Name, p.Resource, null }).ToArray());

            // Admin: full CRUD on all three resources. Surveyor: view/create/edit, no
            // delete. Client and Member: view only - billing data is financial, phase 1
            // does not build a client-scoped billing portal.
            var rolePermissions = new List<(int RolePermId, int PermId, Guid RoleId)>();
            var nextId = 242;
            foreach (var perm in Perms)
                rolePermissions.Add((nextId++, perm.Id, AdminRoleId));
            foreach (var perm in Perms.Where(p => p.Action != "delete"))
                rolePermissions.Add((nextId++, perm.Id, SurveyorRoleId));
            foreach (var perm in Perms.Where(p => p.Action == "view"))
                rolePermissions.Add((nextId++, perm.Id, ClientRoleId));
            foreach (var perm in Perms.Where(p => p.Action == "view"))
                rolePermissions.Add((nextId++, perm.Id, MemberRoleId));

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "Id", "CreatedAt", "PermissionId", "RoleId" },
                values: rolePermissions.Select(rp => new object[] { RolePermId(rp.RolePermId), SeededAt, PermId(rp.PermId), rp.RoleId }).ToArray());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            for (var i = 242; i < 269; i++)
                migrationBuilder.DeleteData(table: "RolePermissions", keyColumn: "Id", keyValue: RolePermId(i));

            foreach (var perm in Perms)
                migrationBuilder.DeleteData(table: "Permissions", keyColumn: "Id", keyValue: PermId(perm.Id));
        }
    }
}
```

Note: `{n:000000000000}` is not a valid .NET format string for a GUID's last
segment — use `new Guid($"00000000-0000-0000-0000-{n:D12}")` instead. Fix both
`PermId` and `RolePermId` to:
```csharp
private static Guid PermId(int n) => new($"00000000-0000-0000-0000-{n:D12}");
private static Guid RolePermId(int n) => new($"00000000-0000-0000-0000-{n:D12}");
```

- [ ] **Step 3: Add resource constants**

In `api/src/SurveyorLedger.Core/Constants.cs`, this codebase passes resource names
as raw strings (`"land"`, `"job"`) to `EnsureAllowedAsync` rather than constants, so
no change is required here — confirmed by reading `LandService.cs`/`JobService.cs`.
Skip this file.

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
git add api/src/SurveyorLedger.Data/Migrations/
git commit -m "feat: seed RBAC permissions for client/quotation/invoice resources"
```

---

### Task 3: ClientService + ClientsController

**Files:**
- Create: `api/src/SurveyorLedger.API/Models/Billing/ClientDtos.cs`
- Create: `api/src/SurveyorLedger.API/Services/ClientService.cs`
- Create: `api/src/SurveyorLedger.API/Controllers/ClientsController.cs`
- Modify: `api/src/SurveyorLedger.API/Program.cs`
- Create: `api/tests/SurveyorLedger.API.Tests/Services/ClientServiceTests.cs`

**Interfaces:**
- Consumes: `Client` entity (Task 1), `IScopedAccessService.EnsureAllowedAsync`,
  `EnsureListAllowedAsync`, `AddressDto` (`api/src/SurveyorLedger.API/Models/Land/LandDtos.cs`
  — reused as-is, same shape).
- Produces: `IClientService` with
  `CreateAsync(Guid workspaceId, Guid callerUserId, ClientRequest) : Task<Client>`,
  `SearchAsync(Guid workspaceId, Guid callerUserId, string? query) : Task<List<Client>>`,
  `GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid clientId) : Task<Client>`,
  `UpdateAsync(Guid workspaceId, Guid callerUserId, Guid clientId, ClientRequest) : Task<Client>`,
  `DeleteAsync(Guid workspaceId, Guid callerUserId, Guid clientId) : Task`.
  These are consumed by `QuotationService`/`InvoiceService` (Tasks 4–5) to validate
  `ClientId` references.

- [ ] **Step 1: Write the DTOs**

`api/src/SurveyorLedger.API/Models/Billing/ClientDtos.cs`:
```csharp
using SurveyorLedger.API.Models.Land;

namespace SurveyorLedger.API.Models.Billing;

public class ClientRequest
{
    public string Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public AddressDto? Address { get; set; }
}

public class ClientResponse
{
    public Guid ClientId { get; set; }
    public string Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public AddressDto Address { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ClientBalanceResponse
{
    public Guid ClientId { get; set; }
    public decimal OutstandingBalance { get; set; }
}
```

- [ ] **Step 2: Write the failing service tests**

`api/tests/SurveyorLedger.API.Tests/Services/ClientServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class ClientServiceTests : WorkspaceIntegrationTestBase
{
    private IClientService _clientService = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IClientService, ClientService>();
    }

    [Fact]
    public async Task CreateAsync_PersistsClient()
    {
        _clientService = GetService<IClientService>();
        var client = await _clientService.CreateAsync(WorkspaceId, AdminId, new ClientRequest { Name = "Acme Ltd", Phone = "0771234567" });

        Assert.Equal("Acme Ltd", client.Name);
        var fetched = await _clientService.GetByIdAsync(WorkspaceId, AdminId, client.Id);
        Assert.Equal(client.Id, fetched.Id);
    }

    [Fact]
    public async Task GetByIdAsync_CrossWorkspace_ThrowsNotFound()
    {
        _clientService = GetService<IClientService>();
        var client = await _clientService.CreateAsync(WorkspaceId, AdminId, new ClientRequest { Name = "Acme Ltd" });

        var otherWorkspaceId = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(
            () => _clientService.GetByIdAsync(otherWorkspaceId, AdminId, client.Id));
    }

    [Fact]
    public async Task Client_CannotCreateClient()
    {
        _clientService = GetService<IClientService>();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _clientService.CreateAsync(WorkspaceId, ClientId, new ClientRequest { Name = "Acme Ltd" }));
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes()
    {
        _clientService = GetService<IClientService>();
        var client = await _clientService.CreateAsync(WorkspaceId, AdminId, new ClientRequest { Name = "Acme Ltd" });
        await _clientService.DeleteAsync(WorkspaceId, AdminId, client.Id);

        var results = await _clientService.SearchAsync(WorkspaceId, AdminId, null);
        Assert.DoesNotContain(results, c => c.Id == client.Id);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter ClientServiceTests`
Expected: FAIL — `IClientService`/`ClientService` do not exist yet.

- [ ] **Step 4: Write the service**

`api/src/SurveyorLedger.API/Services/ClientService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IClientService
{
    Task<Client> CreateAsync(Guid workspaceId, Guid callerUserId, ClientRequest request);
    Task<List<Client>> SearchAsync(Guid workspaceId, Guid callerUserId, string? query);
    Task<Client> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid clientId);
    Task<Client> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid clientId, ClientRequest request);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid clientId);
}

public class ClientService : IClientService
{
    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly ILogger<ClientService> _logger;

    public ClientService(ApplicationDbContext context, IScopedAccessService access, ILogger<ClientService> logger)
    {
        _context = context;
        _access = access;
        _logger = logger;
    }

    public async Task<Client> CreateAsync(Guid workspaceId, Guid callerUserId, ClientRequest request)
    {
        await _access.EnsureAllowedAsync(callerUserId, "client", "create", workspaceId);

        var client = new Client
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = request.Name.Trim(),
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            Address = ToAddress(request.Address),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Clients.AddAsync(client);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Client {ClientId} created in workspace {WorkspaceId} by {UserId}", client.Id, workspaceId, callerUserId);
        return client;
    }

    public async Task<List<Client>> SearchAsync(Guid workspaceId, Guid callerUserId, string? query)
    {
        await _access.EnsureListAllowedAsync(callerUserId, workspaceId);

        var clients = _context.Clients.Where(c => c.WorkspaceId == workspaceId && c.IsActive);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            clients = clients.Where(c =>
                EF.Functions.Like(c.Name, $"%{term}%") ||
                (c.Phone != null && EF.Functions.Like(c.Phone, $"%{term}%")) ||
                (c.Email != null && EF.Functions.Like(c.Email, $"%{term}%")));
        }

        return await clients.OrderByDescending(c => c.CreatedAt).ToListAsync();
    }

    public async Task<Client> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid clientId)
    {
        await _access.EnsureAllowedAsync(callerUserId, "client", "view", workspaceId);
        return await FindClientAsync(workspaceId, clientId);
    }

    public async Task<Client> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid clientId, ClientRequest request)
    {
        await _access.EnsureAllowedAsync(callerUserId, "client", "edit", workspaceId);
        var client = await FindClientAsync(workspaceId, clientId);

        client.Name = request.Name.Trim();
        client.Phone = request.Phone?.Trim();
        client.Email = request.Email?.Trim();
        client.Address = ToAddress(request.Address);
        client.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return client;
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid clientId)
    {
        await _access.EnsureAllowedAsync(callerUserId, "client", "delete", workspaceId);
        var client = await FindClientAsync(workspaceId, clientId);

        client.IsActive = false;
        client.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    internal async Task<Client> FindClientAsync(Guid workspaceId, Guid clientId)
    {
        return await _context.Clients.FirstOrDefaultAsync(c => c.Id == clientId && c.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Client not found");
    }

    private static Address ToAddress(AddressDto? dto) => new()
    {
        Street = dto?.Street,
        City = dto?.City,
        District = dto?.District,
        PostalCode = dto?.PostalCode,
        Country = dto?.Country
    };
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter ClientServiceTests`
Expected: PASS (all 4 tests).

- [ ] **Step 6: Write the controller**

`api/src/SurveyorLedger.API/Controllers/ClientsController.cs`:
```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/clients")]
    [Authorize]
    public class ClientsController : ControllerBase
    {
        private readonly IClientService _clientService;

        public ClientsController(IClientService clientService)
        {
            _clientService = clientService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<ClientResponse>>>> Search(Guid workspaceId, [FromQuery] string? query)
        {
            var clients = await _clientService.SearchAsync(workspaceId, CallerId(), query);
            return Ok(ApiResponse<List<ClientResponse>>.Ok(clients.Select(ToResponse).ToList()));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<ClientResponse>>> Create(Guid workspaceId, [FromBody] ClientRequest request)
        {
            var client = await _clientService.CreateAsync(workspaceId, CallerId(), request);
            return CreatedAtAction(nameof(GetById), new { workspaceId, id = client.Id }, ApiResponse<ClientResponse>.Ok(ToResponse(client)));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<ClientResponse>>> GetById(Guid workspaceId, Guid id)
        {
            var client = await _clientService.GetByIdAsync(workspaceId, CallerId(), id);
            return Ok(ApiResponse<ClientResponse>.Ok(ToResponse(client)));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<ClientResponse>>> Update(Guid workspaceId, Guid id, [FromBody] ClientRequest request)
        {
            var client = await _clientService.UpdateAsync(workspaceId, CallerId(), id, request);
            return Ok(ApiResponse<ClientResponse>.Ok(ToResponse(client)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid workspaceId, Guid id)
        {
            await _clientService.DeleteAsync(workspaceId, CallerId(), id);
            return NoContent();
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        internal static ClientResponse ToResponse(Client c) => new()
        {
            ClientId = c.Id,
            Name = c.Name,
            Phone = c.Phone,
            Email = c.Email,
            Address = new AddressDto
            {
                Street = c.Address.Street,
                City = c.Address.City,
                District = c.Address.District,
                PostalCode = c.Address.PostalCode,
                Country = c.Address.Country
            },
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        };
    }
}
```

- [ ] **Step 7: Register the service in Program.cs**

In `api/src/SurveyorLedger.API/Program.cs`, next to the existing
`builder.Services.AddScoped<ILandService, LandService>();` line, add:
```csharp
builder.Services.AddScoped<IClientService, ClientService>();
```

- [ ] **Step 8: Build and run the full test suite**

Run:
```bash
cd api && dotnet build
cd api && dotnet test --filter ClientServiceTests
```
Expected: build succeeds, all 4 tests pass.

- [ ] **Step 9: Commit**

```bash
git add api/src/SurveyorLedger.API/Models/Billing/ClientDtos.cs api/src/SurveyorLedger.API/Services/ClientService.cs api/src/SurveyorLedger.API/Controllers/ClientsController.cs api/src/SurveyorLedger.API/Program.cs api/tests/SurveyorLedger.API.Tests/Services/ClientServiceTests.cs
git commit -m "feat: add Client CRUD service and API"
```

---

### Task 4: QuotationService + QuotationsController (line items, status, convert-to-invoice, revisions)

**Files:**
- Create: `api/src/SurveyorLedger.API/Models/Billing/QuotationDtos.cs`
- Create: `api/src/SurveyorLedger.API/Services/QuotationService.cs`
- Create: `api/src/SurveyorLedger.API/Controllers/QuotationsController.cs`
- Modify: `api/src/SurveyorLedger.API/Program.cs`
- Create: `api/tests/SurveyorLedger.API.Tests/Services/QuotationServiceTests.cs`

**Interfaces:**
- Consumes: `IClientService.FindClientAsync` (internal, Task 3) to validate `ClientId`;
  `Quotation`/`QuotationLineItem` entities (Task 1); `EnsureAllowedAsync` for resource
  `"quotation"`.
- Produces: `IQuotationService` with
  `CreateAsync(Guid workspaceId, Guid callerUserId, QuotationRequest) : Task<Quotation>`,
  `SearchAsync(Guid workspaceId, Guid callerUserId, Guid? clientId) : Task<List<Quotation>>`,
  `GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid quotationId) : Task<Quotation>`,
  `UpdateAsync(Guid workspaceId, Guid callerUserId, Guid quotationId, QuotationRequest) : Task<Quotation>`,
  `DeleteAsync(Guid workspaceId, Guid callerUserId, Guid quotationId) : Task`,
  `ConvertToInvoiceAsync(Guid workspaceId, Guid callerUserId, Guid quotationId, ConvertQuotationRequest) : Task<Invoice>`.
  `ConvertToInvoiceAsync` is consumed by `QuotationsController` and creates the
  `Invoice` that `InvoiceService` (Task 5) subsequently manages payments for.

- [ ] **Step 1: Write the DTOs**

`api/src/SurveyorLedger.API/Models/Billing/QuotationDtos.cs`:
```csharp
namespace SurveyorLedger.API.Models.Billing;

public class LineItemDto
{
    public string Description { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class QuotationRequest
{
    public Guid ClientId { get; set; }
    public Guid? JobId { get; set; }
    public List<LineItemDto> LineItems { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string? Status { get; set; }
}

public class ConvertQuotationRequest
{
    public DateTime? DueDate { get; set; }
    public decimal DiscountAmount { get; set; }
}

public class QuotationResponse
{
    public Guid QuotationId { get; set; }
    public Guid ClientId { get; set; }
    public Guid? JobId { get; set; }
    public string Number { get; set; }
    public List<LineItemDto> LineItems { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; }
    public DateTime? ValidUntil { get; set; }
    public int RevisionNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

- [ ] **Step 2: Write the failing service tests**

`api/tests/SurveyorLedger.API.Tests/Services/QuotationServiceTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class QuotationServiceTests : WorkspaceIntegrationTestBase
{
    private IClientService _clientService = null!;
    private IQuotationService _quotationService = null!;
    private Guid _clientId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IQuotationService, QuotationService>();
    }

    private async Task SeedClientAsync()
    {
        _clientService = GetService<IClientService>();
        _quotationService = GetService<IQuotationService>();
        var client = await _clientService.CreateAsync(WorkspaceId, AdminId, new ClientRequest { Name = "Acme Ltd" });
        _clientId = client.Id;
    }

    private static QuotationRequest MakeRequest(Guid clientId, string? status = null) => new()
    {
        ClientId = clientId,
        LineItems = new List<LineItemDto> { new() { Description = "Survey", Quantity = 1, UnitPrice = 50000m } },
        TaxRatePercent = 10m,
        Status = status
    };

    [Fact]
    public async Task CreateAsync_ComputesTotalWithTax()
    {
        await SeedClientAsync();
        var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, MakeRequest(_clientId));

        Assert.Equal("Q-0001", quotation.Number);
        Assert.Equal(1, quotation.LineItems.Count);
    }

    [Fact]
    public async Task UpdateAsync_AfterSent_BumpsRevisionNumber()
    {
        await SeedClientAsync();
        var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, MakeRequest(_clientId, "Sent"));
        Assert.Equal(0, quotation.RevisionNumber);

        var updated = await _quotationService.UpdateAsync(WorkspaceId, AdminId, quotation.Id, MakeRequest(_clientId, "Sent"));
        Assert.Equal(1, updated.RevisionNumber);
    }

    [Fact]
    public async Task ConvertToInvoiceAsync_CreatesInvoiceAndMarksAccepted()
    {
        await SeedClientAsync();
        var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, MakeRequest(_clientId, "Sent"));

        var invoice = await _quotationService.ConvertToInvoiceAsync(WorkspaceId, AdminId, quotation.Id, new ConvertQuotationRequest());
        Assert.Equal("INV-0001", invoice.Number);
        Assert.Equal(_clientId, invoice.ClientId);

        var reloaded = await _quotationService.GetByIdAsync(WorkspaceId, AdminId, quotation.Id);
        Assert.Equal("Accepted", reloaded.Status);
    }

    [Fact]
    public async Task ConvertToInvoiceAsync_AlreadyConverted_Throws()
    {
        await SeedClientAsync();
        var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, MakeRequest(_clientId, "Sent"));
        await _quotationService.ConvertToInvoiceAsync(WorkspaceId, AdminId, quotation.Id, new ConvertQuotationRequest());

        await Assert.ThrowsAsync<ValidationException>(
            () => _quotationService.ConvertToInvoiceAsync(WorkspaceId, AdminId, quotation.Id, new ConvertQuotationRequest()));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter QuotationServiceTests`
Expected: FAIL — `IQuotationService`/`QuotationService` do not exist yet.

- [ ] **Step 4: Write the service**

`api/src/SurveyorLedger.API/Services/QuotationService.cs`:
```csharp
using System.Data;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IQuotationService
{
    Task<Quotation> CreateAsync(Guid workspaceId, Guid callerUserId, QuotationRequest request);
    Task<List<Quotation>> SearchAsync(Guid workspaceId, Guid callerUserId, Guid? clientId);
    Task<Quotation> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid quotationId);
    Task<Quotation> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid quotationId, QuotationRequest request);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid quotationId);
    Task<Invoice> ConvertToInvoiceAsync(Guid workspaceId, Guid callerUserId, Guid quotationId, ConvertQuotationRequest request);
}

public class QuotationService : IQuotationService
{
    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly IClientService _clientService;
    private readonly ILogger<QuotationService> _logger;

    public QuotationService(ApplicationDbContext context, IScopedAccessService access, IClientService clientService, ILogger<QuotationService> logger)
    {
        _context = context;
        _access = access;
        _clientService = clientService;
        _logger = logger;
    }

    public async Task<Quotation> CreateAsync(Guid workspaceId, Guid callerUserId, QuotationRequest request)
    {
        await _access.EnsureAllowedAsync(callerUserId, "quotation", "create", workspaceId);
        await _clientService.FindClientAsync(workspaceId, request.ClientId);
        ValidateLineItems(request.LineItems);

        var quotation = new Quotation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ClientId = request.ClientId,
            JobId = request.JobId,
            LineItems = ToEntities(request.LineItems),
            TaxRatePercent = request.TaxRatePercent,
            Status = request.Status ?? "Draft",
            ValidUntil = request.ValidUntil,
            RevisionNumber = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            quotation.Number = await NextNumberAsync(workspaceId, "Q");
            await _context.Quotations.AddAsync(quotation);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        _logger.LogInformation("Quotation {QuotationId} ({Number}) created in workspace {WorkspaceId} by {UserId}", quotation.Id, quotation.Number, workspaceId, callerUserId);
        return quotation;
    }

    public async Task<List<Quotation>> SearchAsync(Guid workspaceId, Guid callerUserId, Guid? clientId)
    {
        await _access.EnsureListAllowedAsync(callerUserId, workspaceId);

        var quotations = _context.Quotations.Where(q => q.WorkspaceId == workspaceId && q.IsActive);
        if (clientId.HasValue)
            quotations = quotations.Where(q => q.ClientId == clientId.Value);

        return await quotations.OrderByDescending(q => q.CreatedAt).ToListAsync();
    }

    public async Task<Quotation> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid quotationId)
    {
        await _access.EnsureAllowedAsync(callerUserId, "quotation", "view", workspaceId);
        return await FindQuotationAsync(workspaceId, quotationId);
    }

    public async Task<Quotation> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid quotationId, QuotationRequest request)
    {
        await _access.EnsureAllowedAsync(callerUserId, "quotation", "edit", workspaceId);
        await _clientService.FindClientAsync(workspaceId, request.ClientId);
        ValidateLineItems(request.LineItems);
        var quotation = await FindQuotationAsync(workspaceId, quotationId);

        // Line items changed after the quote was Sent - bump RevisionNumber so
        // "revision charges" have something to point at, without a new entity.
        if (quotation.Status is "Sent" or "Accepted" or "Rejected" or "Expired")
            quotation.RevisionNumber++;

        quotation.ClientId = request.ClientId;
        quotation.JobId = request.JobId;
        quotation.LineItems = ToEntities(request.LineItems);
        quotation.TaxRatePercent = request.TaxRatePercent;
        quotation.ValidUntil = request.ValidUntil;
        if (request.Status != null)
            quotation.Status = request.Status;
        quotation.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return quotation;
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid quotationId)
    {
        await _access.EnsureAllowedAsync(callerUserId, "quotation", "delete", workspaceId);
        var quotation = await FindQuotationAsync(workspaceId, quotationId);

        quotation.IsActive = false;
        quotation.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<Invoice> ConvertToInvoiceAsync(Guid workspaceId, Guid callerUserId, Guid quotationId, ConvertQuotationRequest request)
    {
        await _access.EnsureAllowedAsync(callerUserId, "quotation", "edit", workspaceId);
        await _access.EnsureAllowedAsync(callerUserId, "invoice", "create", workspaceId);
        var quotation = await FindQuotationAsync(workspaceId, quotationId);

        if (quotation.Status is not ("Draft" or "Sent"))
            throw new ValidationException($"Cannot convert a quotation with status '{quotation.Status}'.");

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ClientId = quotation.ClientId,
            JobId = quotation.JobId,
            QuotationId = quotation.Id,
            LineItems = quotation.LineItems.Select(li => new InvoiceLineItem { Id = Guid.NewGuid(), Description = li.Description, Quantity = li.Quantity, UnitPrice = li.UnitPrice }).ToList(),
            TaxRatePercent = quotation.TaxRatePercent,
            DiscountAmount = request.DiscountAmount,
            Status = "Draft",
            DueDate = request.DueDate,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            invoice.Number = await NextNumberAsync(workspaceId, "INV");
            await _context.Invoices.AddAsync(invoice);
            quotation.Status = "Accepted";
            quotation.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        _logger.LogInformation("Quotation {QuotationId} converted to Invoice {InvoiceId} ({Number})", quotation.Id, invoice.Id, invoice.Number);
        return invoice;
    }

    private async Task<string> NextNumberAsync(Guid workspaceId, string prefix)
    {
        var count = prefix switch
        {
            "Q" => await _context.Quotations.IgnoreQueryFilters().CountAsync(q => q.WorkspaceId == workspaceId),
            "INV" => await _context.Invoices.IgnoreQueryFilters().CountAsync(i => i.WorkspaceId == workspaceId),
            _ => throw new InvalidOperationException($"Unknown prefix '{prefix}'.")
        };
        return $"{prefix}-{count + 1:D4}";
    }

    private static void ValidateLineItems(List<LineItemDto> items)
    {
        if (items.Count == 0)
            throw new ValidationException("At least one line item is required.");
        if (items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0))
            throw new ValidationException("Line item quantity must be positive and unit price cannot be negative.");
    }

    private static List<QuotationLineItem> ToEntities(List<LineItemDto> items) =>
        items.Select(i => new QuotationLineItem { Id = Guid.NewGuid(), Description = i.Description.Trim(), Quantity = i.Quantity, UnitPrice = i.UnitPrice }).ToList();

    private async Task<Quotation> FindQuotationAsync(Guid workspaceId, Guid quotationId)
    {
        return await _context.Quotations.FirstOrDefaultAsync(q => q.Id == quotationId && q.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Quotation not found");
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter QuotationServiceTests`
Expected: PASS (all 4 tests).

- [ ] **Step 6: Write the controller**

`api/src/SurveyorLedger.API/Controllers/QuotationsController.cs`:
```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/quotations")]
    [Authorize]
    public class QuotationsController : ControllerBase
    {
        private readonly IQuotationService _quotationService;

        public QuotationsController(IQuotationService quotationService)
        {
            _quotationService = quotationService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<QuotationResponse>>>> Search(Guid workspaceId, [FromQuery] Guid? clientId)
        {
            var quotations = await _quotationService.SearchAsync(workspaceId, CallerId(), clientId);
            return Ok(ApiResponse<List<QuotationResponse>>.Ok(quotations.Select(ToResponse).ToList()));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<QuotationResponse>>> Create(Guid workspaceId, [FromBody] QuotationRequest request)
        {
            var quotation = await _quotationService.CreateAsync(workspaceId, CallerId(), request);
            return CreatedAtAction(nameof(GetById), new { workspaceId, id = quotation.Id }, ApiResponse<QuotationResponse>.Ok(ToResponse(quotation)));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<QuotationResponse>>> GetById(Guid workspaceId, Guid id)
        {
            var quotation = await _quotationService.GetByIdAsync(workspaceId, CallerId(), id);
            return Ok(ApiResponse<QuotationResponse>.Ok(ToResponse(quotation)));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<QuotationResponse>>> Update(Guid workspaceId, Guid id, [FromBody] QuotationRequest request)
        {
            var quotation = await _quotationService.UpdateAsync(workspaceId, CallerId(), id, request);
            return Ok(ApiResponse<QuotationResponse>.Ok(ToResponse(quotation)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid workspaceId, Guid id)
        {
            await _quotationService.DeleteAsync(workspaceId, CallerId(), id);
            return NoContent();
        }

        [HttpPost("{id}/convert-to-invoice")]
        public async Task<ActionResult<ApiResponse<InvoiceResponse>>> ConvertToInvoice(Guid workspaceId, Guid id, [FromBody] ConvertQuotationRequest request)
        {
            var invoice = await _quotationService.ConvertToInvoiceAsync(workspaceId, CallerId(), id, request);
            return Ok(ApiResponse<InvoiceResponse>.Ok(InvoicesController.ToResponse(invoice)));
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        internal static QuotationResponse ToResponse(Quotation q)
        {
            var subtotal = q.LineItems.Sum(li => li.Quantity * li.UnitPrice);
            var tax = subtotal * q.TaxRatePercent / 100m;
            return new QuotationResponse
            {
                QuotationId = q.Id,
                ClientId = q.ClientId,
                JobId = q.JobId,
                Number = q.Number,
                LineItems = q.LineItems.Select(li => new LineItemDto { Description = li.Description, Quantity = li.Quantity, UnitPrice = li.UnitPrice }).ToList(),
                TaxRatePercent = q.TaxRatePercent,
                Subtotal = subtotal,
                Total = subtotal + tax,
                Status = q.Status,
                ValidUntil = q.ValidUntil,
                RevisionNumber = q.RevisionNumber,
                CreatedAt = q.CreatedAt,
                UpdatedAt = q.UpdatedAt
            };
        }
    }
}
```

Note: `InvoiceResponse` and `InvoicesController.ToResponse` are defined in Task 5 —
this controller references them, so Task 5 must compile alongside this one (both
are in the same project; order tasks 4 then 5, or build only after both land).

- [ ] **Step 7: Register the service in Program.cs**

Add, next to the `IClientService` registration:
```csharp
builder.Services.AddScoped<IQuotationService, QuotationService>();
```

- [ ] **Step 8: Build and test**

Run: `cd api && dotnet build`
Expected: this will fail until Task 5's `InvoiceResponse`/`InvoicesController` exist
— that's expected at this checkpoint. Run
`dotnet test --filter QuotationServiceTests` instead (it doesn't touch the
controller) and confirm PASS before moving to Task 5.

- [ ] **Step 9: Commit**

```bash
git add api/src/SurveyorLedger.API/Models/Billing/QuotationDtos.cs api/src/SurveyorLedger.API/Services/QuotationService.cs api/src/SurveyorLedger.API/Controllers/QuotationsController.cs api/src/SurveyorLedger.API/Program.cs api/tests/SurveyorLedger.API.Tests/Services/QuotationServiceTests.cs
git commit -m "feat: add Quotation service, revisions, and convert-to-invoice"
```

---

### Task 5: InvoiceService + InvoicesController (payments, computed totals, overpayment guard)

**Files:**
- Create: `api/src/SurveyorLedger.API/Models/Billing/InvoiceDtos.cs`
- Create: `api/src/SurveyorLedger.API/Services/InvoiceService.cs`
- Create: `api/src/SurveyorLedger.API/Controllers/InvoicesController.cs`
- Modify: `api/src/SurveyorLedger.API/Program.cs`
- Create: `api/tests/SurveyorLedger.API.Tests/Services/InvoiceServiceTests.cs`

**Interfaces:**
- Consumes: `IClientService.FindClientAsync` (Task 3); `Invoice`/`InvoiceLineItem`/`Payment`
  entities (Task 1); `IFileStorageService.SaveAsync`/`OpenAsync`
  (`api/src/SurveyorLedger.API/Services/IFileStorageService.cs`) for payment proof
  upload, same pattern as `LandService.UploadPhotoAsync`.
- Produces: `IInvoiceService` with
  `CreateAsync`, `SearchAsync(Guid workspaceId, Guid callerUserId, Guid? clientId)`,
  `GetByIdAsync`, `UpdateAsync`, `DeleteAsync`,
  `RecordPaymentAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, PaymentRequest, IFormFile? proofFile) : Task<Payment>`,
  `GetPaymentsAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId) : Task<List<Payment>>`,
  and the computed-value helper `(decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue, int DaysOverdue) ComputeInvoiceTotals(Invoice invoice)`.
  `ComputeInvoiceTotals` is the single source of truth reused by
  `InvoicesController.ToResponse` and by Task 6's client-balance aggregation.

- [ ] **Step 1: Write the DTOs**

`api/src/SurveyorLedger.API/Models/Billing/InvoiceDtos.cs`:
```csharp
namespace SurveyorLedger.API.Models.Billing;

public class InvoiceRequest
{
    public Guid ClientId { get; set; }
    public Guid? JobId { get; set; }
    public List<LineItemDto> LineItems { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Status { get; set; }
}

public class PaymentRequest
{
    public decimal Amount { get; set; }
    public string Method { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? ReferenceNumber { get; set; }
}

public class PaymentResponse
{
    public Guid PaymentId { get; set; }
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? ReferenceNumber { get; set; }
    public bool HasProofFile { get; set; }
    public string ReceiptNumber { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class InvoiceResponse
{
    public Guid InvoiceId { get; set; }
    public Guid ClientId { get; set; }
    public Guid? JobId { get; set; }
    public Guid? QuotationId { get; set; }
    public string Number { get; set; }
    public List<LineItemDto> LineItems { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Balance { get; set; }
    public string Status { get; set; }
    public DateTime? DueDate { get; set; }
    public bool IsOverdue { get; set; }
    public int DaysOverdue { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

- [ ] **Step 2: Write the failing service tests**

`api/tests/SurveyorLedger.API.Tests/Services/InvoiceServiceTests.cs`:
```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class InvoiceServiceTests : WorkspaceIntegrationTestBase
{
    private IClientService _clientService = null!;
    private IInvoiceService _invoiceService = null!;
    private Guid _clientId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-invoice-test-{Guid.NewGuid():N}")
                })
                .Build());
    }

    private async Task<Guid> SeedInvoiceAsync(DateTime? dueDate = null)
    {
        _clientService = GetService<IClientService>();
        _invoiceService = GetService<IInvoiceService>();
        var client = await _clientService.CreateAsync(WorkspaceId, AdminId, new ClientRequest { Name = "Acme Ltd" });
        _clientId = client.Id;

        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = _clientId,
            LineItems = new List<LineItemDto> { new() { Description = "Survey", Quantity = 1, UnitPrice = 100000m } },
            TaxRatePercent = 0,
            DiscountAmount = 0,
            DueDate = dueDate,
            Status = "Sent"
        });
        return invoice.Id;
    }

    [Fact]
    public async Task RecordPaymentAsync_PartialPayment_SetsPartiallyPaid()
    {
        var invoiceId = await SeedInvoiceAsync();
        await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 40000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);

        var invoice = await _invoiceService.GetByIdAsync(WorkspaceId, AdminId, invoiceId);
        Assert.Equal("PartiallyPaid", invoice.Status);
    }

    [Fact]
    public async Task RecordPaymentAsync_FullPayment_SetsPaid()
    {
        var invoiceId = await SeedInvoiceAsync();
        await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 100000m, Method = "BankTransfer", ReceivedAt = DateTime.UtcNow }, null);

        var invoice = await _invoiceService.GetByIdAsync(WorkspaceId, AdminId, invoiceId);
        Assert.Equal("Paid", invoice.Status);
    }

    [Fact]
    public async Task RecordPaymentAsync_Overpayment_Throws()
    {
        var invoiceId = await SeedInvoiceAsync();
        await Assert.ThrowsAsync<ValidationException>(
            () => _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 150000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null));
    }

    [Fact]
    public async Task RecordPaymentAsync_AssignsSequentialReceiptNumbers()
    {
        var invoiceId = await SeedInvoiceAsync();
        var p1 = await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 30000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);
        var p2 = await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 20000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);

        Assert.Equal("RCP-0001", p1.ReceiptNumber);
        Assert.Equal("RCP-0002", p2.ReceiptNumber);
    }

    [Fact]
    public async Task DeleteAsync_WithPayments_Throws409()
    {
        var invoiceId = await SeedInvoiceAsync();
        await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 10000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);

        var ex = await Assert.ThrowsAsync<AppException>(() => _invoiceService.DeleteAsync(WorkspaceId, AdminId, invoiceId));
        Assert.Equal(409, ex.StatusCode);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter InvoiceServiceTests`
Expected: FAIL — `IInvoiceService`/`InvoiceService` do not exist yet.

- [ ] **Step 4: Add the 409 exception type**

`AppException` (Task's Global Constraints reference,
`api/src/SurveyorLedger.Core/Exceptions/AppException.cs`) has no 409 subclass yet.
Add one alongside `ForbiddenException`/`NotFoundException`:
```csharp
public class ConflictException : AppException
{
    public ConflictException(string message)
        : base(Constants.ErrorCodes.ValidationFailed, message, 409) { }
}
```

- [ ] **Step 5: Write the service**

`api/src/SurveyorLedger.API/Services/InvoiceService.cs`:
```csharp
using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IInvoiceService
{
    Task<Invoice> CreateAsync(Guid workspaceId, Guid callerUserId, InvoiceRequest request);
    Task<List<Invoice>> SearchAsync(Guid workspaceId, Guid callerUserId, Guid? clientId);
    Task<Invoice> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId);
    Task<Invoice> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, InvoiceRequest request);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId);
    Task<Payment> RecordPaymentAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, PaymentRequest request, IFormFile? proofFile);
    Task<List<Payment>> GetPaymentsAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId);
    (decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue, int DaysOverdue) ComputeInvoiceTotals(Invoice invoice);
}

public class InvoiceService : IInvoiceService
{
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase) { "Cash", "BankTransfer", "Cheque" };

    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly IClientService _clientService;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(ApplicationDbContext context, IScopedAccessService access, IClientService clientService, IFileStorageService fileStorage, ILogger<InvoiceService> logger)
    {
        _context = context;
        _access = access;
        _clientService = clientService;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<Invoice> CreateAsync(Guid workspaceId, Guid callerUserId, InvoiceRequest request)
    {
        await _access.EnsureAllowedAsync(callerUserId, "invoice", "create", workspaceId);
        await _clientService.FindClientAsync(workspaceId, request.ClientId);
        ValidateLineItems(request.LineItems);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ClientId = request.ClientId,
            JobId = request.JobId,
            LineItems = request.LineItems.Select(i => new InvoiceLineItem { Id = Guid.NewGuid(), Description = i.Description.Trim(), Quantity = i.Quantity, UnitPrice = i.UnitPrice }).ToList(),
            TaxRatePercent = request.TaxRatePercent,
            DiscountAmount = request.DiscountAmount,
            Status = request.Status ?? "Draft",
            DueDate = request.DueDate,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            invoice.Number = await NextInvoiceNumberAsync(workspaceId);
            await _context.Invoices.AddAsync(invoice);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        _logger.LogInformation("Invoice {InvoiceId} ({Number}) created in workspace {WorkspaceId} by {UserId}", invoice.Id, invoice.Number, workspaceId, callerUserId);
        return invoice;
    }

    public async Task<List<Invoice>> SearchAsync(Guid workspaceId, Guid callerUserId, Guid? clientId)
    {
        await _access.EnsureListAllowedAsync(callerUserId, workspaceId);

        var invoices = _context.Invoices.Include(i => i.Payments).Where(i => i.WorkspaceId == workspaceId && i.IsActive);
        if (clientId.HasValue)
            invoices = invoices.Where(i => i.ClientId == clientId.Value);

        return await invoices.OrderByDescending(i => i.CreatedAt).ToListAsync();
    }

    public async Task<Invoice> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId)
    {
        await _access.EnsureAllowedAsync(callerUserId, "invoice", "view", workspaceId);
        return await FindInvoiceAsync(workspaceId, invoiceId);
    }

    public async Task<Invoice> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, InvoiceRequest request)
    {
        await _access.EnsureAllowedAsync(callerUserId, "invoice", "edit", workspaceId);
        await _clientService.FindClientAsync(workspaceId, request.ClientId);
        ValidateLineItems(request.LineItems);
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);

        invoice.ClientId = request.ClientId;
        invoice.JobId = request.JobId;
        invoice.LineItems = request.LineItems.Select(i => new InvoiceLineItem { Id = Guid.NewGuid(), Description = i.Description.Trim(), Quantity = i.Quantity, UnitPrice = i.UnitPrice }).ToList();
        invoice.TaxRatePercent = request.TaxRatePercent;
        invoice.DiscountAmount = request.DiscountAmount;
        invoice.DueDate = request.DueDate;
        // Server-derived statuses (PartiallyPaid/Paid) are not client-settable here -
        // only Draft/Sent/Cancelled pass through, matching the spec's status rules.
        if (request.Status is "Draft" or "Sent" or "Cancelled")
            invoice.Status = request.Status;
        invoice.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return invoice;
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId)
    {
        await _access.EnsureAllowedAsync(callerUserId, "invoice", "delete", workspaceId);
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);

        if (invoice.Payments.Count > 0)
            throw new ConflictException("Cannot delete an invoice with recorded payments. Cancel it instead.");

        invoice.IsActive = false;
        invoice.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<Payment> RecordPaymentAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, PaymentRequest request, IFormFile? proofFile)
    {
        await _access.EnsureAllowedAsync(callerUserId, "invoice", "create", workspaceId);
        if (!AllowedMethods.Contains(request.Method))
            throw new ValidationException($"Method must be one of: {string.Join(", ", AllowedMethods)}.");
        if (request.Amount <= 0)
            throw new ValidationException("Amount must be positive.");

        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        var (total, amountPaid, balance, _, _) = ComputeInvoiceTotals(invoice);
        if (request.Amount > balance)
            throw new ValidationException($"Amount {request.Amount} exceeds the outstanding balance of {balance}.");

        string? proofPath = null;
        if (proofFile != null)
        {
            var storedFileName = $"{Guid.NewGuid():N}_{proofFile.FileName}";
            proofPath = $"{workspaceId}/invoices/{invoiceId}/payments/{storedFileName}";
            await using var stream = proofFile.OpenReadStream();
            await _fileStorage.SaveAsync(stream, proofPath, CancellationToken.None);
        }

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            InvoiceId = invoiceId,
            Amount = request.Amount,
            Method = request.Method,
            ReceivedAt = request.ReceivedAt,
            ReferenceNumber = request.ReferenceNumber?.Trim(),
            ProofFilePath = proofPath,
            RecordedBy = callerUserId,
            CreatedAt = DateTime.UtcNow
        };

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            payment.ReceiptNumber = await NextReceiptNumberAsync(workspaceId);
            await _context.Payments.AddAsync(payment);

            var newAmountPaid = amountPaid + request.Amount;
            invoice.Status = newAmountPaid >= total ? "Paid" : "PartiallyPaid";
            invoice.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        _logger.LogInformation("Payment {PaymentId} ({ReceiptNumber}) of {Amount} recorded for Invoice {InvoiceId}", payment.Id, payment.ReceiptNumber, payment.Amount, invoiceId);
        return payment;
    }

    public async Task<List<Payment>> GetPaymentsAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId)
    {
        await _access.EnsureAllowedAsync(callerUserId, "invoice", "view", workspaceId);
        await FindInvoiceAsync(workspaceId, invoiceId);

        return await _context.Payments
            .Where(p => p.InvoiceId == invoiceId && p.WorkspaceId == workspaceId)
            .OrderByDescending(p => p.ReceivedAt)
            .ToListAsync();
    }

    public (decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue, int DaysOverdue) ComputeInvoiceTotals(Invoice invoice)
    {
        var subtotal = invoice.LineItems.Sum(li => li.Quantity * li.UnitPrice);
        var tax = subtotal * invoice.TaxRatePercent / 100m;
        var total = subtotal - invoice.DiscountAmount + tax;
        var amountPaid = invoice.Payments.Sum(p => p.Amount);
        var balance = total - amountPaid;

        var isOverdue = invoice.Status is "Sent" or "PartiallyPaid" && invoice.DueDate.HasValue && invoice.DueDate.Value.Date < DateTime.UtcNow.Date;
        var daysOverdue = isOverdue ? (DateTime.UtcNow.Date - invoice.DueDate!.Value.Date).Days : 0;

        return (total, amountPaid, balance, isOverdue, daysOverdue);
    }

    private async Task<string> NextInvoiceNumberAsync(Guid workspaceId)
    {
        var count = await _context.Invoices.IgnoreQueryFilters().CountAsync(i => i.WorkspaceId == workspaceId);
        return $"INV-{count + 1:D4}";
    }

    private async Task<string> NextReceiptNumberAsync(Guid workspaceId)
    {
        var count = await _context.Payments.IgnoreQueryFilters().CountAsync(p => p.WorkspaceId == workspaceId);
        return $"RCP-{count + 1:D4}";
    }

    private static void ValidateLineItems(List<LineItemDto> items)
    {
        if (items.Count == 0)
            throw new ValidationException("At least one line item is required.");
        if (items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0))
            throw new ValidationException("Line item quantity must be positive and unit price cannot be negative.");
    }

    private async Task<Invoice> FindInvoiceAsync(Guid workspaceId, Guid invoiceId)
    {
        return await _context.Invoices.Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Invoice not found");
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter InvoiceServiceTests`
Expected: PASS (all 5 tests).

- [ ] **Step 7: Write the controller**

`api/src/SurveyorLedger.API/Controllers/InvoicesController.cs`:
```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/invoices")]
    [Authorize]
    public class InvoicesController : ControllerBase
    {
        private readonly IInvoiceService _invoiceService;

        public InvoicesController(IInvoiceService invoiceService)
        {
            _invoiceService = invoiceService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<InvoiceResponse>>>> Search(Guid workspaceId, [FromQuery] Guid? clientId)
        {
            var invoices = await _invoiceService.SearchAsync(workspaceId, CallerId(), clientId);
            return Ok(ApiResponse<List<InvoiceResponse>>.Ok(invoices.Select(i => ToResponse(i, _invoiceService)).ToList()));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<InvoiceResponse>>> Create(Guid workspaceId, [FromBody] InvoiceRequest request)
        {
            var invoice = await _invoiceService.CreateAsync(workspaceId, CallerId(), request);
            return CreatedAtAction(nameof(GetById), new { workspaceId, id = invoice.Id }, ApiResponse<InvoiceResponse>.Ok(ToResponse(invoice, _invoiceService)));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<InvoiceResponse>>> GetById(Guid workspaceId, Guid id)
        {
            var invoice = await _invoiceService.GetByIdAsync(workspaceId, CallerId(), id);
            return Ok(ApiResponse<InvoiceResponse>.Ok(ToResponse(invoice, _invoiceService)));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<InvoiceResponse>>> Update(Guid workspaceId, Guid id, [FromBody] InvoiceRequest request)
        {
            var invoice = await _invoiceService.UpdateAsync(workspaceId, CallerId(), id, request);
            return Ok(ApiResponse<InvoiceResponse>.Ok(ToResponse(invoice, _invoiceService)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid workspaceId, Guid id)
        {
            await _invoiceService.DeleteAsync(workspaceId, CallerId(), id);
            return NoContent();
        }

        [HttpPost("{id}/payments")]
        public async Task<ActionResult<ApiResponse<PaymentResponse>>> RecordPayment(Guid workspaceId, Guid id, [FromForm] PaymentRequest request, IFormFile? proofFile)
        {
            var payment = await _invoiceService.RecordPaymentAsync(workspaceId, CallerId(), id, request, proofFile);
            return Ok(ApiResponse<PaymentResponse>.Ok(ToResponse(payment)));
        }

        [HttpGet("{id}/payments")]
        public async Task<ActionResult<ApiResponse<List<PaymentResponse>>>> GetPayments(Guid workspaceId, Guid id)
        {
            var payments = await _invoiceService.GetPaymentsAsync(workspaceId, CallerId(), id);
            return Ok(ApiResponse<List<PaymentResponse>>.Ok(payments.Select(ToResponse).ToList()));
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        internal static InvoiceResponse ToResponse(Invoice i, IInvoiceService invoiceService)
        {
            var (total, amountPaid, balance, isOverdue, daysOverdue) = invoiceService.ComputeInvoiceTotals(i);
            var subtotal = i.LineItems.Sum(li => li.Quantity * li.UnitPrice);
            return new InvoiceResponse
            {
                InvoiceId = i.Id,
                ClientId = i.ClientId,
                JobId = i.JobId,
                QuotationId = i.QuotationId,
                Number = i.Number,
                LineItems = i.LineItems.Select(li => new LineItemDto { Description = li.Description, Quantity = li.Quantity, UnitPrice = li.UnitPrice }).ToList(),
                TaxRatePercent = i.TaxRatePercent,
                DiscountAmount = i.DiscountAmount,
                Subtotal = subtotal,
                Total = total,
                AmountPaid = amountPaid,
                Balance = balance,
                Status = i.Status,
                DueDate = i.DueDate,
                IsOverdue = isOverdue,
                DaysOverdue = daysOverdue,
                CreatedAt = i.CreatedAt,
                UpdatedAt = i.UpdatedAt
            };
        }

        // Overload used by QuotationsController.ConvertToInvoice, which does not have
        // an IInvoiceService instance handy - recomputes totals inline instead.
        internal static InvoiceResponse ToResponse(Invoice i)
        {
            var subtotal = i.LineItems.Sum(li => li.Quantity * li.UnitPrice);
            var tax = subtotal * i.TaxRatePercent / 100m;
            var total = subtotal - i.DiscountAmount + tax;
            return new InvoiceResponse
            {
                InvoiceId = i.Id,
                ClientId = i.ClientId,
                JobId = i.JobId,
                QuotationId = i.QuotationId,
                Number = i.Number,
                LineItems = i.LineItems.Select(li => new LineItemDto { Description = li.Description, Quantity = li.Quantity, UnitPrice = li.UnitPrice }).ToList(),
                TaxRatePercent = i.TaxRatePercent,
                DiscountAmount = i.DiscountAmount,
                Subtotal = subtotal,
                Total = total,
                AmountPaid = 0,
                Balance = total,
                Status = i.Status,
                DueDate = i.DueDate,
                IsOverdue = false,
                DaysOverdue = 0,
                CreatedAt = i.CreatedAt,
                UpdatedAt = i.UpdatedAt
            };
        }

        private static PaymentResponse ToResponse(Payment p) => new()
        {
            PaymentId = p.Id,
            InvoiceId = p.InvoiceId,
            Amount = p.Amount,
            Method = p.Method,
            ReceivedAt = p.ReceivedAt,
            ReferenceNumber = p.ReferenceNumber,
            HasProofFile = p.ProofFilePath != null,
            ReceiptNumber = p.ReceiptNumber,
            CreatedAt = p.CreatedAt
        };
    }
}
```

- [ ] **Step 8: Register the service and the ConflictException handler**

In `api/src/SurveyorLedger.API/Program.cs`, add:
```csharp
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
```
`ConflictException` extends `AppException`, so it flows through the existing global
exception handler (which reads `AppException.StatusCode`) with no further wiring —
confirm this by locating the handler (search for `catch (AppException` or an
`IExceptionHandler` implementation) and reading it; do not add a new handler if one
already exists.

- [ ] **Step 9: Build and run the full billing test suite**

Run:
```bash
cd api && dotnet build
cd api && dotnet test --filter "ClientServiceTests|QuotationServiceTests|InvoiceServiceTests"
```
Expected: build succeeds (Task 4's controller now resolves `InvoiceResponse`), all
13 tests pass.

- [ ] **Step 10: Commit**

```bash
git add api/src/SurveyorLedger.API/Models/Billing/InvoiceDtos.cs api/src/SurveyorLedger.API/Services/InvoiceService.cs api/src/SurveyorLedger.API/Controllers/InvoicesController.cs api/src/SurveyorLedger.API/Program.cs api/src/SurveyorLedger.Core/Exceptions/AppException.cs api/tests/SurveyorLedger.API.Tests/Services/InvoiceServiceTests.cs
git commit -m "feat: add Invoice service with payments, overpayment guard, and aging"
```

- [ ] **Step 11: Run the api-layer-review skill**

Invoke the `api-layer-review` skill against the full billing change set (Tasks 1–5)
to confirm Controller→Service→Data layering, tenant isolation, and RBAC correctness
before moving on.

---

### Task 6: Client balance and payment history aggregation

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/ClientService.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/ClientsController.cs`
- Modify: `api/src/SurveyorLedger.API/Program.cs` (no change expected — verify only)
- Modify: `api/tests/SurveyorLedger.API.Tests/Services/ClientServiceTests.cs`

**Interfaces:**
- Consumes: `IInvoiceService.ComputeInvoiceTotals(Invoice) : (decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue, int DaysOverdue)`
  (Task 5).
- Produces: `IClientService.GetBalanceAsync(Guid workspaceId, Guid callerUserId, Guid clientId) : Task<decimal>`,
  `IClientService.GetPaymentHistoryAsync(Guid workspaceId, Guid callerUserId, Guid clientId) : Task<List<Payment>>`.

- [ ] **Step 1: Write the failing tests**

Append to `api/tests/SurveyorLedger.API.Tests/Services/ClientServiceTests.cs`
(add `IInvoiceService`/`IFileStorageService`/`IConfiguration` to `ConfigureServices`,
matching the setup in `InvoiceServiceTests.cs`):

```csharp
    // Add to ConfigureServices:
    // services.AddScoped<IInvoiceService, InvoiceService>();
    // services.AddScoped<IFileStorageService, LocalFileStorageService>();
    // services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
    //     .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-client-test-{Guid.NewGuid():N}") })
    //     .Build());

    [Fact]
    public async Task GetBalanceAsync_SumsOutstandingAcrossInvoices()
    {
        _clientService = GetService<IClientService>();
        var invoiceService = GetService<IInvoiceService>();
        var client = await _clientService.CreateAsync(WorkspaceId, AdminId, new ClientRequest { Name = "Acme Ltd" });

        var invoice1 = await invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = client.Id,
            LineItems = new List<LineItemDto> { new() { Description = "Survey A", Quantity = 1, UnitPrice = 100000m } },
            Status = "Sent"
        });
        await invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoice1.Id, new PaymentRequest { Amount = 40000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);

        var invoice2 = await invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = client.Id,
            LineItems = new List<LineItemDto> { new() { Description = "Survey B", Quantity = 1, UnitPrice = 50000m } },
            Status = "Sent"
        });

        var balance = await _clientService.GetBalanceAsync(WorkspaceId, AdminId, client.Id);
        Assert.Equal(60000m + 50000m, balance);
    }

    [Fact]
    public async Task GetPaymentHistoryAsync_ReturnsPaymentsAcrossInvoices()
    {
        _clientService = GetService<IClientService>();
        var invoiceService = GetService<IInvoiceService>();
        var client = await _clientService.CreateAsync(WorkspaceId, AdminId, new ClientRequest { Name = "Acme Ltd" });

        var invoice = await invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = client.Id,
            LineItems = new List<LineItemDto> { new() { Description = "Survey", Quantity = 1, UnitPrice = 100000m } },
            Status = "Sent"
        });
        await invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoice.Id, new PaymentRequest { Amount = 40000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);
        await invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoice.Id, new PaymentRequest { Amount = 20000m, Method = "Cheque", ReceivedAt = DateTime.UtcNow }, null);

        var history = await _clientService.GetPaymentHistoryAsync(WorkspaceId, AdminId, client.Id);
        Assert.Equal(2, history.Count);
    }
```
Also add these `using` statements at the top of the file if not already present:
`using SurveyorLedger.API.Services;` (already there) and reference
`SurveyorLedger.API.Models.Billing.InvoiceRequest`/`PaymentRequest` (same
namespace as `ClientRequest`, already imported).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter ClientServiceTests`
Expected: FAIL to compile — `GetBalanceAsync`/`GetPaymentHistoryAsync` don't exist.

- [ ] **Step 3: Implement the two methods**

In `api/src/SurveyorLedger.API/Services/ClientService.cs`:

1. Add `IInvoiceService _invoices` as a constructor-injected field (add
   `IInvoiceService invoiceService` parameter, assign `_invoices = invoiceService;`).
2. Add to the `IClientService` interface:
```csharp
    Task<decimal> GetBalanceAsync(Guid workspaceId, Guid callerUserId, Guid clientId);
    Task<List<Payment>> GetPaymentHistoryAsync(Guid workspaceId, Guid callerUserId, Guid clientId);
```
3. Add to the `ClientService` class body (needs `using Microsoft.EntityFrameworkCore;`
   already present, and access to `_context.Invoices`/`_context.Payments`):
```csharp
    public async Task<decimal> GetBalanceAsync(Guid workspaceId, Guid callerUserId, Guid clientId)
    {
        await _access.EnsureAllowedAsync(callerUserId, "client", "view", workspaceId);
        await FindClientAsync(workspaceId, clientId);

        var invoices = await _context.Invoices.Include(i => i.Payments)
            .Where(i => i.WorkspaceId == workspaceId && i.ClientId == clientId && i.IsActive)
            .ToListAsync();

        return invoices.Sum(i => _invoices.ComputeInvoiceTotals(i).Balance);
    }

    public async Task<List<Payment>> GetPaymentHistoryAsync(Guid workspaceId, Guid callerUserId, Guid clientId)
    {
        await _access.EnsureAllowedAsync(callerUserId, "client", "view", workspaceId);
        await FindClientAsync(workspaceId, clientId);

        return await _context.Payments
            .Where(p => p.WorkspaceId == workspaceId && p.Invoice.ClientId == clientId)
            .OrderByDescending(p => p.ReceivedAt)
            .ToListAsync();
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter ClientServiceTests`
Expected: PASS (all 6 tests).

- [ ] **Step 5: Wire the endpoints into ClientsController**

In `api/src/SurveyorLedger.API/Controllers/ClientsController.cs`, add:
```csharp
        [HttpGet("{id}/balance")]
        public async Task<ActionResult<ApiResponse<ClientBalanceResponse>>> GetBalance(Guid workspaceId, Guid id)
        {
            var balance = await _clientService.GetBalanceAsync(workspaceId, CallerId(), id);
            return Ok(ApiResponse<ClientBalanceResponse>.Ok(new ClientBalanceResponse { ClientId = id, OutstandingBalance = balance }));
        }

        [HttpGet("{id}/payments")]
        public async Task<ActionResult<ApiResponse<List<PaymentResponse>>>> GetPayments(Guid workspaceId, Guid id)
        {
            var payments = await _clientService.GetPaymentHistoryAsync(workspaceId, CallerId(), id);
            return Ok(ApiResponse<List<PaymentResponse>>.Ok(payments.Select(ToPaymentResponse).ToList()));
        }

        private static PaymentResponse ToPaymentResponse(Payment p) => new()
        {
            PaymentId = p.Id,
            InvoiceId = p.InvoiceId,
            Amount = p.Amount,
            Method = p.Method,
            ReceivedAt = p.ReceivedAt,
            ReferenceNumber = p.ReferenceNumber,
            HasProofFile = p.ProofFilePath != null,
            ReceiptNumber = p.ReceiptNumber,
            CreatedAt = p.CreatedAt
        };
```
Add `using SurveyorLedger.API.Models.Billing;` (already present).

- [ ] **Step 6: Build and run the full billing test suite**

Run:
```bash
cd api && dotnet build
cd api && dotnet test --filter "ClientServiceTests|QuotationServiceTests|InvoiceServiceTests"
```
Expected: build succeeds, all 15 tests pass.

- [ ] **Step 7: Commit**

```bash
git add api/src/SurveyorLedger.API/Services/ClientService.cs api/src/SurveyorLedger.API/Controllers/ClientsController.cs api/tests/SurveyorLedger.API.Tests/Services/ClientServiceTests.cs
git commit -m "feat: add client outstanding balance and payment history endpoints"
```

- [ ] **Step 8: Final api-layer-review pass**

Invoke the `api-layer-review` skill once more across the full diff (Tasks 1–6) as a
final gate before considering the phase done.

---

## Post-plan out of scope reminder

Expenses, staff payroll, profitability calculations, financial dashboard, fee
templates, actually-sending reminders/notifications, online payment gateway, and
any UI work are explicitly deferred to future phases per the spec.
