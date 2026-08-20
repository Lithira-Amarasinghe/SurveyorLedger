# Quotation-Invoice Line Traceability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an invoice line optionally bill a specific quotation line for a specific amount, with hard-blocked over-billing and per-line billing-progress visibility, replacing the current whole-document `Invoice.QuotationId` link.

**Architecture:** `InvoiceLineItem` gains a scalar `QuotationLineId` (no EF nav, same pattern as the existing `MilestoneId`). `Invoice.QuotationId` is removed entirely. `QuotationService.UpdateAsync` switches from wholesale-clear-and-regenerate line IDs to update-in-place-by-`Id`, so a quotation line's identity survives edits once anything is billed against it — and an edit that would break that identity is rejected. `InvoiceService` gains a reusable `GetAmountBilledAgainstQuotationLine` query, used both for the over-billing block on invoice save and for `QuotationService`'s edit-safety guard.

**Tech Stack:** .NET 9, EF Core 9, SQL Server LocalDB, xUnit integration tests against real LocalDB (see `WorkspaceIntegrationTestBase`).

## Global Constraints

- Job-scoped only — no workspace-level (job-less) quotations/invoices in this pass.
- Migrations generated via `dotnet ef migrations add`, never hand-edited (enforced by `guard-migrations.ps1` PreToolUse hook).
- Tenant isolation stays routed through `Job.WorkspaceId` — no `WorkspaceId` column added to Quotation/Invoice.
- `MilestoneId` and `QuotationLineId` are independent fields on a line — no auto-copy, no forced pairing.
- Commit after each task.

---

### Task 1: Schema — entities, EF configuration, migration

**Files:**
- Modify: `api/src/SurveyorLedger.Data/Entities/InvoiceLineItem.cs`
- Modify: `api/src/SurveyorLedger.Data/Entities/Invoice.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/InvoiceConfiguration.cs`
- Create (generated): `api/src/SurveyorLedger.Data/Migrations/<timestamp>_DropInvoiceQuotationIdAddLineQuotationLineId.cs`

**Interfaces:**
- Produces: `InvoiceLineItem.QuotationLineId` (`Guid?`), consumed by Tasks 2, 4, 5.
- Produces: `Invoice` with no `QuotationId`/`Quotation` members — any remaining reference elsewhere is a compile error to fix in Task 2.

- [ ] **Step 1: Add `QuotationLineId` to `InvoiceLineItem`**

Edit `api/src/SurveyorLedger.Data/Entities/InvoiceLineItem.cs`:

```csharp
namespace SurveyorLedger.Data.Entities;

public class InvoiceLineItem
{
    public Guid Id { get; set; }
    public string Description { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public Guid? MilestoneId { get; set; }
    public Guid? QuotationLineId { get; set; }
}
```

- [ ] **Step 2: Remove `QuotationId`/`Quotation` from `Invoice`**

Edit `api/src/SurveyorLedger.Data/Entities/Invoice.cs` — delete the `public Guid? QuotationId { get; set; }` line and the `public Quotation? Quotation { get; set; }` line. Result:

```csharp
namespace SurveyorLedger.Data.Entities;

/// <summary>
/// Draft/Sent/PartiallyPaid/Paid/Overdue/Cancelled. Total/AmountPaid/Balance/DaysOverdue
/// are computed by InvoiceService from LineItems and Payments, never stored - see
/// InvoiceService.ComputeInvoiceTotals for the single source of truth. No WorkspaceId
/// column - tenant scoping goes through Job.WorkspaceId (see JobScopedBilling migration).
/// Quotation linkage lives per-line only - see InvoiceLineItem.QuotationLineId.
/// </summary>
public class Invoice
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid JobId { get; set; }
    public string Number { get; set; }
    public List<InvoiceLineItem> LineItems { get; set; } = new();
    public List<InvoiceInstallment> Installments { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Person Client { get; set; }
    public Job Job { get; set; }
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
```

- [ ] **Step 3: Update `InvoiceConfiguration`**

Edit `api/src/SurveyorLedger.Data/Configurations/InvoiceConfiguration.cs` — inside the `OwnsMany(x => x.LineItems, li => {...})` block, add the new column property after the `MilestoneId` line:

```csharp
li.Property(x => x.MilestoneId);
li.Property(x => x.QuotationLineId);
```

Delete this line entirely (the FK config for the removed `Quotation` nav):

