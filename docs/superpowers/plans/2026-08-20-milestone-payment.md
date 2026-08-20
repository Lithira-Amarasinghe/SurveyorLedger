# Milestone Payment Linking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a Milestone carry a fee, become a real invoice/quotation line item, optionally gate its own status transitions on that invoice's payment state (fully user-defined rules, not fixed flags), and let one Quotation draw multiple Invoices against it (advance/milestone/final, each free to add extra fees) - replacing the old one-shot convert-to-invoice action and its two form modals with one shared routed billing page.

**Architecture:** Three additive DB changes (`Milestone.Amount`, a `MilestoneId` tag on invoice/quotation line items, an owned `MilestonePaymentRequirement` collection on `Milestone` mirroring how `InvoiceInstallments` already works) plus one behavior change (`InvoiceRequest.QuotationId` replaces the removed `ConvertToInvoiceAsync` special path). Frontend replaces `InvoiceFormModalComponent`/`QuotationFormModalComponent`/`ConvertQuotationModalComponent` with one `BillingDocumentFormPageComponent` parameterized by document type.

**Tech Stack:** .NET 9, EF Core 9 (SQL Server LocalDB), Angular 21, Angular CDK (already a dependency).

## Global Constraints

- Migrations generated via `dotnet ef migrations add`, never hand-edited (project rule, enforced by a PreToolUse hook that blocks `Edit`/`Write` on any `Migrations/*.cs` file - if a migration needs a manual data fix, do it via a direct SQL statement against LocalDB outside the migration file, the way the Manager-role-removal migration did, not by editing the generated file).
- Every tenant-scoped query goes through `WorkspaceId` filtering - for all entities here that's transitive via `Job.WorkspaceId`, resolved through the existing `FindJobAsync`/`FindInvoiceAsync`/`FindQuotationAsync` helpers, always called first.
- `Milestone.Amount` is one-directional: it seeds a line item once, and is never written back to from an edited line item.
- Zero `MilestonePaymentRequirement` rows on a milestone (the default) means that milestone transitions completely freely - a linked invoice existing, even unpaid, never blocks anything on its own.
- `InvoiceLineItem.MilestoneId`/`QuotationLineItem.MilestoneId` are plain scalar columns with no EF navigation or FK constraint (owned-type-to-independent-entity references add real complexity this doesn't need) - "at most one active link per milestone per document type" is enforced entirely in `ValidateLineItems`, not by the database.
- No proportional per-line-item payment allocation anywhere - a milestone's payment state is read straight off its linked invoice's own `Status`.

---

## Part A — Milestone fee and payment gating (backend)

### Task 1: `Milestone.Amount`

**Files:**
- Modify: `api/src/SurveyorLedger.Data/Entities/Milestone.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Milestone/MilestoneRequest.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Milestone/MilestoneResponse.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/MilestoneController.cs`
- Modify: `api/src/SurveyorLedger.API/Services/MilestoneService.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/MilestoneServiceTests.cs`

**Interfaces:**
- Produces: `Milestone.Amount` (`decimal?`), `MilestoneRequest.Amount` (`decimal?`), `MilestoneResponse.Amount` (`decimal?`). Consumed by Task 5 (`GetPaymentStatusAsync`) and the frontend (Task 12+).

- [ ] **Step 1: Write the failing test**

Add to `MilestoneServiceTests.cs`:
```csharp
[Fact]
public async Task Amount_IsPersisted_AndDefaultsToNull()
{
    await SeedJobsAsync();
    var withAmount = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "Deed Verified", Amount = 25000m });
    var withoutAmount = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobAId, new MilestoneRequest { Title = "Site Visit" });

    Assert.Equal(25000m, withAmount.Amount);
    Assert.Null(withoutAmount.Amount);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd api && dotnet test --filter "FullyQualifiedName~Amount_IsPersisted_AndDefaultsToNull"`
Expected: FAIL - `MilestoneRequest` has no `Amount` member.

- [ ] **Step 3: Add the field to the entity, DTOs, and service**

In `Milestone.cs`, add after `public string Status { get; set; } = "Pending";`:
```csharp
    public decimal? Amount { get; set; }
```

In `MilestoneRequest.cs`, add after the `Description` property:
```csharp
    [Range(0, double.MaxValue, ErrorMessage = "Amount cannot be negative.")]
    public decimal? Amount { get; set; }
```

In `MilestoneResponse.cs`, add after `DueDate`:
```csharp
    public decimal? Amount { get; set; }
```

In `MilestoneController.cs`, add `Amount = m.Amount,` to `ToResponse` right after `DueDate = m.DueDate,`.

In `MilestoneService.cs`, set it in both `CreateAsync` and `UpdateAsync` right after `DueDate = request.DueDate` (Create) / `milestone.DueDate = request.DueDate;` (Update):
```csharp
            Amount = request.Amount,
```
```csharp
        milestone.Amount = request.Amount;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd api && dotnet test --filter "FullyQualifiedName~Amount_IsPersisted_AndDefaultsToNull"`
Expected: PASS

- [ ] **Step 5: Generate and apply the migration**

```bash
cd api && dotnet ef migrations add AddMilestoneAmount --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```
Expected: `Done.` both times. Verify: `sqlcmd -S "(localdb)\mssqllocaldb" -d SurveyorLedger -Q "SELECT name FROM sys.columns WHERE object_id=OBJECT_ID('Milestones') AND name='Amount'"` returns one row.

- [ ] **Step 6: Commit**

```bash
git add api/src/SurveyorLedger.Data/Entities/Milestone.cs api/src/SurveyorLedger.API/Models/Milestone/MilestoneRequest.cs api/src/SurveyorLedger.API/Models/Milestone/MilestoneResponse.cs api/src/SurveyorLedger.API/Controllers/MilestoneController.cs api/src/SurveyorLedger.API/Services/MilestoneService.cs api/tests/SurveyorLedger.API.Tests/Services/MilestoneServiceTests.cs api/src/SurveyorLedger.Data/Migrations/
git commit -m "feat: add optional fee amount to Milestone"
```

---

### Task 2: `MilestoneId` tag on invoice and quotation line items

**Files:**
- Modify: `api/src/SurveyorLedger.Data/Entities/InvoiceLineItem.cs`
- Modify: `api/src/SurveyorLedger.Data/Entities/QuotationLineItem.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/InvoiceConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/QuotationConfiguration.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Billing/QuotationDtos.cs` (`LineItemDto` lives here)
- Modify: `api/src/SurveyorLedger.API/Services/InvoiceService.cs`
- Modify: `api/src/SurveyorLedger.API/Services/QuotationService.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/InvoicesController.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/QuotationsController.cs`
- Test: new `api/tests/SurveyorLedger.API.Tests/Services/MilestoneBillingLinkTests.cs`

**Interfaces:**
- Consumes: `Milestone` (Task 1), existing `InvoiceService`/`QuotationService` shape as read from the current code.
- Produces: `LineItemDto.MilestoneId` (`Guid?`), enforced-unique-while-active across both `InvoiceLineItem` and `QuotationLineItem` independently. Consumed by Task 3 (payment-status resolution) and Task 6/7 (quotation-draw UI).

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.Milestone;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class MilestoneBillingLinkTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IMilestoneService _milestoneService = null!;
    private IInvoiceService _invoiceService = null!;
    private IQuotationService _quotationService = null!;
    private Guid _jobId;
    private Guid _milestoneId;
    private Guid _clientPersonId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IMilestoneService, MilestoneService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IQuotationService, QuotationService>();
    }

    private async Task SeedAsync()
    {
        _jobService = GetService<IJobService>();
        _milestoneService = GetService<IMilestoneService>();
        _invoiceService = GetService<IInvoiceService>();
        _quotationService = GetService<IQuotationService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        _jobId = job.Id;
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobId, new MilestoneRequest { Title = "Deed Verified", Amount = 25000m });
        _milestoneId = milestone.Id;
        _clientPersonId = await GrantClientBillingRoleAsync(_jobId);
    }

    private InvoiceRequest InvoiceRequestFor(Guid? milestoneId) => new()
    {
        ClientId = _clientPersonId,
        JobId = _jobId,
        LineItems = new() { new LineItemDto { Description = "Deed Verified", Quantity = 1, UnitPrice = 25000m, MilestoneId = milestoneId } },
        TaxRatePercent = 0,
        DiscountAmount = 0,
        Installments = new()
    };

    [Fact]
    public async Task InvoiceLineItem_CarriesMilestoneId()
    {
        await SeedAsync();
        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceRequestFor(_milestoneId));
        Assert.Equal(_milestoneId, invoice.LineItems.Single().MilestoneId);
    }

    [Fact]
    public async Task SecondInvoice_CannotClaimSameMilestone_WhileFirstIsActive()
    {
        await SeedAsync();
        await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceRequestFor(_milestoneId));

        await Assert.ThrowsAsync<ValidationException>(
            () => _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceRequestFor(_milestoneId)));
    }

    [Fact]
    public async Task Quotation_And_Invoice_CanEachHoldTheirOwnActiveLink_Simultaneously()
    {
        await SeedAsync();
        var quotationRequest = new QuotationRequest
        {
            ClientId = _clientPersonId,
            JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Deed Verified", Quantity = 1, UnitPrice = 25000m, MilestoneId = _milestoneId } },
            TaxRatePercent = 0
        };
        await _quotationService.CreateAsync(WorkspaceId, AdminId, quotationRequest);

        // Same milestone tagged on an invoice too - independent document types, both allowed.
        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceRequestFor(_milestoneId));
        Assert.Equal(_milestoneId, invoice.LineItems.Single().MilestoneId);
    }
}
```

- [ ] **Step 2: Add a `GrantClientBillingRoleAsync` test helper**

`WorkspaceIntegrationTestBase` doesn't yet grant a Client/Finance role scoped to one job (billing tests need `EnsureClientHoldsBillingRoleOnJobAsync` to pass). Add to `WorkspaceIntegrationTestBase.cs`, after `SeedWorkspaceAndMembersAsync`:
```csharp
    /// <summary>Grants the seeded Client account job-scoped access on jobId, and returns
    /// their PersonId - the id InvoiceRequest.ClientId/QuotationRequest.ClientId expect.</summary>
    protected async Task<Guid> GrantClientBillingRoleAsync(Guid jobId)
    {
        await GrantService.GrantAsync(ClientId, RoleConfiguration.ClientRoleId, Constants.ScopeTypes.Job, jobId, AdminId);
        return ClientPersonId;
    }
```
(If `RoleConfiguration`/`Constants` aren't already imported in that file, add `using SurveyorLedger.Data.Configurations;` / `using SurveyorLedger.Core;` - check the file's existing usings first, they're likely already there since `SeedWorkspaceAndMembersAsync` uses both.)

- [ ] **Step 3: Run tests to verify they fail**

Run: `cd api && dotnet test --filter "FullyQualifiedName~MilestoneBillingLinkTests"`
Expected: FAIL to compile - `LineItemDto` has no `MilestoneId`.

- [ ] **Step 4: Add `MilestoneId` to the entities and DTO**

In `InvoiceLineItem.cs`, add:
```csharp
    public Guid? MilestoneId { get; set; }
```
Same addition in `QuotationLineItem.cs`.

In `QuotationDtos.cs`, add to `LineItemDto`:
```csharp
    public Guid? MilestoneId { get; set; }
```

- [ ] **Step 5: Map the column in both owned-entity configurations**

In `InvoiceConfiguration.cs`, inside the `OwnsMany(x => x.LineItems, li => { ... })` block, add after the `UnitPrice` line:
```csharp
            li.Property(x => x.MilestoneId);
```
Same addition in `QuotationConfiguration.cs`'s `OwnsMany` block.

- [ ] **Step 6: Wire the field through both services' create/update/response paths**

In `InvoiceService.cs`:
- `CreateAsync`'s line-item projection: add `MilestoneId = i.MilestoneId` to the `new InvoiceLineItem { ... }` initializer.
- `UpdateAsync`'s line-item projection: same addition to its `new InvoiceLineItem { ... }` initializer.
- Add the uniqueness check inside `ValidateLineItems` - change its signature to take the context it needs and call it as an instance method (it's currently `private static`, promote to `private async Task`, called with `await ValidateLineItemsAsync(...)` from both `CreateAsync` and `UpdateAsync`, passing the invoice id being saved so an update doesn't reject its own existing tag):
```csharp
    private async Task ValidateLineItemsAsync(List<LineItemDto> items, Guid? excludingInvoiceId)
    {
        if (items.Count == 0)
            throw new ValidationException("At least one line item is required.");
        if (items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0))
            throw new ValidationException("Line item quantity must be positive and unit price cannot be negative.");

        var milestoneIds = items.Where(i => i.MilestoneId.HasValue).Select(i => i.MilestoneId!.Value).ToList();
        if (milestoneIds.Count == 0)
            return;

        var conflicting = await _context.Invoices
            .Where(inv => inv.IsActive && (excludingInvoiceId == null || inv.Id != excludingInvoiceId))
            .Where(inv => inv.LineItems.Any(li => li.MilestoneId != null && milestoneIds.Contains(li.MilestoneId.Value)))
            .Select(inv => inv.Number)
            .FirstOrDefaultAsync();
        if (conflicting != null)
            throw new ValidationException($"One of these milestones is already billed on invoice {conflicting}.");
    }
```
Update the two call sites (`ValidateLineItems(request.LineItems);` in `CreateAsync` → `await ValidateLineItemsAsync(request.LineItems, null);`; in `UpdateAsync` → `await ValidateLineItemsAsync(request.LineItems, invoiceId);`).
- `EnsureOnlyDueDateChanged`'s line-item comparison tuple: change
  `(li.Description, li.Quantity, li.UnitPrice)` → `(li.Description, li.Quantity, li.UnitPrice, li.MilestoneId)`
  and the matching request-side projection
  `(li.Description.Trim(), li.Quantity, li.UnitPrice)` → `(li.Description.Trim(), li.Quantity, li.UnitPrice, li.MilestoneId)`.

In `InvoicesController.cs`'s `ToResponse`, add `MilestoneId = li.MilestoneId` to the `new LineItemDto { ... }` projection.

In `QuotationService.cs`: apply the identical pattern - `ToEntities` gains `MilestoneId = i.MilestoneId`, `ValidateLineItems` becomes `ValidateLineItemsAsync` with the same milestone-conflict query against `_context.Quotations` instead of `_context.Invoices`, called from `CreateAsync`/`UpdateAsync` the same way.

In `QuotationsController.cs`'s `ToResponse`, add `MilestoneId = li.MilestoneId` to its `LineItemDto` projection.

- [ ] **Step 7: Run tests to verify they pass**

Run: `cd api && dotnet test --filter "FullyQualifiedName~MilestoneBillingLinkTests"`
Expected: PASS (3 tests)

- [ ] **Step 8: Generate and apply the migration**

```bash
cd api && dotnet ef migrations add AddMilestoneIdToLineItems --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```
Expected: adds `MilestoneId` to both `InvoiceLineItems` and `QuotationLineItems` tables. `Done.` on apply.

- [ ] **Step 9: Run the full test suite**

Run: `cd api && dotnet test`
Expected: everything passes, including every pre-existing invoice/quotation test - `ValidateLineItems` → `ValidateLineItemsAsync` is a rename, not a behavior change, for line items with no `MilestoneId`.

- [ ] **Step 10: Commit**

```bash
git add api/src/SurveyorLedger.Data/Entities/InvoiceLineItem.cs api/src/SurveyorLedger.Data/Entities/QuotationLineItem.cs api/src/SurveyorLedger.Data/Configurations/InvoiceConfiguration.cs api/src/SurveyorLedger.Data/Configurations/QuotationConfiguration.cs api/src/SurveyorLedger.API/Models/Billing/QuotationDtos.cs api/src/SurveyorLedger.API/Services/InvoiceService.cs api/src/SurveyorLedger.API/Services/QuotationService.cs api/src/SurveyorLedger.API/Controllers/InvoicesController.cs api/src/SurveyorLedger.API/Controllers/QuotationsController.cs api/tests/SurveyorLedger.API.Tests/Services/MilestoneBillingLinkTests.cs api/tests/SurveyorLedger.API.Tests/Services/WorkspaceIntegrationTestBase.cs api/src/SurveyorLedger.Data/Migrations/
git commit -m "feat: tag invoice/quotation line items with their originating milestone"
```

---

### Task 3: `MilestonePaymentRequirement` and status-transition gating

**Files:**
- Modify: `api/src/SurveyorLedger.Data/Entities/Milestone.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/MilestoneConfiguration.cs`
- Create: `api/src/SurveyorLedger.Data/Entities/MilestonePaymentRequirement.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Milestone/MilestoneRequest.cs` (no change needed - requirements are set via their own endpoint, not the main request)
- Create: `api/src/SurveyorLedger.API/Models/Milestone/MilestonePaymentRequirementDtos.cs`
- Modify: `api/src/SurveyorLedger.API/Services/MilestoneService.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/MilestoneController.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/MilestonePaymentGatingTests.cs`

**Interfaces:**
- Consumes: `Milestone.Amount` (Task 1), `InvoiceLineItem.MilestoneId` (Task 2).
- Produces: `IMilestoneService.GetPaymentRequirementsAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId) -> Task<List<MilestonePaymentRequirement>>`, `SetPaymentRequirementsAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, List<(string TargetStatus, string RequiredState)> rules) -> Task<List<MilestonePaymentRequirement>>`, `GetPaymentStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId) -> Task<MilestonePaymentStatus>` where `MilestonePaymentStatus` is a new record `(decimal? Amount, Guid? LinkedInvoiceId, string? LinkedInvoiceNumber, string? InvoiceStatus, string? NextGate)`. Consumed by the frontend (Task 12+) and by `UpdateStatusAsync`'s own gate check internally.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.Milestone;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class MilestonePaymentGatingTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IMilestoneService _milestoneService = null!;
    private IInvoiceService _invoiceService = null!;
    private Guid _jobId;
    private Guid _clientPersonId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IMilestoneService, MilestoneService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IQuotationService, QuotationService>();
    }

    private async Task<Data.Entities.Milestone> SeedMilestoneAsync(decimal? amount)
    {
        _jobService = GetService<IJobService>();
        _milestoneService = GetService<IMilestoneService>();
        _invoiceService = GetService<IInvoiceService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        _jobId = job.Id;
        _clientPersonId = await GrantClientBillingRoleAsync(_jobId);
        return await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobId, new MilestoneRequest { Title = "Deed Verified", Amount = amount });
    }

    [Fact]
    public async Task NoRequirements_TransitionsFreely_EvenWithUnpaidLinkedInvoice()
    {
        var milestone = await SeedMilestoneAsync(25000m);
        await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Deed Verified", Quantity = 1, UnitPrice = 25000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
        });

        var updated = await _milestoneService.UpdateStatusAsync(WorkspaceId, AdminId, _jobId, milestone.Id, "Completed");
        Assert.Equal("Completed", updated.Status);
    }

    [Fact]
    public async Task FeelessMilestone_NeverGated()
    {
        var milestone = await SeedMilestoneAsync(null);
        await _milestoneService.SetPaymentRequirementsAsync(WorkspaceId, AdminId, _jobId, milestone.Id,
            new() { ("Completed", "FullyPaid") });

        // No invoice ever linked - the rule can never be satisfied by definition, but this
        // documents the failure mode explicitly rather than leaving it implicit.
        await Assert.ThrowsAsync<ValidationException>(
            () => _milestoneService.UpdateStatusAsync(WorkspaceId, AdminId, _jobId, milestone.Id, "Completed"));
    }

    [Fact]
    public async Task FullyPaidRequirement_BlocksUntilInvoicePaid_ThenSucceeds()
    {
        var milestone = await SeedMilestoneAsync(25000m);
        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Deed Verified", Quantity = 1, UnitPrice = 25000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
        });
        await _milestoneService.SetPaymentRequirementsAsync(WorkspaceId, AdminId, _jobId, milestone.Id,
            new() { ("Completed", "FullyPaid") });

        await Assert.ThrowsAsync<ValidationException>(
            () => _milestoneService.UpdateStatusAsync(WorkspaceId, AdminId, _jobId, milestone.Id, "Completed"));

        await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoice.Id,
            new PaymentRequest { Amount = 25000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);

        var updated = await _milestoneService.UpdateStatusAsync(WorkspaceId, AdminId, _jobId, milestone.Id, "Completed");
        Assert.Equal("Completed", updated.Status);
    }

    [Fact]
    public async Task GetPaymentStatus_ReflectsLinkedInvoice()
    {
        var milestone = await SeedMilestoneAsync(25000m);
        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Deed Verified", Quantity = 1, UnitPrice = 25000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
        });

        var status = await _milestoneService.GetPaymentStatusAsync(WorkspaceId, AdminId, _jobId, milestone.Id);
        Assert.Equal(invoice.Id, status.LinkedInvoiceId);
        Assert.Equal("Draft", status.InvoiceStatus);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd api && dotnet test --filter "FullyQualifiedName~MilestonePaymentGatingTests"`