```csharp
builder.HasOne(x => x.Quotation).WithMany().HasForeignKey(x => x.QuotationId).OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 4: Build to confirm the entity/config changes compile (expect downstream compile errors elsewhere — that's Task 2)**

Run: `cd api && dotnet build src/SurveyorLedger.Data`
Expected: `SurveyorLedger.Data` project builds clean (it doesn't reference the DTOs/services that still use `Invoice.QuotationId`, so this project alone should succeed).

- [ ] **Step 5: Generate the migration**

Run: `cd api && dotnet ef migrations add DropInvoiceQuotationIdAddLineQuotationLineId --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`

This will fail to build the startup project first (API still references `Invoice.QuotationId`/`InvoiceRequest.QuotationId` — those aren't fixed until Task 2). **Do not fix it by hand-editing.** Instead, do Step 5a below, then retry.

- [ ] **Step 5a: Temporarily comment out the two API-side compile errors just enough to let `dotnet ef migrations add` build**

This is unavoidable because EF's migration tool builds the whole startup project. In `api/src/SurveyorLedger.API/Services/InvoiceService.cs`, `api/src/SurveyorLedger.API/Services/QuotationService.cs`, `api/src/SurveyorLedger.API/Controllers/InvoicesController.cs`, and `api/src/SurveyorLedger.API/Models/Billing/InvoiceDtos.cs`, every reference to `Invoice.QuotationId`, `InvoiceRequest.QuotationId`, `InvoiceResponse.QuotationId`, and `EnsureQuotationBelongsToJobAsync` will show as a build error. Since Task 2 rewrites all of these properly anyway, **do Task 2 first, then come back and run this migration command last** — reorder: do Task 2's DTO/service/controller edits before generating the migration. Skip Step 5 above until Task 2 is complete; the migration is generated at the end of Task 2 instead (see Task 2 Step 8).

- [ ] **Step 6: Commit schema-only changes together with Task 2 (see Task 2's final commit) — do not commit Task 1 alone, since it doesn't build standalone with the rest of the API project untouched.**

No commit here — proceed directly to Task 2. (This avoids a broken intermediate commit; Task 1 and Task 2 land as one commit.)

---

### Task 2: DTOs, services, controllers — replace `Invoice.QuotationId` with line-level `QuotationLineId`

**Files:**
- Modify: `api/src/SurveyorLedger.API/Models/Billing/QuotationDtos.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Billing/InvoiceDtos.cs`
- Modify: `api/src/SurveyorLedger.API/Services/InvoiceService.cs`
- Modify: `api/src/SurveyorLedger.API/Services/QuotationService.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/InvoicesController.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/QuotationsController.cs`

**Interfaces:**
- Consumes: `InvoiceLineItem.QuotationLineId` (Task 1).
- Produces: `LineItemDto.Id` (`Guid?`) and `LineItemDto.QuotationLineId` (`Guid?`), consumed by Tasks 3, 4, 5, 6.
- Produces: `IInvoiceService.GetAmountBilledAgainstQuotationLine(Guid jobId, Guid quotationLineId, Guid? excludingInvoiceId = null) -> decimal`, consumed by Task 3 (quotation edit guard) and used internally by Task 5.
- Produces: `IQuotationService.ComputeLineProgress(Quotation quotation, Guid quotationLineId) -> (decimal InvoicedAmount, decimal RemainingAmount)`, consumed by Task 4 and the `QuotationsController.ToResponse` change below.
- Produces: `QuotationLineItemResponse { Guid Id; string Description; decimal Quantity; decimal UnitPrice; Guid? MilestoneId; decimal InvoicedAmount; decimal RemainingAmount; }`, replacing `LineItemDto` as the type of `QuotationResponse.LineItems`.

- [ ] **Step 1: Add `Id` and `QuotationLineId` to the shared `LineItemDto`; add `QuotationLineItemResponse`; drop `QuotationId` from `InvoiceRequest`/`InvoiceResponse`**

Edit `api/src/SurveyorLedger.API/Models/Billing/QuotationDtos.cs`:

```csharp
namespace SurveyorLedger.API.Models.Billing;