Expected: FAIL to compile - `SetPaymentRequirementsAsync`/`GetPaymentStatusAsync` don't exist yet.

- [ ] **Step 3: Add the owned collection to `Milestone`**

Create `MilestonePaymentRequirement.cs`:
```csharp
namespace SurveyorLedger.Data.Entities;

/// <summary>
/// One user-defined gate: Milestone cannot enter TargetStatus until the milestone's
/// linked invoice (via a tagged InvoiceLineItem) reaches RequiredState. No fixed pair
/// of gates - a milestone can have zero, one, or several of these, on any of its
/// statuses. RequiredState is "Invoiced" | "PartiallyPaid" | "FullyPaid".
/// </summary>
public class MilestonePaymentRequirement
{
    public Guid Id { get; set; }
    public string TargetStatus { get; set; }
    public string RequiredState { get; set; }
}
```

In `Milestone.cs`, add:
```csharp
    public List<MilestonePaymentRequirement> PaymentRequirements { get; set; } = new();
```

In `MilestoneConfiguration.cs`, add inside `Configure`, after the existing `HasOne(x => x.CompletedByUser)...` block:
```csharp
        builder.OwnsMany(x => x.PaymentRequirements, r =>
        {
            r.ToTable("MilestonePaymentRequirements");
            r.WithOwner().HasForeignKey("MilestoneId");
            r.HasKey(x => x.Id);
            r.Property(x => x.TargetStatus).HasMaxLength(20).IsRequired();
            r.Property(x => x.RequiredState).HasMaxLength(20).IsRequired();
        });
```

- [ ] **Step 4: Add the DTOs**

Create `MilestonePaymentRequirementDtos.cs`:
```csharp
namespace SurveyorLedger.API.Models.Milestone;

public class PaymentRequirementDto
{
    public required string TargetStatus { get; set; }
    public required string RequiredState { get; set; }
}

public class SetPaymentRequirementsRequest
{
    public List<PaymentRequirementDto> Requirements { get; set; } = new();
}

public class MilestonePaymentStatusResponse
{
    public decimal? Amount { get; set; }
    public Guid? LinkedInvoiceId { get; set; }
    public string? LinkedInvoiceNumber { get; set; }
    public string? InvoiceStatus { get; set; }
    public string? NextGate { get; set; }
}
```

- [ ] **Step 5: Implement the service methods**

In `MilestoneService.cs`, add to the interface:
```csharp
    Task<List<MilestonePaymentRequirement>> GetPaymentRequirementsAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId);
    Task<List<MilestonePaymentRequirement>> SetPaymentRequirementsAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, List<(string TargetStatus, string RequiredState)> rules);
    Task<MilestonePaymentStatus> GetPaymentStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId);
```
Add the record above the interface:
```csharp
public record MilestonePaymentStatus(decimal? Amount, Guid? LinkedInvoiceId, string? LinkedInvoiceNumber, string? InvoiceStatus, string? NextGate);
```
Add the static requirement whitelist next to `ValidStatuses`:
```csharp
    private static readonly HashSet<string> ValidPaymentStates = new() { "Invoiced", "PartiallyPaid", "FullyPaid" };
```
Add the implementations after `ReorderAsync`:
```csharp
    public async Task<List<MilestonePaymentRequirement>> GetPaymentRequirementsAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");
        var milestone = await _context.Milestones.Include(m => m.PaymentRequirements).FirstOrDefaultAsync(m => m.Id == milestoneId && m.JobId == jobId)
            ?? throw new NotFoundException("Milestone not found");
        return milestone.PaymentRequirements;
    }

    public async Task<List<MilestonePaymentRequirement>> SetPaymentRequirementsAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId, List<(string TargetStatus, string RequiredState)> rules)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        foreach (var (targetStatus, requiredState) in rules)
        {
            if (!ValidStatuses.Contains(targetStatus))
                throw new ValidationException($"TargetStatus must be one of: {string.Join(", ", ValidStatuses)}.");
            if (!ValidPaymentStates.Contains(requiredState))
                throw new ValidationException($"RequiredState must be one of: {string.Join(", ", ValidPaymentStates)}.");
        }

        var milestone = await _context.Milestones.Include(m => m.PaymentRequirements).FirstOrDefaultAsync(m => m.Id == milestoneId && m.JobId == jobId)
            ?? throw new NotFoundException("Milestone not found");

        // Wholesale replace - same delete-all/insert-all pattern InvoiceService uses for
        // LineItems/Installments, appropriate here for the same reason: a small owned list
        // with no external references to preserve.
        foreach (var old in milestone.PaymentRequirements.ToList())
            _context.Remove(old);
        milestone.PaymentRequirements.Clear();
        foreach (var (targetStatus, requiredState) in rules)
        {
            var rule = new MilestonePaymentRequirement { Id = Guid.NewGuid(), TargetStatus = targetStatus, RequiredState = requiredState };
            milestone.PaymentRequirements.Add(rule);
            _context.Entry(rule).State = EntityState.Added;
        }
        milestone.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return milestone.PaymentRequirements;
    }

    public async Task<MilestonePaymentStatus> GetPaymentStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");
        var milestone = await FindMilestoneAsync(jobId, milestoneId);

        var linkedInvoice = await FindLinkedInvoiceAsync(milestoneId);
        var nextGate = await ResolveNextGateAsync(milestone, linkedInvoice);

        return new MilestonePaymentStatus(
            milestone.Amount,
            linkedInvoice?.Id,
            linkedInvoice?.Number,
            linkedInvoice?.Status,
            nextGate);
    }

    /// <summary>The invoice, if any, carrying a line item tagged with this milestone - at
    /// most one, per the uniqueness rule enforced in InvoiceService.ValidateLineItemsAsync.</summary>
    private async Task<Invoice?> FindLinkedInvoiceAsync(Guid milestoneId) =>
        await _context.Invoices.Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.IsActive && i.LineItems.Any(li => li.MilestoneId == milestoneId));

    /// <summary>Human-readable description of the nearest unmet requirement for this
    /// milestone's *current* status - i.e. what would block its next transition attempt via
    /// UpdateStatusAsync, without knowing in advance which status the caller will try next.</summary>
    private async Task<string?> ResolveNextGateAsync(Milestone milestone, Invoice? linkedInvoice)
    {
        await _context.Entry(milestone).Collection(m => m.PaymentRequirements).LoadAsync();
        foreach (var rule in milestone.PaymentRequirements)
        {
            if (!IsRequirementSatisfied(rule.RequiredState, linkedInvoice))
                return $"Requires the linked invoice to be {DescribeState(rule.RequiredState)} before it can be marked {rule.TargetStatus}.";
        }
        return null;
    }

    private static string DescribeState(string state) => state switch
    {
        "Invoiced" => "invoiced",
        "PartiallyPaid" => "at least partially paid",
        "FullyPaid" => "fully paid",
        _ => state
    };

    private static bool IsRequirementSatisfied(string requiredState, Invoice? invoice)
    {
        if (invoice == null)
            return false;
        return requiredState switch
        {
            "Invoiced" => invoice.Status is "Sent" or "PartiallyPaid" or "Paid",
            "PartiallyPaid" => invoice.Status is "PartiallyPaid" or "Paid",
            "FullyPaid" => invoice.Status == "Paid",
            _ => false
        };
    }
```

Wire the gate into `UpdateStatusAsync` - insert right after the existing `EnsureJobAccessAsync` call and before the status is applied:
```csharp
        var milestone = await FindMilestoneAsync(jobId, milestoneId);

        await _context.Entry(milestone).Collection(m => m.PaymentRequirements).LoadAsync();
        var applicableRules = milestone.PaymentRequirements.Where(r => r.TargetStatus == status).ToList();
        if (applicableRules.Count > 0)
        {
            var linkedInvoice = await FindLinkedInvoiceAsync(milestoneId);
            var unmet = applicableRules.FirstOrDefault(r => !IsRequirementSatisfied(r.RequiredState, linkedInvoice));
            if (unmet != null)
                throw new ValidationException($"Requires the linked invoice to be {DescribeState(unmet.RequiredState)} before it can be marked {status}.");
        }

        milestone.Status = status;
```
(This replaces the existing `var milestone = await FindMilestoneAsync(jobId, milestoneId);` / `milestone.Status = status;` pair already in `UpdateStatusAsync` - insert the gate check between them, don't duplicate the `FindMilestoneAsync` call.)

Add `using SurveyorLedger.Data.Entities;` is already present in this file (it uses `Job`, `Milestone` already) - no new using needed beyond what's there, `Invoice` needs adding since it's a new type referenced: add `using SurveyorLedger.Data.Entities;` already covers it (same namespace).

- [ ] **Step 6: Add the controller endpoints**

In `MilestoneController.cs`, add after the `Reorder` action:
```csharp
        [HttpGet("{id}/payment-requirements")]
        public async Task<ActionResult<ApiResponse<List<PaymentRequirementDto>>>> GetPaymentRequirements(Guid workspaceId, Guid jobId, Guid id)
        {
            var requirements = await _milestoneService.GetPaymentRequirementsAsync(workspaceId, CallerId(), jobId, id);
            return Ok(ApiResponse<List<PaymentRequirementDto>>.Ok(requirements.Select(r => new PaymentRequirementDto { TargetStatus = r.TargetStatus, RequiredState = r.RequiredState }).ToList()));
        }

        [HttpPut("{id}/payment-requirements")]
        public async Task<ActionResult<ApiResponse<List<PaymentRequirementDto>>>> SetPaymentRequirements(Guid workspaceId, Guid jobId, Guid id, [FromBody] SetPaymentRequirementsRequest request)
        {
            var rules = request.Requirements.Select(r => (r.TargetStatus, r.RequiredState)).ToList();
            var requirements = await _milestoneService.SetPaymentRequirementsAsync(workspaceId, CallerId(), jobId, id, rules);
            return Ok(ApiResponse<List<PaymentRequirementDto>>.Ok(requirements.Select(r => new PaymentRequirementDto { TargetStatus = r.TargetStatus, RequiredState = r.RequiredState }).ToList()));
        }

        [HttpGet("{id}/payment-status")]
        public async Task<ActionResult<ApiResponse<MilestonePaymentStatusResponse>>> GetPaymentStatus(Guid workspaceId, Guid jobId, Guid id)
        {
            var status = await _milestoneService.GetPaymentStatusAsync(workspaceId, CallerId(), jobId, id);
            return Ok(ApiResponse<MilestonePaymentStatusResponse>.Ok(new MilestonePaymentStatusResponse
            {
                Amount = status.Amount,
                LinkedInvoiceId = status.LinkedInvoiceId,
                LinkedInvoiceNumber = status.LinkedInvoiceNumber,
                InvoiceStatus = status.InvoiceStatus,
                NextGate = status.NextGate
            }));
        }
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `cd api && dotnet test --filter "FullyQualifiedName~MilestonePaymentGatingTests"`
Expected: PASS (4 tests). If `FeelessMilestone_NeverGated` fails, check `IsRequirementSatisfied` returns `false` when `invoice` is `null` (it does above) - the test name is slightly misleading (kept from the original ask's wording) but asserts the real behavior: a rule that can never be satisfied because nothing is linked correctly blocks, which is the requirement's own doing, not an implicit "has fee = gated" default.

- [ ] **Step 8: Generate and apply the migration**

```bash
cd api && dotnet ef migrations add AddMilestonePaymentRequirements --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```

- [ ] **Step 9: Run the full test suite**

Run: `cd api && dotnet test`
Expected: all pass.

- [ ] **Step 10: Commit**

```bash
git add api/src/SurveyorLedger.Data/Entities/Milestone.cs api/src/SurveyorLedger.Data/Entities/MilestonePaymentRequirement.cs api/src/SurveyorLedger.Data/Configurations/MilestoneConfiguration.cs api/src/SurveyorLedger.API/Models/Milestone/MilestonePaymentRequirementDtos.cs api/src/SurveyorLedger.API/Services/MilestoneService.cs api/src/SurveyorLedger.API/Controllers/MilestoneController.cs api/tests/SurveyorLedger.API.Tests/Services/MilestonePaymentGatingTests.cs api/src/SurveyorLedger.Data/Migrations/
git commit -m "feat: freeform payment-requirement gating for milestone status transitions"
```

---

## Part B — Quotation to many Invoices (backend)

### Task 4: `InvoiceRequest.QuotationId`, drop the convert action, add billing progress

**Files:**
- Modify: `api/src/SurveyorLedger.API/Models/Billing/InvoiceDtos.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Billing/QuotationDtos.cs`
- Modify: `api/src/SurveyorLedger.API/Services/InvoiceService.cs`
- Modify: `api/src/SurveyorLedger.API/Services/QuotationService.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/InvoicesController.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/QuotationsController.cs`
- Test: new `api/tests/SurveyorLedger.API.Tests/Services/QuotationManyInvoicesTests.cs`

**Interfaces:**
- Consumes: existing `Invoice`/`Quotation` shape.
- Produces: `InvoiceRequest.QuotationId` (`Guid?`), `IQuotationService.ComputeBillingProgress(Quotation quotation) -> (decimal InvoicedAmount, decimal RemainingAmount)`, `QuotationResponse.InvoicedAmount`/`RemainingAmount`. Consumed by the frontend (Task 13).

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class QuotationManyInvoicesTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IQuotationService _quotationService = null!;
    private IInvoiceService _invoiceService = null!;
    private Guid _jobId;
    private Guid _clientPersonId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IQuotationService, QuotationService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
    }

    private async Task<SurveyorLedger.Data.Entities.Quotation> SeedQuotationAsync()
    {
        _jobService = GetService<IJobService>();
        _quotationService = GetService<IQuotationService>();
        _invoiceService = GetService<IInvoiceService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        _jobId = job.Id;
        _clientPersonId = await GrantClientBillingRoleAsync(_jobId);

        return await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new()
            {
                new LineItemDto { Description = "Advance", Quantity = 1, UnitPrice = 30000m },
                new LineItemDto { Description = "Final", Quantity = 1, UnitPrice = 120000m }
            },
            TaxRatePercent = 0
        });
    }

    private InvoiceRequest DrawFrom(Quotation quotation, LineItemDto item) => new()
    {
        ClientId = _clientPersonId, JobId = _jobId, QuotationId = quotation.QuotationId,
        LineItems = new() { item }, TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
    };

    [Fact]
    public async Task TwoInvoices_CanDrawFromTheSameQuotation()
    {
        var quotation = await SeedQuotationAsync();
        var first = await _invoiceService.CreateAsync(WorkspaceId, AdminId, DrawFrom(quotation, quotation.LineItems[0]));
        var second = await _invoiceService.CreateAsync(WorkspaceId, AdminId, DrawFrom(quotation, quotation.LineItems[1]));

        Assert.Equal(quotation.QuotationId, first.QuotationId);
        Assert.Equal(quotation.QuotationId, second.QuotationId);
    }

    [Fact]
    public async Task BillingProgress_SumsActiveInvoicesAgainstTheQuotation()
    {
        var quotation = await SeedQuotationAsync();
        await _invoiceService.CreateAsync(WorkspaceId, AdminId, DrawFrom(quotation, quotation.LineItems[0]));

        var refreshed = await _quotationService.GetByIdAsync(WorkspaceId, AdminId, quotation.QuotationId);
        var (invoiced, remaining) = _quotationService.ComputeBillingProgress(refreshed);

        Assert.Equal(30000m, invoiced);
        Assert.Equal(120000m, remaining);
    }

    [Fact]
    public async Task InvoiceRequest_RejectsQuotationFromADifferentJob()
    {
        var quotation = await SeedQuotationAsync();
        var otherJob = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job B" });
        var otherClientPersonId = await GrantClientBillingRoleAsync(otherJob.Id);

        var request = DrawFrom(quotation, quotation.LineItems[0]);
        request.JobId = otherJob.Id;
        request.ClientId = otherClientPersonId;

        await Assert.ThrowsAsync<ValidationException>(() => _invoiceService.CreateAsync(WorkspaceId, AdminId, request));
    }

    [Fact]
    public async Task QuotationStatus_IsNotAutoAcceptedByDrawingAnInvoice()
    {
        var quotation = await SeedQuotationAsync();
        Assert.Equal("Draft", quotation.Status);
        await _invoiceService.CreateAsync(WorkspaceId, AdminId, DrawFrom(quotation, quotation.LineItems[0]));

        var refreshed = await _quotationService.GetByIdAsync(WorkspaceId, AdminId, quotation.QuotationId);
        Assert.Equal("Draft", refreshed.Status);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd api && dotnet test --filter "FullyQualifiedName~QuotationManyInvoicesTests"`
Expected: FAIL to compile - `InvoiceRequest` has no `QuotationId`, `IQuotationService` has no `ComputeBillingProgress`.

- [ ] **Step 3: Add `QuotationId` to `InvoiceRequest` and validate it**

In `InvoiceDtos.cs`, add to `InvoiceRequest`:
```csharp
    public Guid? QuotationId { get; set; }
```

In `InvoiceService.cs`'s `CreateAsync`, add right after `await EnsureClientHoldsBillingRoleOnJobAsync(request.ClientId, request.JobId);`:
```csharp
        if (request.QuotationId.HasValue)
            await EnsureQuotationBelongsToJobAsync(request.QuotationId.Value, request.JobId);
```
Set it on the entity - add `QuotationId = request.QuotationId,` to the `new Invoice { ... }` initializer.

Add the helper near `EnsureClientHoldsBillingRoleOnJobAsync`:
```csharp
    private async Task EnsureQuotationBelongsToJobAsync(Guid quotationId, Guid jobId)
    {
        var belongs = await _context.Quotations.AnyAsync(q => q.Id == quotationId && q.JobId == jobId && q.IsActive);
        if (!belongs)
            throw new ValidationException("QuotationId must reference an active quotation on this same job.");
    }
```
(`UpdateAsync` deliberately does not accept changing `QuotationId` after creation - it's not in the "locked once paid" comparison since it's set once at creation and the update path doesn't touch it; leave `UpdateAsync`'s entity untouched for this field.)

- [ ] **Step 4: Remove the convert-to-invoice action**

In `QuotationService.cs`: delete `ConvertToInvoiceAsync` entirely (the whole method) and its line in the `IQuotationService` interface. Delete `ConvertQuotationRequest` from `QuotationDtos.cs`.

In `QuotationsController.cs`: delete the `ConvertToInvoice` action and the now-unused `IInvoiceService _invoiceService`/`InvoicesController.ToResponse` reference in its constructor - the controller no longer needs `IInvoiceService` at all, so remove that constructor parameter and field too.

- [ ] **Step 5: Add `ComputeBillingProgress`**

`Invoice.Total` isn't a stored column - `InvoiceService.ComputeInvoiceTotals` derives it from `LineItems`/`TaxRatePercent`/`DiscountAmount` at read time. `ComputeBillingProgress` needs the same derivation, so `QuotationService` needs access to `IInvoiceService.ComputeInvoiceTotals`. Inject it into `QuotationService`'s constructor: add an `IInvoiceService invoiceService` parameter and a `private readonly IInvoiceService _invoiceService;` field alongside the existing ones, assigned in the constructor body the same way `_context`/`_access`/`_pdfService`/`_emailService`/`_logger` already are. (No circular dependency: `InvoiceService` does not depend on `IQuotationService`.)

Add to the `IQuotationService` interface:
```csharp
    (decimal InvoicedAmount, decimal RemainingAmount) ComputeBillingProgress(Quotation quotation);
```
Implementation, placed near `NextNumberAsync`:
```csharp
    /// <summary>Sums each active Invoice's own computed Total (via InvoiceService -
    /// Invoice.Total isn't a stored column) where that invoice carries this QuotationId.
    /// Computed, not stored, same pattern as InvoiceService.ComputeInvoiceTotals itself.
    /// Requires quotation.LineItems already loaded (FindQuotationAsync/GetByIdAsync/the
    /// updated SearchAsync in Step 6 all do this).</summary>
    public (decimal InvoicedAmount, decimal RemainingAmount) ComputeBillingProgress(Quotation quotation)
    {
        var invoices = _context.Invoices.Include(i => i.LineItems).Include(i => i.Payments)
            .Where(i => i.IsActive && i.QuotationId == quotation.Id)
            .ToList();
        var invoicedAmount = invoices.Sum(i => _invoiceService.ComputeInvoiceTotals(i).Total);

        var quotationSubtotal = quotation.LineItems.Sum(li => li.Quantity * li.UnitPrice);
        var quotationTax = quotationSubtotal * quotation.TaxRatePercent / 100m;
        var quotationTotal = quotationSubtotal + quotationTax;

        return (invoicedAmount, quotationTotal - invoicedAmount);
    }
```

- [ ] **Step 6: Expose it on `QuotationResponse`**

In `QuotationDtos.cs`, add to `QuotationResponse`:
```csharp
    public decimal InvoicedAmount { get; set; }
    public decimal RemainingAmount { get; set; }
```
In `QuotationsController.cs`'s `ToResponse`, change its signature to accept the service (`internal static QuotationResponse ToResponse(Quotation q, IQuotationService quotationService)`) and set the two new fields:
```csharp
        var (invoicedAmount, remainingAmount) = quotationService.ComputeBillingProgress(q);
```
adding `InvoicedAmount = invoicedAmount, RemainingAmount = remainingAmount,` to the returned object. Update every call site in the controller (`Search`, `Create`, `GetById`, `Update`) to pass `_quotationService` as the second argument.
**Note:** `ComputeBillingProgress` queries `q.LineItems` for the subtotal - `FindQuotationAsync`/`GetByIdAsync`/`SearchAsync` already `.Include(q => q.LineItems)` or return entities where it's needed elsewhere in the file, but double-check `SearchAsync`'s query includes `LineItems` (it currently doesn't - `SearchAsync` only does `_context.Quotations.Where(...)` with no `.Include`). Add `.Include(q => q.LineItems)` to `SearchAsync`'s base query so the list endpoint's progress numbers aren't computed against an empty collection.

- [ ] **Step 7: Run tests to verify they pass**

Run: `cd api && dotnet test --filter "FullyQualifiedName~QuotationManyInvoicesTests"`
Expected: PASS (4 tests)

- [ ] **Step 8: Run the full test suite, fix any break from removing `ConvertToInvoiceAsync`**

Run: `cd api && dotnet test`
Expected: any pre-existing test named around "Convert" will fail to compile - find and delete it (`grep -rn "ConvertToInvoice\|ConvertQuotationRequest" api/tests`), since the feature it tested no longer exists. Confirm no other production code references `ConvertToInvoiceAsync`/`ConvertQuotationRequest` (`grep -rn "ConvertToInvoice\|ConvertQuotationRequest" api/src`) before moving on.

- [ ] **Step 9: Commit**

```bash
git add api/src/SurveyorLedger.API/Models/Billing/ api/src/SurveyorLedger.API/Services/InvoiceService.cs api/src/SurveyorLedger.API/Services/QuotationService.cs api/src/SurveyorLedger.API/Controllers/InvoicesController.cs api/src/SurveyorLedger.API/Controllers/QuotationsController.cs api/tests/SurveyorLedger.API.Tests/Services/QuotationManyInvoicesTests.cs
git commit -m "feat: quotation-to-many-invoices via InvoiceRequest.QuotationId, remove one-shot convert"
git rm api/tests/SurveyorLedger.API.Tests/Services/<the deleted convert test file, if found in Step 8>
git commit -m "test: remove obsolete convert-to-invoice test"
```

---

## Part C — Shared billing document UI

### Task 5: `billing.service.ts` and `milestone.service.ts` updates

**Files:**
- Modify: `ui/src/app/core/billing.service.ts`
- Modify: `ui/src/app/core/milestone.service.ts`

**Interfaces:**
- Consumes: API changes from Tasks 1-4.
- Produces: `LineItem.milestoneId?: string`, `InvoiceRequest.quotationId?: string`, `Quotation.invoicedAmount`/`remainingAmount`, `Milestone.amount?: number`, `MilestoneService.getPaymentRequirements/setPaymentRequirements/getPaymentStatus`. Consumed by every remaining frontend task.

- [ ] **Step 1: Update `billing.service.ts`**

Add `milestoneId?: string;` to the `LineItem` interface. Add `quotationId?: string;` to `InvoiceRequest`. Add `invoicedAmount: number;` and `remainingAmount: number;` to `Quotation`. Delete `ConvertQuotationRequest` and `QuotationService.convertToInvoice(...)`.

- [ ] **Step 2: Update `milestone.service.ts`**

Add `amount: number | null;` to the `Milestone` interface. Add to `MilestoneService`:
```typescript
export interface PaymentRequirement {
  targetStatus: string;
  requiredState: 'Invoiced' | 'PartiallyPaid' | 'FullyPaid';
}

export interface MilestonePaymentStatus {
  amount: number | null;
  linkedInvoiceId: string | null;
  linkedInvoiceNumber: string | null;
  invoiceStatus: string | null;
  nextGate: string | null;
}
```
and, inside the `MilestoneService` class:
```typescript
  getPaymentRequirements(workspaceId: string, jobId: string, milestoneId: string): Observable<PaymentRequirement[]> {
    return this.http
      .get<ApiResponse<PaymentRequirement[]>>(`${this.base(workspaceId, jobId)}/${milestoneId}/payment-requirements`)
      .pipe(map(res => res.data));
  }

  setPaymentRequirements(workspaceId: string, jobId: string, milestoneId: string, requirements: PaymentRequirement[]): Observable<PaymentRequirement[]> {
    return this.http
      .put<ApiResponse<PaymentRequirement[]>>(`${this.base(workspaceId, jobId)}/${milestoneId}/payment-requirements`, { requirements })
      .pipe(map(res => res.data));
  }

  getPaymentStatus(workspaceId: string, jobId: string, milestoneId: string): Observable<MilestonePaymentStatus> {
    return this.http
      .get<ApiResponse<MilestonePaymentStatus>>(`${this.base(workspaceId, jobId)}/${milestoneId}/payment-status`)
      .pipe(map(res => res.data));
  }
```
Also add `amount?: number | null;` to whatever request-shaped type `create`/`update` already send (check the file's existing `create(...)` signature - it inline-types `{ title, description?, dueDate? }`; add `amount?: number | null` to that inline type in both `create` and `update`).

- [ ] **Step 3: Build**

Run: `cd ui && npx ng build 2>&1 | tail -30`
Expected: fails here (call sites not updated yet) - that's expected, subsequent tasks fix them. Just confirm the errors are only in files this task didn't touch (the old modals, job-detail, list pages) and not in `billing.service.ts`/`milestone.service.ts` themselves.

- [ ] **Step 4: Commit**

```bash
git add ui/src/app/core/billing.service.ts ui/src/app/core/milestone.service.ts
git commit -m "feat: wire milestone-billing fields and payment endpoints into billing/milestone services"
```

---

### Task 6: Milestone picker on `LineItemEditorComponent`

**Files:**
- Modify: `ui/src/app/shared/line-item-editor/line-item-editor.component.ts`

**Interfaces:**
- Consumes: `Milestone` (Task 5's updated interface), existing `LineItem`.
- Produces: `LineItemEditorComponent` gains `@Input() milestones: Milestone[] = []`. Consumed by Task 8 (the shared billing page).

- [ ] **Step 1: Add the input and the per-row picker**

Add the import and input:
```typescript
import { Milestone } from '../../core/milestone.service';
```
```typescript
  @Input() milestones: Milestone[] = [];
```
In the template, add a `<select>` after the unit-price input and before the remove button:
```html
            @if (milestones.length > 0) {
              <select
                class="input-field w-40"
                [ngModel]="item.milestoneId ?? ''"
                (ngModelChange)="updateItem(i, 'milestoneId', $event || undefined)"
                [name]="'milestone-' + i"
              >
                <option value="">No milestone (other fee)</option>
                @for (m of milestones; track m.milestoneId) {
                  <option [value]="m.milestoneId">{{ m.title }}</option>
                }
              </select>
            }
```
`updateItem` currently coerces every field with `Number(value)` except `description` - extend its field check:
```typescript
  updateItem(index: number, field: keyof LineItem, value: string | number | undefined): void {
    const updated = this.items.map((item, i) => {
      if (i !== index) return item;
      if (field === 'description' || field === 'milestoneId') return { ...item, [field]: value };
      return { ...item, [field]: Number(value) };
    });
    this.itemsChange.emit(updated);
  }
```

- [ ] **Step 2: Build**

Run: `cd ui && npx ng build 2>&1 | tail -30`
Expected: this component compiles clean (still errors elsewhere from Task 5's not-yet-updated call sites - unrelated to this task).

- [ ] **Step 3: Commit**

```bash
git add ui/src/app/shared/line-item-editor/line-item-editor.component.ts
git commit -m "feat: add per-row milestone picker to the shared line-item editor"
```

---

### Task 7: `BillingDocumentFormPageComponent` - the shared routed page

**Files:**
- Create: `ui/src/app/pages/billing/document-form/billing-document-form-page.component.ts`
- Modify: `ui/src/app/app.routes.ts`

**Interfaces:**
- Consumes: `LineItemEditorComponent` (Task 6), `BillingRecipientPickerComponent`, `InstallmentEditorComponent`, `InvoiceService`/`QuotationService` (Task 5), `JobService`, `MilestoneService`.
- Produces: the routed page itself. Consumed by Task 8 (job detail wiring) and Task 9 (list-page wiring).

- [ ] **Step 1: Write the component**

```typescript
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import {
  Installment, Invoice, InvoiceRequest, InvoiceService, LineItem,
  Quotation, QuotationRequest, QuotationService, QuotationStatus
} from '../../../core/billing.service';
import { Job, JobService } from '../../../core/job.service';
import { Milestone, MilestoneService } from '../../../core/milestone.service';
import { BillingRecipientPickerComponent } from '../../../shared/billing-recipient-picker/billing-recipient-picker.component';
import { LineItemEditorComponent } from '../../../shared/line-item-editor/line-item-editor.component';
import { InstallmentEditorComponent } from '../../../shared/installment-editor/installment-editor.component';

type DocumentType = 'invoice' | 'quotation';

@Component({
  selector: 'app-billing-document-form-page',
  standalone: true,
  imports: [CommonModule, FormsModule, BillingRecipientPickerComponent, LineItemEditorComponent, InstallmentEditorComponent],
  template: `
    <div class="p-lg max-w-2xl mx-auto space-y-lg">
      <div class="flex items-center justify-between">
        <h1 class="text-lg font-semibold text-neutral-900">
          {{ editingId ? 'Edit ' + documentType : 'New ' + documentType }}
        </h1>
        <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" (click)="goBack()">← Back</button>
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else {
        <div class="card">
          <form class="space-y-md" (ngSubmit)="submit()">
            @if (!jobId) {
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Job</label>
                <select class="input-field" name="jobId" [(ngModel)]="jobId" (ngModelChange)="onJobChange()">
                  <option [ngValue]="null">Select a job…</option>
                  @for (job of jobs(); track job.jobId) {
                    <option [ngValue]="job.jobId">{{ job.jobNumber }} · {{ job.title }}</option>
                  }
                </select>
              </div>
            }

            @if (fromQuotation(); as quotation) {
              <div class="rounded bg-neutral-50 p-md">
                <p class="text-xs font-medium text-neutral-700 mb-sm">Draw from {{ quotation.number }}</p>
                <div class="space-y-xs">
                  @for (item of quotation.lineItems; track $index; let i = $index) {
                    <label class="flex items-center gap-sm text-sm text-neutral-700">
                      <input type="checkbox" [checked]="isDrawn(i)" (change)="toggleDraw(i, item)" />
                      {{ item.description }} - {{ item.quantity * item.unitPrice | number: '1.2-2' }}
                    </label>
                  }
                </div>
              </div>
            }

            @if (isLocked()) {
              <div class="rounded bg-amber-50 border border-amber-200 px-md py-sm text-xs text-amber-800">
                This invoice already has recorded payments - the amount is locked. Only the due date can be changed.
              </div>
            }

            <fieldset [disabled]="isLocked()" class="space-y-md" [class.opacity-60]="isLocked()">
              <app-billing-recipient-picker [workspaceId]="workspaceId" [jobId]="jobId" [value]="clientId" (valueChange)="clientId = $event" />

              <app-line-item-editor [items]="lineItems" [milestones]="milestones()" (itemsChange)="lineItems = $event" />

              <div class="grid grid-cols-2 gap-sm">
                <div>
                  <label class="block text-xs font-medium text-neutral-700 mb-xs">Tax rate (%)</label>
                  <input class="input-field" type="number" min="0" step="0.01" name="taxRate" [(ngModel)]="taxRatePercent" />
                </div>
                @if (documentType === 'invoice') {
                  <div>
                    <label class="block text-xs font-medium text-neutral-700 mb-xs">Discount</label>
                    <input class="input-field" type="number" min="0" step="0.01" name="discount" [(ngModel)]="discountAmount" />
                  </div>
                } @else {
                  <div>
                    <label class="block text-xs font-medium text-neutral-700 mb-xs">Valid until</label>
                    <input class="input-field" type="date" name="validUntil" [(ngModel)]="validUntil" />
                  </div>
                }
              </div>

              @if (documentType === 'invoice') {
                <app-installment-editor [items]="installments" [invoiceTotal]="documentTotal()" (itemsChange)="installments = $event" />
              }

              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Status</label>
                <select class="input-field" name="status" [(ngModel)]="status">
                  @if (documentType === 'invoice') {
                    <option value="Draft">Draft</option>
                    <option value="Sent">Sent</option>
                    <option value="Cancelled">Cancelled</option>
                  } @else {
                    <option value="Draft">Draft</option>
                    <option value="Sent">Sent</option>
                    <option value="Accepted">Accepted</option>
                    <option value="Rejected">Rejected</option>
                    <option value="Expired">Expired</option>
                  }
                </select>
              </div>
            </fieldset>

            @if (documentType === 'invoice') {
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Due date</label>
                <input class="input-field" type="date" name="dueDate" [(ngModel)]="dueDate" />
              </div>
            }

            @if (error()) {
              <p class="text-sm text-primary-500">{{ error() }}</p>
            }

            <div class="flex justify-end gap-sm pt-sm">
              <button type="button" class="btn-secondary" (click)="goBack()">Cancel</button>
              <button type="submit" class="btn-primary" [disabled]="saving() || !jobId || !clientId || lineItems.length === 0">
                {{ saving() ? 'Saving…' : editingId ? 'Save' : 'Create' }}
              </button>
            </div>
          </form>
        </div>
      }
    </div>
  `
})
export class BillingDocumentFormPageComponent implements OnInit {
  documentType!: DocumentType;
  workspaceId = '';
  jobId: string | null = null;
  editingId: string | null = null;

  jobs = signal<Job[]>([]);
  milestones = signal<Milestone[]>([]);
  fromQuotation = signal<Quotation | null>(null);
  drawnIndexes = new Set<number>();

  clientId: string | null = null;
  lineItems: LineItem[] = [{ description: '', quantity: 1, unitPrice: 0 }];
  taxRatePercent = 0;
  discountAmount = 0;
  validUntil = '';
  dueDate = '';
  status = 'Draft';
  installments: Installment[] = [];

  loading = signal(false);
  saving = signal(false);
  error = signal('');

  private editingInvoice: Invoice | null = null;
  private editingQuotation: Quotation | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private invoiceService: InvoiceService,
    private quotationService: QuotationService,
    private jobService: JobService,
    private milestoneService: MilestoneService
  ) {}

  ngOnInit(): void {
    this.documentType = this.route.snapshot.data['documentType'];
    this.workspaceId = this.route.snapshot.paramMap.get('id') ?? '';
    this.editingId = this.route.snapshot.paramMap.get('invoiceId') ?? this.route.snapshot.paramMap.get('quotationId');
    this.jobId = this.route.snapshot.queryParamMap.get('jobId');

    if (!this.editingId) {
      this.jobs.set([]);
      if (!this.jobId) {
        this.jobService.list(this.workspaceId).subscribe({ next: jobs => this.jobs.set(jobs) });
      } else {
        this.loadMilestones();
      }
    }

    const fromQuotationId = this.route.snapshot.queryParamMap.get('fromQuotation');
    const milestoneId = this.route.snapshot.queryParamMap.get('milestoneId');

    if (this.editingId) {
      this.loading.set(true);
      const load$ = this.documentType === 'invoice'
        ? this.invoiceService.getById(this.workspaceId, this.editingId)
        : this.quotationService.getById(this.workspaceId, this.editingId);
      load$.subscribe({
        next: (doc: any) => {
          this.jobId = doc.jobId;
          this.loadMilestones();
          this.clientId = doc.clientId;
          this.lineItems = doc.lineItems.length > 0 ? [...doc.lineItems] : [{ description: '', quantity: 1, unitPrice: 0 }];
          this.taxRatePercent = doc.taxRatePercent;
          this.status = doc.status;
          if (this.documentType === 'invoice') {
            this.editingInvoice = doc;
            this.discountAmount = doc.discountAmount;
            this.dueDate = doc.dueDate ? doc.dueDate.substring(0, 10) : '';
            this.installments = doc.installments.map((i: Installment) => ({ amount: i.amount, dueDate: i.dueDate.substring(0, 10) }));
          } else {
            this.editingQuotation = doc;
            this.validUntil = doc.validUntil ? doc.validUntil.substring(0, 10) : '';
          }
          this.loading.set(false);
        },
        error: err => {
          this.error.set(err.error?.message ?? 'Could not load document.');
          this.loading.set(false);
        }
      });
    } else if (fromQuotationId && this.documentType === 'invoice') {
      this.loading.set(true);
      this.quotationService.getById(this.workspaceId, fromQuotationId).subscribe({
        next: quotation => {
          this.fromQuotation.set(quotation);
          this.clientId = quotation.clientId;
          this.jobId = quotation.jobId;
          this.loadMilestones();
          this.lineItems = [];
          this.loading.set(false);
        },
        error: err => {
          this.error.set(err.error?.message ?? 'Could not load quotation.');
          this.loading.set(false);
        }
      });
    } else if (milestoneId && this.jobId) {
      this.loading.set(true);
      this.milestoneService.getById(this.workspaceId, this.jobId, milestoneId).subscribe({
        next: milestone => {
          this.lineItems = [{ description: milestone.title, quantity: 1, unitPrice: milestone.amount ?? 0, milestoneId: milestone.milestoneId }];
          this.loading.set(false);
        },
        error: () => this.loading.set(false)
      });
    }
  }

  private loadMilestones(): void {
    if (!this.jobId) return;
    this.milestoneService.list(this.workspaceId, this.jobId).subscribe({ next: milestones => this.milestones.set(milestones) });
  }

  onJobChange(): void {
    this.clientId = null;
    this.loadMilestones();
  }

  isDrawn(index: number): boolean {
    return this.drawnIndexes.has(index);
  }

  toggleDraw(index: number, item: LineItem): void {
    if (this.drawnIndexes.has(index)) {
      this.drawnIndexes.delete(index);
      this.lineItems = this.lineItems.filter(li => li !== item);
    } else {
      this.drawnIndexes.add(index);
      this.lineItems = [...this.lineItems, { ...item }];
    }
  }

  isLocked(): boolean {
    return this.documentType === 'invoice' && !!this.editingInvoice && this.editingInvoice.amountPaid > 0;
  }

  documentTotal(): number {
    const subtotal = this.lineItems.reduce((sum, li) => sum + li.quantity * li.unitPrice, 0);
    const discount = this.documentType === 'invoice' ? this.discountAmount : 0;
    return subtotal - discount + (subtotal * this.taxRatePercent) / 100;
  }

  goBack(): void {
    if (this.jobId) {
      this.router.navigate(['/app/workspace', this.workspaceId, 'jobs', this.jobId]);
    } else {
      this.router.navigate(['/app/workspace', this.workspaceId, 'billing', this.documentType === 'invoice' ? 'invoices' : 'quotations']);
    }
  }

  submit(): void {
    if (!this.jobId || !this.clientId || this.lineItems.length === 0) return;
    this.error.set('');
    this.saving.set(true);

    if (this.documentType === 'invoice') {
      const request: InvoiceRequest = {
        clientId: this.clientId,
        jobId: this.jobId,
        quotationId: this.fromQuotation()?.quotationId,
        lineItems: this.lineItems,
        taxRatePercent: this.taxRatePercent,
        discountAmount: this.discountAmount,
        dueDate: this.dueDate || undefined,
        status: this.status as 'Draft' | 'Sent' | 'Cancelled',
        installments: this.installments
      };
      const save$ = this.editingId
        ? this.invoiceService.update(this.workspaceId, this.editingId, request)
        : this.invoiceService.create(this.workspaceId, request);
      save$.subscribe({
        next: () => { this.saving.set(false); this.goBack(); },
        error: err => { this.saving.set(false); this.error.set(err.error?.message ?? 'Could not save invoice.'); }
      });
    } else {
      const request: QuotationRequest = {
        clientId: this.clientId,
        jobId: this.jobId,
        lineItems: this.lineItems,
        taxRatePercent: this.taxRatePercent,
        validUntil: this.validUntil || undefined,
        status: this.status as QuotationStatus
      };
      const save$ = this.editingId
        ? this.quotationService.update(this.workspaceId, this.editingId, request)
        : this.quotationService.create(this.workspaceId, request);
      save$.subscribe({
        next: () => { this.saving.set(false); this.goBack(); },
        error: err => { this.saving.set(false); this.error.set(err.error?.message ?? 'Could not save quotation.'); }
      });
    }
  }
}
```

- [ ] **Step 2: Add the routes**

In `app.routes.ts`, add near the existing `billing/quotations`/`billing/invoices` routes (inside the same workspace-children array):
```typescript
          { path: 'billing/invoices/new', component: BillingDocumentFormPageComponent, data: { documentType: 'invoice' } },
          { path: 'billing/invoices/:invoiceId/edit', component: BillingDocumentFormPageComponent, data: { documentType: 'invoice' } },
          { path: 'billing/quotations/new', component: BillingDocumentFormPageComponent, data: { documentType: 'quotation' } },
          { path: 'billing/quotations/:quotationId/edit', component: BillingDocumentFormPageComponent, data: { documentType: 'quotation' } },
```
Add the import at the top: `import { BillingDocumentFormPageComponent } from './pages/billing/document-form/billing-document-form-page.component';`

- [ ] **Step 3: Build**

Run: `cd ui && npx ng build 2>&1 | tail -40`
Expected: this file compiles; remaining errors are in the old modals/list pages/job-detail, fixed in the next tasks.

- [ ] **Step 4: Commit**

```bash
git add ui/src/app/pages/billing/document-form/ ui/src/app/app.routes.ts
git commit -m "feat: add shared routed billing document form page"
```

---

### Task 8: Rewire the flat list pages, delete the old modals

**Files:**
- Modify: `ui/src/app/pages/billing/invoices/invoice-list.component.ts`
- Modify: `ui/src/app/pages/billing/quotations/quotation-list.component.ts`
- Delete: `ui/src/app/pages/billing/invoices/invoice-form-modal/invoice-form-modal.component.ts`
- Delete: `ui/src/app/pages/billing/quotations/quotation-form-modal/quotation-form-modal.component.ts`
- Delete: `ui/src/app/pages/billing/quotations/convert-modal/convert-quotation-modal.component.ts`

**Interfaces:**
- Consumes: `BillingDocumentFormPageComponent`'s routes (Task 7).
- Produces: nothing new - this is the last consumer of the deleted modals, so nothing downstream depends on this task's internals beyond "the app still builds."

- [ ] **Step 1: Rewire `invoice-list.component.ts`**

Remove the `InvoiceFormModalComponent` import and its usage in `imports`. Remove the `@if (modalOpen()) { <app-invoice-form-modal ... /> }` block and the `modalOpen`/`editingInvoice` signals/`openCreate`/`openEdit`/`closeModal`/`onSaved` methods that only existed to drive it (keep `payingInvoice`/`sendingInvoice` and their methods - those still apply). Replace the "New invoice" button and each row's number-cell click with router links:
```html
      <button class="btn-primary" [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices', 'new']">New invoice</button>
```
```html
                  <td class="px-lg py-sm text-neutral-900">
                    <a [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices', invoice.invoiceId, 'edit']">{{ invoice.number }}</a>
                  </td>
```
(`RouterLink` is already imported in this file.)

- [ ] **Step 2: Rewire `quotation-list.component.ts`**

Same pattern: remove `QuotationFormModalComponent`/`ConvertQuotationModalComponent` imports and usage, their driving signals/methods (`modalOpen`, `editingQuotation`, `convertingQuotation`, `openCreate`, `openEdit`, `closeModal`, `onSaved`, `openConvert`, `onConverted`). Keep `sendingQuotation`/`openSend`/`onSend`.

"New quotation" and row-number become router links to `billing/quotations/new` / `.../:quotationId/edit`, same shape as invoices above.

"Convert to invoice" becomes "Create invoice", always shown (not gated to `Draft`/`Sent`) except when `Rejected`/`Expired`, routing instead of opening a modal:
```html
                    @if (quotation.status !== 'Rejected' && quotation.status !== 'Expired') {
                      <a
                        class="text-xs text-primary-500 hover:text-primary-600"
                        [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices', 'new']"
                        [queryParams]="{ jobId: quotation.jobId, fromQuotation: quotation.quotationId }"
                      >Create invoice</a>
                    }
```
Add a progress column to the table (`InvoicedAmount / Total`):
```html
                <th class="text-left px-lg py-sm font-medium">Billed</th>
```
```html
                  <td class="px-lg py-sm text-neutral-600">{{ quotation.invoicedAmount | number: '1.2-2' }} / {{ quotation.total | number: '1.2-2' }}</td>
```

- [ ] **Step 3: Delete the three obsolete files**

```bash
git rm ui/src/app/pages/billing/invoices/invoice-form-modal/invoice-form-modal.component.ts
git rm ui/src/app/pages/billing/quotations/quotation-form-modal/quotation-form-modal.component.ts
git rm -r ui/src/app/pages/billing/quotations/convert-modal
```

- [ ] **Step 4: Build**

Run: `cd ui && npx ng build 2>&1 | tail -40`
Expected: errors remain only in `job-detail.component.ts` (Task 9 fixes it - it still imports the two deleted modal components).

- [ ] **Step 5: Commit**

```bash
git add ui/src/app/pages/billing/invoices/invoice-list.component.ts ui/src/app/pages/billing/quotations/quotation-list.component.ts
git commit -m "refactor: replace invoice/quotation modals with routed billing page"
```

---

### Task 9: Job detail - billing section rewire, milestone money chip / lock / bill action / payment rules

**Files:**
- Modify: `ui/src/app/pages/job/job-detail.component.ts`

**Interfaces:**
- Consumes: `BillingDocumentFormPageComponent` routes (Task 7), `MilestoneService.getPaymentStatus`/`getPaymentRequirements`/`setPaymentRequirements` (Task 5).
- Produces: nothing consumed elsewhere - last integration point.

- [ ] **Step 1: Remove the modal imports and their driving state**

Delete the `InvoiceFormModalComponent`/`QuotationFormModalComponent` imports and their entries in the component's `imports` array. Delete the `showInvoiceModal`/`showQuotationModal` signals and their two `@if` blocks (around the current lines 566-583, anchor on `@if (showInvoiceModal())` / `@if (showQuotationModal())`) and whatever `onInvoiceSaved`/`onQuotationSaved`-style handlers only existed to close them and refetch (check for methods referencing these signals and keep the refetch logic if it's reused elsewhere - otherwise delete alongside).

- [ ] **Step 2: Replace the "+ Invoice"/"+ Quotation" buttons with router links**

Anchor on the existing buttons (`(click)="showQuotationModal.set(true)"` / `(click)="showInvoiceModal.set(true)"`, currently around lines 396-397):
```html
              <a class="text-xs text-primary-500 hover:text-primary-600" [routerLink]="['/app/workspace', workspaceId, 'billing', 'quotations', 'new']" [queryParams]="{ jobId: jobId }">+ Quotation</a>
              <a class="text-xs text-primary-500 hover:text-primary-600" [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices', 'new']" [queryParams]="{ jobId: jobId }">+ Invoice</a>
```
(`RouterLink` is already imported in this file, used elsewhere for the "Manage invoices/quotations →" links.)

- [ ] **Step 3: Add milestone payment state loading**

Add a new signal and load method - milestone payment status is fetched per-milestone once the milestone list itself loads (mirrors how `jobInvoices`/`jobQuotations` are already fetched in this file's existing billing-fetch method):
```typescript
  milestonePaymentStatuses = signal<Record<string, MilestonePaymentStatus>>({});
```
Add the import: `import { Milestone, MilestonePaymentStatus, MilestoneService } from '../../core/milestone.service';` (this file already imports `Milestone`/`MilestoneService` from this path - just add `MilestonePaymentStatus` to the existing import list, don't duplicate the import line).

After the existing milestone-list fetch resolves (find where `this.milestones.set(...)` is currently called - it's set once during the page's initial `forkJoin`/fetch, per the original Milestones-section implementation), add:
```typescript
        milestones.forEach(m => {
          this.milestoneService.getPaymentStatus(this.workspaceId, this.jobId, m.milestoneId).subscribe({
            next: status => this.milestonePaymentStatuses.update(map => ({ ...map, [m.milestoneId]: status }))
          });
        });
```

- [ ] **Step 4: Add the money chip, lock icon, and "Bill this milestone" link to each milestone row**

Anchor on the milestone row template (the `<div cdkDrag ...>` block, currently starting around line 218's section). Inside the row, after the title/description block and before or alongside the existing status control, add:
```html
                    @if (milestonePaymentStatuses()[m.milestoneId]; as pay) {
                      @if (pay.linkedInvoiceId) {
                        <a
                          class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-700"
                          [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices', pay.linkedInvoiceId, 'edit']"
                        >{{ pay.amount | number: '1.2-2' }} · {{ pay.linkedInvoiceNumber }}</a>
                        @if (pay.nextGate) {
                          <span class="text-xs" [title]="pay.nextGate">🔒</span>
                        } @else {
                          <span class="text-xs" title="No payment blocking the next status">🔓</span>
                        }
                      } @else if (pay.amount) {
                        <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-700">{{ pay.amount | number: '1.2-2' }}</span>
                        @if (!isClient()) {
                          <a
                            class="text-xs text-primary-500 hover:text-primary-600"
                            [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices', 'new']"
                            [queryParams]="{ jobId: jobId, milestoneId: m.milestoneId }"
                          >Bill this milestone</a>
                        }
                      }
                    }
```
(`isClient()` already exists in this component from the original Milestones section - reuse it, don't redefine it.)

- [ ] **Step 5: Add the collapsible "Payment rules" editor per milestone**

Add state and methods:
```typescript
  editingRulesFor = signal<string | null>(null);
  ruleDrafts: PaymentRequirement[] = [];

  toggleRulesEditor(milestone: Milestone): void {
    if (this.editingRulesFor() === milestone.milestoneId) {
      this.editingRulesFor.set(null);
      return;
    }
    this.editingRulesFor.set(milestone.milestoneId);
    this.milestoneService.getPaymentRequirements(this.workspaceId, this.jobId, milestone.milestoneId).subscribe({
      next: rules => (this.ruleDrafts = [...rules])
    });
  }

  addRule(): void {
    this.ruleDrafts = [...this.ruleDrafts, { targetStatus: 'Completed', requiredState: 'FullyPaid' }];
  }

  removeRule(index: number): void {
    this.ruleDrafts = this.ruleDrafts.filter((_, i) => i !== index);
  }

  saveRules(milestoneId: string): void {
    this.milestoneService.setPaymentRequirements(this.workspaceId, this.jobId, milestoneId, this.ruleDrafts).subscribe({
      next: () => {
        this.editingRulesFor.set(null);
        this.milestoneService.getPaymentStatus(this.workspaceId, this.jobId, milestoneId).subscribe({
          next: status => this.milestonePaymentStatuses.update(map => ({ ...map, [milestoneId]: status }))
        });
      }
    });
  }
```
Add `import { PaymentRequirement } from '../../core/milestone.service';` to the existing milestone-service import line.

Add the collapsed toggle and editor to the row template, hidden for Client (same `@if (!isClient())` guard used elsewhere in this section):
```html
                    @if (!isClient()) {
                      <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" (click)="toggleRulesEditor(m)">Payment rules</button>
                    }
```
And, still inside the row (or immediately after it, matching this file's existing pattern of expanding content below a row - see how the Land section expands `LandDetailPanelComponent` below its row for the established shape):
```html
                  @if (editingRulesFor() === m.milestoneId) {
                    <div class="px-md pb-md pt-sm border-t border-neutral-200 space-y-sm">
                      @for (rule of ruleDrafts; track $index; let i = $index) {
                        <div class="flex items-center gap-sm text-sm">
                          <span>Requires</span>
                          <select class="input-field w-32 py-xs text-xs" [(ngModel)]="rule.targetStatus" [name]="'target-' + i">
                            @for (s of milestoneStatuses; track s) {
                              <option [value]="s">{{ s }}</option>
                            }
                          </select>
                          <span>→</span>
                          <select class="input-field w-36 py-xs text-xs" [(ngModel)]="rule.requiredState" [name]="'state-' + i">
                            <option value="Invoiced">Invoiced</option>
                            <option value="PartiallyPaid">Partially paid</option>
                            <option value="FullyPaid">Fully paid</option>
                          </select>
                          <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="removeRule(i)">✕</button>
                        </div>
                      }
                      <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="addRule()">+ Add rule</button>
                      <div class="flex justify-end">
                        <button type="button" class="btn-primary text-xs" (click)="saveRules(m.milestoneId)">Save rules</button>
                      </div>
                    </div>
                  }
```
(`milestoneStatuses` already exists in this component from the original Milestones section.)

- [ ] **Step 6: Build**

Run: `cd ui && npx ng build 2>&1 | tail -40`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Manual verification**

Start both servers (`preview_start` for "SurveyorLedger API" and "SurveyorLedger UI"), log in, open a job, and walk through: add a milestone with an amount → "Bill this milestone" opens the new invoice page prefilled → save → milestone row shows the money chip linking to the invoice → open the invoice page directly (not via the milestone) and confirm the line item shows the milestone picker with this milestone pre-selected → back on the job page, open "Payment rules" on the milestone, add a `Completed → FullyPaid` rule, save → try changing the milestone's status to Completed → confirm the 400 error surfaces → record a payment on the invoice from the invoice list page → return to the job, confirm the lock icon flips and the status change now succeeds. Separately: create a quotation with two line items, "Create invoice" against it, select only one line item, save, confirm the quotation list's Billed column shows partial progress, then create a second invoice against the same quotation, confirm it's allowed (not blocked as "already converted").

- [ ] **Step 8: Commit**

```bash
git add ui/src/app/pages/job/job-detail.component.ts
git commit -m "feat: wire milestone billing (money chip, payment gate, bill action, rules editor) into Job detail"
```

---

## Self-Review Notes

- **Spec coverage:** Part 1 (fee, freeform gating, no-fee-no-gate default) → Tasks 1, 3. Part 1's line-item link + uniqueness → Task 2. Part 2 (1:many quotation/invoice, dropped convert action, billing progress) → Task 4. Part 3 (shared page, milestone picker, back-nav, quotation draw, progress display, job-detail integration) → Tasks 5-9. Every spec section maps to at least one task.
- **Type consistency:** `MilestonePaymentStatus` record fields (Task 3) match `MilestonePaymentStatusResponse` DTO fields (Task 3) match the frontend `MilestonePaymentStatus` interface (Task 5) match how `job-detail.component.ts` reads them (Task 9) - `amount`/`linkedInvoiceId`/`linkedInvoiceNumber`/`invoiceStatus`/`nextGate` used consistently throughout. `PaymentRequirement.targetStatus`/`requiredState` match `PaymentRequirementDto.TargetStatus`/`RequiredState` (camelCase JSON binding handles the casing). `LineItem.milestoneId` (Task 5) matches `LineItemDto.MilestoneId` (Task 2) and what `LineItemEditorComponent` reads/writes (Task 6) and what `BillingDocumentFormPageComponent` passes through (Task 7).
- **Placeholder scan:** none found - `Task 4`'s `ComputeBillingProgress` (originally drafted as a two-phase placeholder-then-fix) was collapsed into one correct implementation during self-review.