public class LineItemDto
{
    public Guid? Id { get; set; }
    public string Description { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public Guid? MilestoneId { get; set; }
    public Guid? QuotationLineId { get; set; }
}

public class QuotationRequest
{
    public Guid ClientId { get; set; }
    public Guid JobId { get; set; }
    public List<LineItemDto> LineItems { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string? Status { get; set; }
}

public class SendQuotationRequest
{
    public List<Guid> RecipientPersonIds { get; set; } = new();
}

public class QuotationLineItemResponse
{
    public Guid Id { get; set; }
    public string Description { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public Guid? MilestoneId { get; set; }
    public decimal InvoicedAmount { get; set; }
    public decimal RemainingAmount { get; set; }
}

public class QuotationResponse
{
    public Guid QuotationId { get; set; }
    public Guid ClientId { get; set; }
    public Guid JobId { get; set; }
    public string Number { get; set; }
    public List<QuotationLineItemResponse> LineItems { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; }
    public DateTime? ValidUntil { get; set; }
    public int RevisionNumber { get; set; }
    public decimal InvoicedAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

`QuotationLineId` on `LineItemDto` is meaningful only when the DTO is used for an *invoice* line (points at a quotation line elsewhere); it's simply unused/null when the same DTO is used for a *quotation* line itself.

Edit `api/src/SurveyorLedger.API/Models/Billing/InvoiceDtos.cs` — delete `public Guid? QuotationId { get; set; }` from both `InvoiceRequest` and `InvoiceResponse`. Result for the two affected classes:

```csharp
public class InvoiceRequest
{
    public Guid ClientId { get; set; }
    public Guid JobId { get; set; }
    public List<LineItemDto> LineItems { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Status { get; set; }
    public List<InstallmentDto> Installments { get; set; } = new();
}
```

```csharp
public class InvoiceResponse
{
    public Guid InvoiceId { get; set; }
    public Guid ClientId { get; set; }
    public Guid JobId { get; set; }
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
    public List<InstallmentResponse> Installments { get; set; } = new();
}
```

(`InstallmentDto`, `InstallmentResponse`, `SendInvoiceRequest`, `PaymentRequest`, `PaymentResponse` in that file are untouched.)

- [ ] **Step 2: Rewrite `InvoiceService.ValidateLineItemsAsync` to take `jobId`, validate `QuotationLineId`, and hard-block over-billing**

In `api/src/SurveyorLedger.API/Services/InvoiceService.cs`, replace the whole `ValidateLineItemsAsync` method with:

```csharp
/// <summary>Also enforces "at most one active line item per milestone": a Milestone
/// tagged on this document's line items can't already be tagged on a different active
/// Invoice. excludingInvoiceId lets an update re-save its own existing tag without
/// tripping over itself. Separately, any line carrying a QuotationLineId must point at
/// an active quotation line on this same job, and the total billed against that
/// quotation line (this invoice's own lines plus every other active invoice's) must
/// not exceed the quotation line's Quantity * UnitPrice - partial/progressive billing
/// is allowed, over-billing is not.</summary>
private async Task ValidateLineItemsAsync(List<LineItemDto> items, Guid jobId, Guid? excludingInvoiceId)
{
    if (items.Count == 0)
        throw new ValidationException("At least one line item is required.");
    if (items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0))
        throw new ValidationException("Line item quantity must be positive and unit price cannot be negative.");

    var milestoneIds = items.Where(i => i.MilestoneId.HasValue).Select(i => i.MilestoneId!.Value).ToList();
    if (milestoneIds.Count > 0)
    {
        var conflicting = await _context.Invoices
            .Where(inv => inv.IsActive && (excludingInvoiceId == null || inv.Id != excludingInvoiceId))
            .Where(inv => inv.LineItems.Any(li => li.MilestoneId != null && milestoneIds.Contains(li.MilestoneId.Value)))
            .Select(inv => inv.Number)
            .FirstOrDefaultAsync();
        if (conflicting != null)
            throw new ValidationException($"One of these milestones is already billed on invoice {conflicting}.");
    }

    var quotationLineGroups = items.Where(i => i.QuotationLineId.HasValue).GroupBy(i => i.QuotationLineId!.Value);
    foreach (var group in quotationLineGroups)
    {
        var quotationLine = await FindQuotationLineAsync(group.Key);
        if (quotationLine == null || quotationLine.Value.JobId != jobId)
            throw new ValidationException("QuotationLineId must reference an active quotation line on this same job.");

        var thisInvoiceAmount = group.Sum(i => i.Quantity * i.UnitPrice);
        var otherInvoicesAmount = GetAmountBilledAgainstQuotationLine(jobId, group.Key, excludingInvoiceId);
        var totalBilled = thisInvoiceAmount + otherInvoicesAmount;
        if (totalBilled > quotationLine.Value.Amount)
            throw new ValidationException($"Billing {totalBilled} against this quotation line would exceed its total of {quotationLine.Value.Amount}.");
    }
}

/// <summary>Resolves a QuotationLineId to its owning quotation's JobId and its
/// Quantity * UnitPrice amount, or null if no active quotation currently has a line
/// with that Id. Owned-entity line items have no standalone DbSet, so this goes
/// through Quotations with LineItems included.</summary>
private async Task<(Guid JobId, decimal Amount)?> FindQuotationLineAsync(Guid quotationLineId)
{
    var quotation = await _context.Quotations
        .Include(q => q.LineItems)
        .Where(q => q.IsActive)
        .FirstOrDefaultAsync(q => q.LineItems.Any(li => li.Id == quotationLineId));
    if (quotation == null)
        return null;
    var line = quotation.LineItems.First(li => li.Id == quotationLineId);
    return (quotation.JobId, line.Quantity * line.UnitPrice);
}

/// <summary>Sums Quantity * UnitPrice across every active invoice line on this job whose
/// QuotationLineId matches, optionally excluding one invoice (the one currently being
/// saved, so it doesn't double-count against itself). Used both for the over-billing
/// check above and by QuotationService's edit-safety guard - the single source of
/// truth for "how much has been invoiced against this quotation line so far".</summary>
public decimal GetAmountBilledAgainstQuotationLine(Guid jobId, Guid quotationLineId, Guid? excludingInvoiceId = null)
{
    return _context.Invoices
        .Include(i => i.LineItems)
        .Where(i => i.IsActive && i.JobId == jobId && (excludingInvoiceId == null || i.Id != excludingInvoiceId))
        .AsEnumerable()
        .SelectMany(i => i.LineItems)
        .Where(li => li.QuotationLineId == quotationLineId)
        .Sum(li => li.Quantity * li.UnitPrice);
}
```

Add `GetAmountBilledAgainstQuotationLine` to the `IInvoiceService` interface at the top of the file:

```csharp
decimal GetAmountBilledAgainstQuotationLine(Guid jobId, Guid quotationLineId, Guid? excludingInvoiceId = null);
```

- [ ] **Step 3: Update the two call sites of `ValidateLineItemsAsync` to pass `jobId`, and remove `QuotationId` handling from `CreateAsync`/`UpdateAsync`**

In `CreateAsync`: change

```csharp
if (request.QuotationId.HasValue)
    await EnsureQuotationBelongsToJobAsync(request.QuotationId.Value, request.JobId);
await ValidateLineItemsAsync(request.LineItems, null);
```

to

```csharp
await ValidateLineItemsAsync(request.LineItems, request.JobId, null);
```

and remove `QuotationId = request.QuotationId,` from the `new Invoice { ... }` initializer, and remove the whole `EnsureQuotationBelongsToJobAsync` method (no longer called anywhere).

Also update the `LineItems = request.LineItems.Select(...)` projection in `CreateAsync` to carry the new field:

```csharp
LineItems = request.LineItems.Select(i => new InvoiceLineItem { Id = Guid.NewGuid(), Description = i.Description.Trim(), Quantity = i.Quantity, UnitPrice = i.UnitPrice, MilestoneId = i.MilestoneId, QuotationLineId = i.QuotationLineId }).ToList(),
```

In `UpdateAsync`: change

```csharp
await ValidateLineItemsAsync(request.LineItems, invoiceId);
```

to

```csharp
await ValidateLineItemsAsync(request.LineItems, request.JobId, invoiceId);
```

and update the line-item replacement loop's projection the same way:

```csharp
foreach (var item in request.LineItems.Select(i => new InvoiceLineItem { Id = Guid.NewGuid(), Description = i.Description.Trim(), Quantity = i.Quantity, UnitPrice = i.UnitPrice, MilestoneId = i.MilestoneId, QuotationLineId = i.QuotationLineId }))
```

- [ ] **Step 4: Include `QuotationLineId` in the payment-lock change-detection tuple**

In `EnsureOnlyDueDateChanged`, change the comparison tuples to include the new field so an attempt to silently re-point a paid invoice's line at a different quotation line is caught by the existing lock, same as every other figure-affecting change:

```csharp
var lineItemsChanged = invoice.LineItems.Count != request.LineItems.Count
    || invoice.LineItems.OrderBy(li => li.Id).Select(li => (li.Description, li.Quantity, li.UnitPrice, li.MilestoneId, li.QuotationLineId))
        .Except(request.LineItems.Select(li => (li.Description.Trim(), li.Quantity, li.UnitPrice, li.MilestoneId, li.QuotationLineId))).Any();
```

- [ ] **Step 5: `QuotationService` — update-in-place line editing, edit-safety guard, and `ComputeLineProgress`**

In `api/src/SurveyorLedger.API/Services/QuotationService.cs`, add to `IQuotationService`:

```csharp
(decimal InvoicedAmount, decimal RemainingAmount) ComputeLineProgress(Guid jobId, Guid quotationLineId, decimal lineAmount);
```

Add the implementation next to `ComputeBillingProgress`:

```csharp
/// <summary>Per-line counterpart to ComputeBillingProgress - how much of THIS specific
/// quotation line has been invoiced so far, and how much remains. Delegates the actual
/// sum to InvoiceService.GetAmountBilledAgainstQuotationLine, the single source of
/// truth also used by the over-billing block on invoice save.</summary>
public (decimal InvoicedAmount, decimal RemainingAmount) ComputeLineProgress(Guid jobId, Guid quotationLineId, decimal lineAmount)
{
    var invoiced = _invoiceService.GetAmountBilledAgainstQuotationLine(jobId, quotationLineId);
    return (invoiced, lineAmount - invoiced);
}
```

Replace the line-item section of `UpdateAsync` (currently the wholesale clear-and-regenerate block) with update-in-place-by-`Id`, guarded by an edit-safety check run first:

```csharp
public async Task<Quotation> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid quotationId, QuotationRequest request)
{
    var quotation = await FindQuotationAsync(workspaceId, quotationId);
    await _access.EnsureJobAccessAsync(callerUserId, workspaceId, quotation.JobId, "edit");
    await EnsureClientHoldsBillingRoleOnJobAsync(request.ClientId, request.JobId);
    await ValidateLineItemsAsync(request.LineItems, quotationId);
    ValidateTaxRate(request.TaxRatePercent);
    EnsureLineEditsPreserveInvoicedAmounts(quotation, request.LineItems);

    // Line items changed after the quote was Sent - bump RevisionNumber so
    // "revision charges" have something to point at, without a new entity.
    if (quotation.Status is "Sent" or "Accepted" or "Rejected" or "Expired")
        quotation.RevisionNumber++;

    quotation.ClientId = request.ClientId;
    quotation.JobId = request.JobId;

    // Update-in-place by Id so a line's identity survives an edit - once anything is
    // invoiced against a quotation line, InvoiceLineItem.QuotationLineId depends on that
    // Id staying stable. EnsureLineEditsPreserveInvoicedAmounts (above) already rejected
    // any edit that would remove or shrink an invoiced line below its invoiced amount.
    var currentById = quotation.LineItems.ToDictionary(li => li.Id);
    var keepIds = new HashSet<Guid>();
    foreach (var item in request.LineItems)
    {
        if (item.Id.HasValue && currentById.TryGetValue(item.Id.Value, out var existing))
        {
            existing.Description = item.Description.Trim();
            existing.Quantity = item.Quantity;
            existing.UnitPrice = item.UnitPrice;
            existing.MilestoneId = item.MilestoneId;
            keepIds.Add(existing.Id);
        }
        else
        {
            var created = new QuotationLineItem { Id = Guid.NewGuid(), Description = item.Description.Trim(), Quantity = item.Quantity, UnitPrice = item.UnitPrice, MilestoneId = item.MilestoneId };
            quotation.LineItems.Add(created);
            keepIds.Add(created.Id);
        }
    }
    foreach (var stale in quotation.LineItems.Where(li => !keepIds.Contains(li.Id)).ToList())
    {
        quotation.LineItems.Remove(stale);
        _context.Remove(stale);
    }

    quotation.TaxRatePercent = request.TaxRatePercent;
    quotation.ValidUntil = request.ValidUntil;
    if (request.Status != null)
        quotation.Status = request.Status;
    quotation.UpdatedAt = DateTime.UtcNow;

    await _context.SaveChangesAsync();
    return quotation;
}

/// <summary>Rejects an update that would remove a quotation line, or shrink one below
/// its already-invoiced amount, while any invoice is still actively billed against it.
/// Must run before the update-in-place loop mutates anything.</summary>
private void EnsureLineEditsPreserveInvoicedAmounts(Quotation quotation, List<LineItemDto> requestItems)
{
    var incomingById = requestItems.Where(i => i.Id.HasValue).ToDictionary(i => i.Id!.Value);
    foreach (var current in quotation.LineItems)
    {
        var invoiced = _invoiceService.GetAmountBilledAgainstQuotationLine(quotation.JobId, current.Id);
        if (invoiced <= 0)
            continue;
        if (!incomingById.TryGetValue(current.Id, out var stillPresent))
            throw new ValidationException($"Cannot remove quotation line '{current.Description}' - {invoiced} is already invoiced against it.");
        var newAmount = stillPresent.Quantity * stillPresent.UnitPrice;
        if (newAmount < invoiced)
            throw new ValidationException($"Cannot reduce quotation line '{current.Description}' below its invoiced amount of {invoiced}.");
    }
}
```

Note `ToEntities` (used only by `CreateAsync` now) is unchanged — new quotations have no invoiced history to protect.

- [ ] **Step 6: Wire per-line progress into `QuotationsController.ToResponse`**

Edit `api/src/SurveyorLedger.API/Controllers/QuotationsController.cs`:

```csharp
internal static QuotationResponse ToResponse(Quotation q, IQuotationService quotationService)
{
    var subtotal = q.LineItems.Sum(li => li.Quantity * li.UnitPrice);
    var tax = subtotal * q.TaxRatePercent / 100m;
    var (invoicedAmount, remainingAmount) = quotationService.ComputeBillingProgress(q);
    return new QuotationResponse
    {
        QuotationId = q.Id,
        ClientId = q.ClientId,
        JobId = q.JobId,
        Number = q.Number,
        LineItems = q.LineItems.Select(li =>
        {
            var lineAmount = li.Quantity * li.UnitPrice;
            var (lineInvoiced, lineRemaining) = quotationService.ComputeLineProgress(q.JobId, li.Id, lineAmount);
            return new QuotationLineItemResponse
            {
                Id = li.Id,
                Description = li.Description,
                Quantity = li.Quantity,
                UnitPrice = li.UnitPrice,
                MilestoneId = li.MilestoneId,
                InvoicedAmount = lineInvoiced,
                RemainingAmount = lineRemaining
            };
        }).ToList(),
        TaxRatePercent = q.TaxRatePercent,
        Subtotal = subtotal,
        Total = subtotal + tax,
        Status = q.Status,
        ValidUntil = q.ValidUntil,
        RevisionNumber = q.RevisionNumber,
        InvoicedAmount = invoicedAmount,
        RemainingAmount = remainingAmount,
        CreatedAt = q.CreatedAt,
        UpdatedAt = q.UpdatedAt
    };
}
```

- [ ] **Step 7: Remove `QuotationId` from `InvoicesController.ToResponse` and add `QuotationLineId` to its line projection**

Edit `api/src/SurveyorLedger.API/Controllers/InvoicesController.cs`:

```csharp
internal static InvoiceResponse ToResponse(Invoice i, IInvoiceService invoiceService)
{
    var (total, amountPaid, balance, isOverdue, daysOverdue) = invoiceService.ComputeInvoiceTotals(i);
    var subtotal = i.LineItems.Sum(li => li.Quantity * li.UnitPrice);
    return new InvoiceResponse
    {
        InvoiceId = i.Id,
        ClientId = i.ClientId,
        JobId = i.JobId,
        Number = i.Number,
        LineItems = i.LineItems.Select(li => new LineItemDto { Id = li.Id, Description = li.Description, Quantity = li.Quantity, UnitPrice = li.UnitPrice, MilestoneId = li.MilestoneId, QuotationLineId = li.QuotationLineId }).ToList(),
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
        UpdatedAt = i.UpdatedAt,
        Installments = invoiceService.ComputeInstallmentStatuses(i)
            .Select(x => new InstallmentResponse { Amount = x.Installment.Amount, DueDate = x.Installment.DueDate, Status = x.Status })
            .ToList()
    };
}
```

(Also add `Id = li.Id` to the equivalent line-item projection inside `QuotationsController` isn't needed — that controller now uses `QuotationLineItemResponse` from Step 6, which already sets `Id`.)

- [ ] **Step 8: Build the full solution, then generate the migration**

Run: `cd api && dotnet build`
Expected: builds clean, 0 errors.

Run: `cd api && dotnet ef migrations add DropInvoiceQuotationIdAddLineQuotationLineId --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`
Expected: new migration file generated under `api/src/SurveyorLedger.Data/Migrations/` whose `Up()` drops the `Invoices.QuotationId` FK/index/column and adds `InvoiceLineItems.QuotationLineId`.

Open the generated migration and confirm (read-only check, per the migration-check skill — do not hand-edit):
- `Up()` contains `DropForeignKey`/`DropColumn` for `Invoices.QuotationId` (and its index if one existed).
- `Up()` contains `AddColumn` for `InvoiceLineItems.QuotationLineId`.
- `Down()` reverses both.

- [ ] **Step 9: Apply the migration to LocalDB**

Run: `cd api && dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`
Expected: succeeds, no errors.

- [ ] **Step 10: Commit**

```bash
cd D:/Lithira/Projects/SurveyorLedger
git add api/src/SurveyorLedger.Data/Entities/InvoiceLineItem.cs api/src/SurveyorLedger.Data/Entities/Invoice.cs api/src/SurveyorLedger.Data/Configurations/InvoiceConfiguration.cs api/src/SurveyorLedger.Data/Migrations api/src/SurveyorLedger.API/Models/Billing/QuotationDtos.cs api/src/SurveyorLedger.API/Models/Billing/InvoiceDtos.cs api/src/SurveyorLedger.API/Services/InvoiceService.cs api/src/SurveyorLedger.API/Services/QuotationService.cs api/src/SurveyorLedger.API/Controllers/InvoicesController.cs api/src/SurveyorLedger.API/Controllers/QuotationsController.cs
git commit -m "feat: replace invoice-level QuotationId with per-line QuotationLineId

Invoice.QuotationId is removed; an invoice line can now optionally bill
a specific quotation line via InvoiceLineItem.QuotationLineId. Adds a
hard over-billing block (sum billed against a quotation line can't
exceed its total) and per-line invoiced/remaining progress on the
quotation response. QuotationService.UpdateAsync now updates lines
in place by Id instead of clearing and regenerating them, since a
line's identity must survive edits once anything is billed against it."
```

---

### Task 3: Quotation line identity stability — tests

**Files:**
- Modify: `api/tests/SurveyorLedger.API.Tests/Services/QuotationServiceTests.cs`

**Interfaces:**
- Consumes: `LineItemDto.Id`, `QuotationService.UpdateAsync` update-in-place behavior, `EnsureLineEditsPreserveInvoicedAmounts` guard (Task 2).

- [ ] **Step 1: Write failing test — update-in-place preserves `Id` for a matched line, assigns new `Id` for a new line**

Add to `QuotationServiceTests.cs` (follow the existing file's setup pattern — read the file first to match its exact `ConfigureServices`/seed helpers before adding):

```csharp
[Fact]
public async Task UpdateAsync_PreservesLineIdForMatchedLine_AssignsNewIdForNewLine()
{
    var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
    {
        ClientId = _clientPersonId, JobId = _jobId,
        LineItems = new() { new LineItemDto { Description = "Survey", Quantity = 1, UnitPrice = 50000m } },
        TaxRatePercent = 0
    });
    var originalLineId = quotation.LineItems[0].Id;

    var updated = await _quotationService.UpdateAsync(WorkspaceId, AdminId, quotation.Id, new QuotationRequest
    {
        ClientId = _clientPersonId, JobId = _jobId,
        LineItems = new()
        {
            new LineItemDto { Id = originalLineId, Description = "Survey", Quantity = 1, UnitPrice = 55000m },
            new LineItemDto { Description = "Extra visit", Quantity = 1, UnitPrice = 5000m }
        },
        TaxRatePercent = 0
    });

    Assert.Equal(2, updated.LineItems.Count);
    var survey = updated.LineItems.Single(li => li.Description == "Survey");
    Assert.Equal(originalLineId, survey.Id);
    Assert.Equal(55000m, survey.UnitPrice);
    var extra = updated.LineItems.Single(li => li.Description == "Extra visit");
    Assert.NotEqual(originalLineId, extra.Id);
}
```

- [ ] **Step 2: Run test to verify it passes (this exercises Task 2's already-implemented behavior)**

Run: `cd api && dotnet test --filter QuotationServiceTests`
Expected: PASS. If it fails, the update-in-place logic from Task 2 Step 5 has a bug — fix `QuotationService.UpdateAsync` before continuing.

- [ ] **Step 3: Write failing test — removing an invoiced line is rejected**

```csharp
[Fact]
public async Task UpdateAsync_RejectsRemovingALineWithInvoicedAmount()
{
    var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
    {
        ClientId = _clientPersonId, JobId = _jobId,
        LineItems = new()
        {
            new LineItemDto { Description = "Survey", Quantity = 1, UnitPrice = 50000m },
            new LineItemDto { Description = "Plan", Quantity = 1, UnitPrice = 20000m }
        },
        TaxRatePercent = 0
    });
    var surveyLineId = quotation.LineItems.Single(li => li.Description == "Survey").Id;

    await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
    {
        ClientId = _clientPersonId, JobId = _jobId,
        LineItems = new() { new LineItemDto { Description = "Survey (advance)", Quantity = 1, UnitPrice = 20000m, QuotationLineId = surveyLineId } },
        TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
    });

    var requestWithoutSurveyLine = new QuotationRequest
    {
        ClientId = _clientPersonId, JobId = _jobId,
        LineItems = new() { new LineItemDto { Id = quotation.LineItems.Single(li => li.Description == "Plan").Id, Description = "Plan", Quantity = 1, UnitPrice = 20000m } },
        TaxRatePercent = 0
    };

    await Assert.ThrowsAsync<ValidationException>(() => _quotationService.UpdateAsync(WorkspaceId, AdminId, quotation.Id, requestWithoutSurveyLine));
}
```

This test needs `_invoiceService` available in the test class — check the existing `QuotationServiceTests.cs` `ConfigureServices`/fields; if `IInvoiceService` isn't already registered there, add `services.AddScoped<IInvoiceService, InvoiceService>();` to `ConfigureServices` and a `private IInvoiceService _invoiceService = null!;` field initialized the same way `_quotationService` is.

- [ ] **Step 4: Write failing test — shrinking an invoiced line below its invoiced amount is rejected**

```csharp
[Fact]
public async Task UpdateAsync_RejectsShrinkingALineBelowItsInvoicedAmount()
{
    var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
    {
        ClientId = _clientPersonId, JobId = _jobId,
        LineItems = new() { new LineItemDto { Description = "Survey", Quantity = 1, UnitPrice = 50000m } },
        TaxRatePercent = 0
    });
    var surveyLineId = quotation.LineItems[0].Id;

    await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
    {
        ClientId = _clientPersonId, JobId = _jobId,
        LineItems = new() { new LineItemDto { Description = "Survey (advance)", Quantity = 1, UnitPrice = 30000m, QuotationLineId = surveyLineId } },
        TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
    });

    var shrunkRequest = new QuotationRequest
    {
        ClientId = _clientPersonId, JobId = _jobId,
        LineItems = new() { new LineItemDto { Id = surveyLineId, Description = "Survey", Quantity = 1, UnitPrice = 25000m } },
        TaxRatePercent = 0
    };

    await Assert.ThrowsAsync<ValidationException>(() => _quotationService.UpdateAsync(WorkspaceId, AdminId, quotation.Id, shrunkRequest));
}
```

- [ ] **Step 5: Run all three new tests, verify pass**

Run: `cd api && dotnet test --filter QuotationServiceTests`
Expected: PASS, all tests in the file including the three new ones.

- [ ] **Step 6: Commit**

```bash
git add api/tests/SurveyorLedger.API.Tests/Services/QuotationServiceTests.cs
git commit -m "test: cover quotation line identity stability on update"
```

---

### Task 4: Partial billing and over-billing block — tests

**Files:**
- Create: `api/tests/SurveyorLedger.API.Tests/Services/QuotationInvoiceLineTraceabilityTests.cs`
- Delete: `api/tests/SurveyorLedger.API.Tests/Services/QuotationManyInvoicesTests.cs` (its tests assumed `Invoice.QuotationId`, which no longer exists — superseded by the new file)

**Interfaces:**
- Consumes: `LineItemDto.QuotationLineId`, `InvoiceService.GetAmountBilledAgainstQuotationLine`, `QuotationService.ComputeLineProgress`, `QuotationsController.ToResponse`'s per-line `InvoicedAmount`/`RemainingAmount` (all from Task 2).

- [ ] **Step 1: Delete the superseded test file**

Run: `rm api/tests/SurveyorLedger.API.Tests/Services/QuotationManyInvoicesTests.cs` (from repo root, or the Windows equivalent — delete the file).

- [ ] **Step 2: Write the new test file, starting with the seed helper and one passing test (partial billing across two invoices)**

Create `api/tests/SurveyorLedger.API.Tests/Services/QuotationInvoiceLineTraceabilityTests.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class QuotationInvoiceLineTraceabilityTests : WorkspaceIntegrationTestBase
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
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IPdfService, PdfService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-quotation-invoice-line-test-{Guid.NewGuid():N}"),
                    ["AppSettings:UiBaseUrl"] = "https://test.local"
                })
                .Build());
    }

    private async Task<Quotation> SeedQuotationAsync()
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
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 80000m } },
            TaxRatePercent = 0
        });
    }

    private InvoiceRequest InvoiceFor(Guid quotationLineId, decimal amount) => new()
    {
        ClientId = _clientPersonId, JobId = _jobId,
        LineItems = new() { new LineItemDto { Description = "Land Survey (partial)", Quantity = 1, UnitPrice = amount, QuotationLineId = quotationLineId } },
        TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
    };

    [Fact]
    public async Task TwoInvoices_CanPartiallyBillTheSameQuotationLine()
    {
        var quotation = await SeedQuotationAsync();
        var lineId = quotation.LineItems[0].Id;

        var first = await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceFor(lineId, 40000m));
        var second = await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceFor(lineId, 40000m));

        Assert.Equal(lineId, first.LineItems[0].QuotationLineId);
        Assert.Equal(lineId, second.LineItems[0].QuotationLineId);
    }

    [Fact]
    public async Task ThirdInvoice_ExceedingRemainingAmount_IsRejected()
    {
        var quotation = await SeedQuotationAsync();
        var lineId = quotation.LineItems[0].Id;

        await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceFor(lineId, 40000m));
        await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceFor(lineId, 40000m));

        await Assert.ThrowsAsync<ValidationException>(() => _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceFor(lineId, 1m)));
    }

    [Fact]
    public async Task QuotationLineFromADifferentJob_IsRejected()
    {
        var quotation = await SeedQuotationAsync();
        var lineId = quotation.LineItems[0].Id;
        var otherJob = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job B" });
        var otherClientPersonId = await GrantClientBillingRoleAsync(otherJob.Id);

        var request = InvoiceFor(lineId, 10000m);
        request.JobId = otherJob.Id;
        request.ClientId = otherClientPersonId;

        await Assert.ThrowsAsync<ValidationException>(() => _invoiceService.CreateAsync(WorkspaceId, AdminId, request));
    }

    [Fact]
    public async Task GetAmountBilledAgainstQuotationLine_SumsAcrossActiveInvoicesOnly()
    {
        var quotation = await SeedQuotationAsync();
        var lineId = quotation.LineItems[0].Id;
        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceFor(lineId, 40000m));

        Assert.Equal(40000m, _invoiceService.GetAmountBilledAgainstQuotationLine(_jobId, lineId));

        await _invoiceService.DeleteAsync(WorkspaceId, AdminId, invoice.Id);

        Assert.Equal(0m, _invoiceService.GetAmountBilledAgainstQuotationLine(_jobId, lineId));
    }

    [Fact]
    public async Task QuotationLineProgress_ReflectsInvoicedAndRemainingAfterEachInvoice()
    {
        var quotation = await SeedQuotationAsync();
        var lineId = quotation.LineItems[0].Id;

        var (invoicedBefore, remainingBefore) = _quotationService.ComputeLineProgress(_jobId, lineId, 80000m);
        Assert.Equal(0m, invoicedBefore);
        Assert.Equal(80000m, remainingBefore);

        await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceFor(lineId, 40000m));

        var (invoicedAfter, remainingAfter) = _quotationService.ComputeLineProgress(_jobId, lineId, 80000m);
        Assert.Equal(40000m, invoicedAfter);
        Assert.Equal(40000m, remainingAfter);
    }
}
```

- [ ] **Step 3: Run the new tests**

Run: `cd api && dotnet test --filter QuotationInvoiceLineTraceabilityTests`
Expected: PASS, all 5 tests.

- [ ] **Step 4: Commit**

```bash
git add api/tests/SurveyorLedger.API.Tests/Services/QuotationInvoiceLineTraceabilityTests.cs
git rm api/tests/SurveyorLedger.API.Tests/Services/QuotationManyInvoicesTests.cs
git commit -m "test: replace invoice-level quotation tests with per-line traceability tests

QuotationManyInvoicesTests exercised Invoice.QuotationId, which this
feature removed. Its coverage is superseded by
QuotationInvoiceLineTraceabilityTests, which covers partial billing,
the over-billing block, cross-job rejection, and per-line progress."
```

---

### Task 5: Full suite verification

**Files:** none (verification only)

**Interfaces:** none

- [ ] **Step 1: Run the full backend test suite**

Run: `cd api && dotnet test`
Expected: all tests pass, 0 failures. Given the earlier flakiness fix (`parallelizeTestCollections: false`), this should be deterministic — if anything fails, re-run once to confirm it's a real regression and not environment noise before investigating.

- [ ] **Step 2: Confirm no other file still references the removed `Invoice.QuotationId` / `InvoiceRequest.QuotationId` / `InvoiceResponse.QuotationId` / `EnsureQuotationBelongsToJobAsync`**

Run: `cd api && grep -rn "QuotationId" src/SurveyorLedger.API/Services/InvoiceService.cs src/SurveyorLedger.API/Models/Billing/InvoiceDtos.cs src/SurveyorLedger.API/Controllers/InvoicesController.cs src/SurveyorLedger.Data/Entities/Invoice.cs`
Expected: no matches (grep exits non-zero / empty output). If anything matches, it's leftover from Task 2 — fix it.

- [ ] **Step 3: No commit for this task** — it's verification only. If Step 1 or 2 surfaces a problem, fix it in the relevant earlier task's files and amend that task's commit history isn't needed — just make a new small fix commit referencing which task it corrects.
