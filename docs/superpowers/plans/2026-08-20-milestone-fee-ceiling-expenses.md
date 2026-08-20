# Milestone Fee Ceiling, Expenses, and Profitability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn `Milestone.Amount` into an enforced ceiling shared across both billing routes (quotation-line and direct-invoice), add milestone-tagged expenses and profitability, and adapt the existing single-invoice payment-gating feature to work under partial/multi-invoice billing.

**Architecture:** `MilestoneService` gains a `GetCommittedAmountAsync`/`EnsureWithinFeeCeilingAsync` pair that sums quotation lines (any non-Rejected/Expired status) plus direct-invoice lines (excluding quotation-drawn ones, already counted via their quotation line) tagged with a `MilestoneId`. `InvoiceService` and `QuotationService` call this instead of their old one-line-ever uniqueness checks. `InvoiceService` also auto-copies `MilestoneId` from a linked `QuotationLineId`'s target line. `Expense` gains a `MilestoneId` tag for profitability. The existing payment-gating logic (`FindLinkedInvoiceAsync`, singular) is rewritten for multiple linked invoices.

**Tech Stack:** .NET 9, EF Core 9, SQL Server LocalDB, xUnit integration tests against real LocalDB.

## Global Constraints

- Job-scoped only, building on the quotation-invoice line traceability feature already shipped.
- Migrations generated via `dotnet ef migrations add`, never hand-edited.
- No per-line payment allocation — Revenue is invoiced-amount, not paid-amount.
- No auto-supersede of quotations — a superseded quotation must be manually set to `Rejected`.
- `MilestoneId` and `QuotationLineId` on an invoice line: if `QuotationLineId` is set and the target quotation line has a `MilestoneId`, the invoice line's `MilestoneId` is auto-copied from it; an explicit conflicting value is rejected.
- Commit after each task.

---

### Task 1: Schema — `Expense.MilestoneId`, indexes, migration

**Files:**
- Modify: `api/src/SurveyorLedger.Data/Entities/Expense.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/ExpenseConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/InvoiceConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/QuotationConfiguration.cs`
- Create (generated): migration under `api/src/SurveyorLedger.Data/Migrations/`

**Interfaces:**
- Produces: `Expense.MilestoneId` (`Guid?`), consumed by Task 6 (Expense DTOs) and Task 2 (profitability query).

- [ ] **Step 1: Add `MilestoneId` to `Expense`**

In `api/src/SurveyorLedger.Data/Entities/Expense.cs`, add a new line directly after the existing `PayeeType` property (leave `PayeeType` itself untouched):

```csharp
public string? PayeeType { get; set; }
public Guid? MilestoneId { get; set; }
```

- [ ] **Step 2: Configure the new column and indexes**

In `api/src/SurveyorLedger.Data/Configurations/ExpenseConfiguration.cs`, add after the existing `builder.HasIndex(x => x.JobId);`:

```csharp
builder.HasIndex(x => x.JobId);
builder.HasIndex(x => x.MilestoneId);
```

Add the property mapping (no FK constraint - same bare-column pattern as every other `MilestoneId` in this codebase) near the other `Property` calls:

```csharp
builder.Property(x => x.PayeeType).HasMaxLength(30);
builder.Property(x => x.MilestoneId);
```

- [ ] **Step 3: Add indexes on the existing `MilestoneId` columns on line items**

The committed-amount query (Task 2) filters `InvoiceLineItems.MilestoneId` and `QuotationLineItems.MilestoneId` on every quotation/invoice save - add indexes now that this is a hot path.

In `api/src/SurveyorLedger.Data/Configurations/InvoiceConfiguration.cs`, inside the `OwnsMany(x => x.LineItems, li => { ... })` block, add after `li.Property(x => x.QuotationLineId);`:

```csharp
li.Property(x => x.QuotationLineId);
li.HasIndex(x => x.MilestoneId);
```

In `api/src/SurveyorLedger.Data/Configurations/QuotationConfiguration.cs`, inside its `OwnsMany(x => x.LineItems, li => { ... })` block, add after `li.Property(x => x.MilestoneId);`:

```csharp
li.Property(x => x.MilestoneId);
li.HasIndex(x => x.MilestoneId);
```

- [ ] **Step 4: Build, then generate and apply the migration**

Run: `cd api && dotnet build`
Expected: 0 errors (the DTO/service call sites that will eventually use `Expense.MilestoneId` don't exist yet, so nothing references it outside the entity/config - should build clean).

Run: `cd api && dotnet ef migrations add AddMilestoneIdToExpenseAndLineItemIndexes --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`
Expected: migration adds `Expenses.MilestoneId` column, an index on it, and indexes on `InvoiceLineItems.MilestoneId`/`QuotationLineItems.MilestoneId`.

Open the generated migration and confirm `Up()` has one `AddColumn` and three `CreateIndex` calls, `Down()` reverses all four. Do not hand-edit.

Run: `cd api && dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`
Expected: succeeds.

- [ ] **Step 5: Commit**

```bash
git add api/src/SurveyorLedger.Data/Entities/Expense.cs api/src/SurveyorLedger.Data/Configurations/ExpenseConfiguration.cs api/src/SurveyorLedger.Data/Configurations/InvoiceConfiguration.cs api/src/SurveyorLedger.Data/Configurations/QuotationConfiguration.cs api/src/SurveyorLedger.Data/Migrations
git commit -m "feat: add Expense.MilestoneId and index MilestoneId on billing line items

Lays the schema groundwork for milestone profitability (Expense tagging)
and the fee-ceiling committed-amount query, which filters line items by
MilestoneId on every quotation/invoice save.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: `MilestoneService` — committed-amount ceiling and profitability

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/MilestoneService.cs`

**Interfaces:**
- Consumes: `Expense.MilestoneId` (Task 1).
- Produces: `IMilestoneService.GetCommittedAmountAsync(Guid jobId, Guid milestoneId, Guid? excludingQuotationId = null, Guid? excludingInvoiceId = null) -> Task<decimal>`, consumed by Tasks 3, 4, 5, 6.
- Produces: `IMilestoneService.EnsureWithinFeeCeilingAsync(Guid jobId, Guid milestoneId, decimal additionalAmount, Guid? excludingQuotationId = null, Guid? excludingInvoiceId = null) -> Task`, consumed by Tasks 3, 4.
- Produces: `IMilestoneService.ComputeProfitabilityAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId) -> Task<(decimal Revenue, decimal Expenses, decimal Profit)>`, consumed by Task 6.

- [ ] **Step 1: Add the three methods to `IMilestoneService`**

In `api/src/SurveyorLedger.API/Services/MilestoneService.cs`, add to the interface:

```csharp
Task<decimal> GetCommittedAmountAsync(Guid jobId, Guid milestoneId, Guid? excludingQuotationId = null, Guid? excludingInvoiceId = null);
Task EnsureWithinFeeCeilingAsync(Guid jobId, Guid milestoneId, decimal additionalAmount, Guid? excludingQuotationId = null, Guid? excludingInvoiceId = null);
Task<(decimal Revenue, decimal Expenses, decimal Profit)> ComputeProfitabilityAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId);
```

- [ ] **Step 2: Implement `GetCommittedAmountAsync` and `EnsureWithinFeeCeilingAsync`**

Add these methods to the `MilestoneService` class, near `GetPaymentStatusAsync`:

```csharp
/// <summary>Sums everything currently committed against a milestone's fee: every
/// quotation line tagged with this MilestoneId on a non-Rejected/Expired active
/// quotation (Draft/Sent/Accepted all count - two drafts can each partially quote the
/// milestone as long as their sum stays under the fee), plus every direct-invoice line
/// tagged with this MilestoneId whose QuotationLineId is null (a line billed through a
/// quotation is already counted via that quotation line - counting the resulting
/// invoice line too would double-charge the ceiling). excludingQuotationId/
/// excludingInvoiceId let a document being saved exclude its own not-yet-persisted
/// lines from the sum.</summary>
public async Task<decimal> GetCommittedAmountAsync(Guid jobId, Guid milestoneId, Guid? excludingQuotationId = null, Guid? excludingInvoiceId = null)
{
    var quotationCommitted = await _context.Quotations
        .Where(q => q.IsActive && q.JobId == jobId && q.Status != "Rejected" && q.Status != "Expired")
        .Where(q => excludingQuotationId == null || q.Id != excludingQuotationId)
        .SelectMany(q => q.LineItems)
        .Where(li => li.MilestoneId == milestoneId)
        .SumAsync(li => (decimal?)(li.Quantity * li.UnitPrice)) ?? 0m;

    var invoiceCommitted = await _context.Invoices
        .Where(i => i.IsActive && i.JobId == jobId)
        .Where(i => excludingInvoiceId == null || i.Id != excludingInvoiceId)
        .SelectMany(i => i.LineItems)
        .Where(li => li.MilestoneId == milestoneId && li.QuotationLineId == null)
        .SumAsync(li => (decimal?)(li.Quantity * li.UnitPrice)) ?? 0m;

    return quotationCommitted + invoiceCommitted;
}

/// <summary>Also validates that milestoneId resolves to an active milestone on this
/// same job - the old exclusivity checks in InvoiceService/QuotationService never
/// verified this, so it's added here as the single place both now route through.
/// No-ops if the milestone has no fee (Amount == null) - "milestone can exist without
/// a fee" per the design spec.</summary>
public async Task EnsureWithinFeeCeilingAsync(Guid jobId, Guid milestoneId, decimal additionalAmount, Guid? excludingQuotationId = null, Guid? excludingInvoiceId = null)
{
    var milestone = await _context.Milestones.FirstOrDefaultAsync(m => m.Id == milestoneId && m.IsActive);
    if (milestone == null || milestone.JobId != jobId)
        throw new ValidationException("MilestoneId must reference an active milestone on this same job.");
    if (milestone.Amount == null)
        return;

    var committed = await GetCommittedAmountAsync(jobId, milestoneId, excludingQuotationId, excludingInvoiceId);
    var total = committed + additionalAmount;
    if (total > milestone.Amount)
        throw new ValidationException($"Billing {total} against milestone '{milestone.Title}' would exceed its fee of {milestone.Amount}.");
}
```

- [ ] **Step 3: Implement `ComputeProfitabilityAsync`**

Add near `GetPaymentStatusAsync`:

```csharp
/// <summary>Revenue is invoiced-amount, not paid-amount: Payment is recorded per
/// Invoice as a whole document, not per line, so there's no reliable way to know how
/// much of one milestone's line within a multi-line invoice has actually been
/// collected. Quotation lines are excluded - a quotation is a proposal, not revenue,
/// even though it counts toward the fee ceiling in GetCommittedAmountAsync.</summary>
public async Task<(decimal Revenue, decimal Expenses, decimal Profit)> ComputeProfitabilityAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId)
{
    await FindJobAsync(workspaceId, jobId);
    await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");
    await FindMilestoneAsync(jobId, milestoneId);

    var revenue = await _context.Invoices
        .Where(i => i.IsActive && i.JobId == jobId)
        .SelectMany(i => i.LineItems)
        .Where(li => li.MilestoneId == milestoneId)
        .SumAsync(li => (decimal?)(li.Quantity * li.UnitPrice)) ?? 0m;

    var expenses = await _context.Expenses
        .Where(e => e.JobId == jobId && e.MilestoneId == milestoneId)
        .SumAsync(e => (decimal?)e.Amount) ?? 0m;

    return (revenue, expenses, revenue - expenses);
}
```

- [ ] **Step 4: Build**

Run: `cd api && dotnet build`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add api/src/SurveyorLedger.API/Services/MilestoneService.cs
git commit -m "feat: add milestone committed-amount ceiling and profitability queries

GetCommittedAmountAsync sums quotation lines (any non-Rejected/Expired
status) plus direct-invoice lines (excluding quotation-drawn ones,
already counted via their quotation line) tagged with a milestone.
EnsureWithinFeeCeilingAsync is the single gate InvoiceService and
QuotationService will route through instead of their old one-line-ever
uniqueness checks. ComputeProfitabilityAsync is Revenue (invoiced
amount) minus Expenses (milestone-tagged) - not wired to callers yet.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: Wire the ceiling and auto-copy into `InvoiceService`

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/InvoiceService.cs`

**Interfaces:**
- Consumes: `IMilestoneService.EnsureWithinFeeCeilingAsync` (Task 2).
- Produces: `InvoiceService` now takes `IMilestoneService` in its constructor - update every place that constructs it (test `ConfigureServices` blocks register it via DI already through `services.AddScoped<IMilestoneService, MilestoneService>();` where present; Task 7/8 add it where missing).

- [ ] **Step 1: Inject `IMilestoneService`**

In `api/src/SurveyorLedger.API/Services/InvoiceService.cs`, add a field and constructor parameter:

```csharp
private readonly ApplicationDbContext _context;
private readonly IScopedAccessService _access;
private readonly IFileStorageService _fileStorage;
private readonly IPdfService _pdfService;
private readonly IEmailService _emailService;
private readonly IMilestoneService _milestoneService;
private readonly ILogger<InvoiceService> _logger;

public InvoiceService(ApplicationDbContext context, IScopedAccessService access, IFileStorageService fileStorage, IPdfService pdfService, IEmailService emailService, IMilestoneService milestoneService, ILogger<InvoiceService> logger)
{
    _context = context;
    _access = access;
    _fileStorage = fileStorage;
    _pdfService = pdfService;
    _emailService = emailService;
    _milestoneService = milestoneService;
    _logger = logger;
}
```

- [ ] **Step 2: Extend `FindQuotationLineAsync` to also return the target line's `MilestoneId`**

Replace:

```csharp
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
```

with:

```csharp
private async Task<(Guid JobId, decimal Amount, Guid? MilestoneId)?> FindQuotationLineAsync(Guid quotationLineId)
{
    var quotation = await _context.Quotations
        .Include(q => q.LineItems)
        .Where(q => q.IsActive)
        .FirstOrDefaultAsync(q => q.LineItems.Any(li => li.Id == quotationLineId));
    if (quotation == null)
        return null;
    var line = quotation.LineItems.First(li => li.Id == quotationLineId);
    return (quotation.JobId, line.Quantity * line.UnitPrice, line.MilestoneId);
}
```

- [ ] **Step 3: Rewrite `ValidateLineItemsAsync` - drop the old exclusivity block, add auto-copy and the ceiling check**

Replace the whole method:

```csharp
/// <summary>Any line carrying a QuotationLineId must point at an active quotation line
/// on this same job; if that quotation line carries a MilestoneId, it's auto-copied
/// onto the invoice line (an explicit conflicting MilestoneId on such a line is
/// rejected) so milestone rollups are a single-field query. The total billed against
/// that quotation line (this invoice's own lines plus every other active invoice's)
/// must not exceed the quotation line's Quantity * UnitPrice. Separately, every line
/// carrying a bare MilestoneId (no QuotationLineId - a direct, not quotation-drawn,
/// charge) is grouped by milestone and checked against
/// MilestoneService.EnsureWithinFeeCeilingAsync - the milestone's fee ceiling, shared
/// with whatever's already committed via quotation lines for the same milestone.</summary>
private async Task ValidateLineItemsAsync(List<LineItemDto> items, Guid jobId, Guid? excludingInvoiceId)
{
    if (items.Count == 0)
        throw new ValidationException("At least one line item is required.");
    if (items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0))
        throw new ValidationException("Line item quantity must be positive and unit price cannot be negative.");

    var quotationLineGroups = items.Where(i => i.QuotationLineId.HasValue).GroupBy(i => i.QuotationLineId!.Value);
    foreach (var group in quotationLineGroups)
    {
        var quotationLine = await FindQuotationLineAsync(group.Key);
        if (quotationLine == null || quotationLine.Value.JobId != jobId)
            throw new ValidationException("QuotationLineId must reference an active quotation line on this same job.");

        foreach (var item in group)
        {
            if (item.MilestoneId.HasValue && item.MilestoneId != quotationLine.Value.MilestoneId)
                throw new ValidationException("A line's MilestoneId must match its linked quotation line's milestone.");
            item.MilestoneId = quotationLine.Value.MilestoneId;
        }

        var thisInvoiceAmount = group.Sum(i => i.Quantity * i.UnitPrice);
        var otherInvoicesAmount = GetAmountBilledAgainstQuotationLine(jobId, group.Key, excludingInvoiceId);
        var totalBilled = thisInvoiceAmount + otherInvoicesAmount;
        if (totalBilled > quotationLine.Value.Amount)
            throw new ValidationException($"Billing {totalBilled} against this quotation line would exceed its total of {quotationLine.Value.Amount}.");
    }

    var directMilestoneGroups = items.Where(i => i.MilestoneId.HasValue && !i.QuotationLineId.HasValue).GroupBy(i => i.MilestoneId!.Value);
    foreach (var group in directMilestoneGroups)
    {
        var amount = group.Sum(i => i.Quantity * i.UnitPrice);
        await _milestoneService.EnsureWithinFeeCeilingAsync(jobId, group.Key, amount, excludingInvoiceId: excludingInvoiceId);
    }
}
```

- [ ] **Step 4: Build**

Run: `cd api && dotnet build`
Expected: errors in test files that construct `InvoiceService`/`QuotationService` without `IMilestoneService` registered - that's expected here, fixed in Task 7/8. Confirm the error is specifically about missing `IMilestoneService` DI registration in test `ConfigureServices`, not a compile error in the service itself:

Run: `cd api && dotnet build src/SurveyorLedger.API`
Expected: 0 errors (the API project itself, excluding tests, should build clean - DI registration is a runtime concern in `Program.cs`, not a compile error).

- [ ] **Step 5: Register `IMilestoneService` in `Program.cs` if not already present**

Check `api/src/SurveyorLedger.API/Program.cs` for `services.AddScoped<IMilestoneService, MilestoneService>();` (or equivalent DI registration line). It should already be there from the earlier milestone-payment feature - if missing, add it next to the other `IInvoiceService`/`IQuotationService` registrations.

- [ ] **Step 6: Commit**

```bash
git add api/src/SurveyorLedger.API/Services/InvoiceService.cs api/src/SurveyorLedger.API/Program.cs
git commit -m "feat: route invoice milestone billing through the fee-ceiling check

Replaces the old one-line-ever milestone exclusivity rule with
MilestoneService.EnsureWithinFeeCeilingAsync, and auto-copies
MilestoneId from a linked QuotationLineId's target line so a
quotation-drawn invoice line doesn't need its own explicit tag.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 4: Wire the ceiling into `QuotationService`

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/QuotationService.cs`

**Interfaces:**
- Consumes: `IMilestoneService.EnsureWithinFeeCeilingAsync` (Task 2).
- Produces: `QuotationService` now takes `IMilestoneService` in its constructor.

- [ ] **Step 1: Inject `IMilestoneService`**

```csharp
private readonly ApplicationDbContext _context;
private readonly IScopedAccessService _access;
private readonly IPdfService _pdfService;
private readonly IEmailService _emailService;
private readonly IInvoiceService _invoiceService;
private readonly IMilestoneService _milestoneService;
private readonly ILogger<QuotationService> _logger;

public QuotationService(ApplicationDbContext context, IScopedAccessService access, IPdfService pdfService, IEmailService emailService, IInvoiceService invoiceService, IMilestoneService milestoneService, ILogger<QuotationService> logger)
{
    _context = context;
    _access = access;
    _pdfService = pdfService;
    _emailService = emailService;
    _invoiceService = invoiceService;
    _milestoneService = milestoneService;
    _logger = logger;
}
```

- [ ] **Step 2: Give `ValidateLineItemsAsync` a `jobId` parameter and replace the exclusivity block with the ceiling check**

Replace:

```csharp
private async Task ValidateLineItemsAsync(List<LineItemDto> items, Guid? excludingQuotationId)
{
    if (items.Count == 0)
        throw new ValidationException("At least one line item is required.");
    if (items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0))
        throw new ValidationException("Line item quantity must be positive and unit price cannot be negative.");

    var milestoneIds = items.Where(i => i.MilestoneId.HasValue).Select(i => i.MilestoneId!.Value).ToList();
    if (milestoneIds.Count == 0)
        return;

    var conflicting = await _context.Quotations
        .Where(q => q.IsActive && (excludingQuotationId == null || q.Id != excludingQuotationId))
        .Where(q => q.LineItems.Any(li => li.MilestoneId != null && milestoneIds.Contains(li.MilestoneId.Value)))
        .Select(q => q.Number)
        .FirstOrDefaultAsync();
    if (conflicting != null)
        throw new ValidationException($"One of these milestones is already quoted on {conflicting}.");
}
```

with:

```csharp
/// <summary>Every line carrying a MilestoneId is grouped by milestone and checked
/// against MilestoneService.EnsureWithinFeeCeilingAsync - the fee ceiling shared with
/// whatever's already committed via direct-invoice lines for the same milestone.
/// excludingQuotationId lets an update exclude this quotation's own current lines from
/// the sum before re-adding its (possibly changed) new amount.</summary>
private async Task ValidateLineItemsAsync(List<LineItemDto> items, Guid jobId, Guid? excludingQuotationId)
{
    if (items.Count == 0)
        throw new ValidationException("At least one line item is required.");
    if (items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0))
        throw new ValidationException("Line item quantity must be positive and unit price cannot be negative.");

    var milestoneGroups = items.Where(i => i.MilestoneId.HasValue).GroupBy(i => i.MilestoneId!.Value);
    foreach (var group in milestoneGroups)
    {
        var amount = group.Sum(i => i.Quantity * i.UnitPrice);
        await _milestoneService.EnsureWithinFeeCeilingAsync(jobId, group.Key, amount, excludingQuotationId: excludingQuotationId);
    }
}
```

- [ ] **Step 3: Update the two call sites**

In `CreateAsync`, change `await ValidateLineItemsAsync(request.LineItems, null);` to `await ValidateLineItemsAsync(request.LineItems, request.JobId, null);`.

In `UpdateAsync`, change `await ValidateLineItemsAsync(request.LineItems, quotationId);` to `await ValidateLineItemsAsync(request.LineItems, request.JobId, quotationId);`.

- [ ] **Step 4: Build**

Run: `cd api && dotnet build src/SurveyorLedger.API`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add api/src/SurveyorLedger.API/Services/QuotationService.cs
git commit -m "feat: route quotation milestone billing through the fee-ceiling check

Same ceiling MilestoneService.EnsureWithinFeeCeilingAsync that
InvoiceService now uses, replacing the old one-line-ever milestone
exclusivity rule on the quotation side. Two Draft quotations can each
partially quote the same milestone as long as their sum stays under
the fee.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 5: Payment-gating adaptation for multiple linked invoices

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/MilestoneService.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Milestone/MilestonePaymentRequirementDtos.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/MilestoneController.cs`

**Interfaces:**
- Consumes: `IMilestoneService.GetCommittedAmountAsync` (Task 2).
- Produces: `record LinkedInvoiceSummary(Guid InvoiceId, string Number, string Status)` and the updated `record MilestonePaymentStatus(decimal? Amount, decimal CommittedAmount, decimal? RemainingAmount, List<LinkedInvoiceSummary> LinkedInvoices, string? NextGate)`, consumed by the controller.

- [ ] **Step 1: Replace `FindLinkedInvoiceAsync` with the plural form**

In `MilestoneService.cs`, replace:

```csharp
/// <summary>The invoice, if any, carrying a line item tagged with this milestone - at
/// most one, per the uniqueness rule enforced in InvoiceService.ValidateLineItemsAsync.</summary>
private async Task<Invoice?> FindLinkedInvoiceAsync(Guid milestoneId) =>
    await _context.Invoices.Include(i => i.LineItems)
        .FirstOrDefaultAsync(i => i.IsActive && i.LineItems.Any(li => li.MilestoneId == milestoneId));
```

with:

```csharp
/// <summary>Every active invoice carrying a line item tagged with this milestone -
/// partial billing means this is no longer at most one, unlike before the fee-ceiling
/// feature. Payments included since gate satisfaction needs each invoice's AmountPaid.</summary>
private async Task<List<Invoice>> FindLinkedInvoicesAsync(Guid milestoneId) =>
    await _context.Invoices.Include(i => i.LineItems).Include(i => i.Payments)
        .Where(i => i.IsActive && i.LineItems.Any(li => li.MilestoneId == milestoneId))
        .ToListAsync();
```

- [ ] **Step 2: Rewrite `IsRequirementSatisfied` and `ResolveNextGateAsync` for the plural list**

Replace:

```csharp
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

with:

```csharp
/// <summary>"FullyPaid" now requires both that the milestone is fully committed (its
/// fee ceiling is exactly met - partial billing means "some invoice is Paid" is no
/// longer sufficient on its own) and that every linked invoice has actually been paid
/// off.</summary>
private static bool IsRequirementSatisfied(string requiredState, Milestone milestone, List<Invoice> linkedInvoices, decimal committedAmount)
{
    if (linkedInvoices.Count == 0)
        return false;
    return requiredState switch
    {
        "Invoiced" => linkedInvoices.Any(i => i.Status is "Sent" or "PartiallyPaid" or "Paid"),
        "PartiallyPaid" => linkedInvoices.Sum(i => i.Payments.Sum(p => p.Amount)) > 0,
        "FullyPaid" => milestone.Amount.HasValue && committedAmount >= milestone.Amount.Value && linkedInvoices.All(i => i.Status == "Paid"),
        _ => false
    };
}
```

Replace:

```csharp
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
```

with:

```csharp
private async Task<string?> ResolveNextGateAsync(Milestone milestone, List<Invoice> linkedInvoices, decimal committedAmount)
{
    await _context.Entry(milestone).Collection(m => m.PaymentRequirements).LoadAsync();
    foreach (var rule in milestone.PaymentRequirements)
    {
        if (!IsRequirementSatisfied(rule.RequiredState, milestone, linkedInvoices, committedAmount))
            return $"Requires the linked invoice(s) to be {DescribeState(rule.RequiredState)} before it can be marked {rule.TargetStatus}.";
    }
    return null;
}
```

- [ ] **Step 3: Update `UpdateStatusAsync` and `GetPaymentStatusAsync` call sites**

In `UpdateStatusAsync`, replace:

```csharp
await _context.Entry(milestone).Collection(m => m.PaymentRequirements).LoadAsync();
var applicableRules = milestone.PaymentRequirements.Where(r => r.TargetStatus == status).ToList();
if (applicableRules.Count > 0)
{
    var linkedInvoice = await FindLinkedInvoiceAsync(milestoneId);
    var unmet = applicableRules.FirstOrDefault(r => !IsRequirementSatisfied(r.RequiredState, linkedInvoice));
    if (unmet != null)
        throw new ValidationException($"Requires the linked invoice to be {DescribeState(unmet.RequiredState)} before it can be marked {status}.");
}
```

with:

```csharp
await _context.Entry(milestone).Collection(m => m.PaymentRequirements).LoadAsync();
var applicableRules = milestone.PaymentRequirements.Where(r => r.TargetStatus == status).ToList();
if (applicableRules.Count > 0)
{
    var linkedInvoices = await FindLinkedInvoicesAsync(milestoneId);
    var committedAmount = await GetCommittedAmountAsync(jobId, milestoneId);
    var unmet = applicableRules.FirstOrDefault(r => !IsRequirementSatisfied(r.RequiredState, milestone, linkedInvoices, committedAmount));
    if (unmet != null)
        throw new ValidationException($"Requires the linked invoice(s) to be {DescribeState(unmet.RequiredState)} before it can be marked {status}.");
}
```

Replace the whole `GetPaymentStatusAsync` method:

```csharp
public async Task<MilestonePaymentStatus> GetPaymentStatusAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId)
{
    await FindJobAsync(workspaceId, jobId);
    await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");
    var milestone = await FindMilestoneAsync(jobId, milestoneId);

    var linkedInvoices = await FindLinkedInvoicesAsync(milestoneId);
    var committedAmount = await GetCommittedAmountAsync(jobId, milestoneId);
    var nextGate = await ResolveNextGateAsync(milestone, linkedInvoices, committedAmount);

    return new MilestonePaymentStatus(
        milestone.Amount,
        committedAmount,
        milestone.Amount.HasValue ? milestone.Amount.Value - committedAmount : null,
        linkedInvoices.Select(i => new LinkedInvoiceSummary(i.Id, i.Number, i.Status)).ToList(),
        nextGate);
}
```

- [ ] **Step 4: Update the `MilestonePaymentStatus` record**

Replace:

```csharp
public record MilestonePaymentStatus(decimal? Amount, Guid? LinkedInvoiceId, string? LinkedInvoiceNumber, string? InvoiceStatus, string? NextGate);
```

with:

```csharp
public record LinkedInvoiceSummary(Guid InvoiceId, string Number, string Status);

public record MilestonePaymentStatus(decimal? Amount, decimal CommittedAmount, decimal? RemainingAmount, List<LinkedInvoiceSummary> LinkedInvoices, string? NextGate);
```

- [ ] **Step 5: Update `MilestonePaymentStatusResponse` DTO**

In `api/src/SurveyorLedger.API/Models/Milestone/MilestonePaymentRequirementDtos.cs`, replace:

```csharp
public class MilestonePaymentStatusResponse
{
    public decimal? Amount { get; set; }
    public Guid? LinkedInvoiceId { get; set; }
    public string? LinkedInvoiceNumber { get; set; }
    public string? InvoiceStatus { get; set; }
    public string? NextGate { get; set; }
}
```

with:

```csharp
public class LinkedInvoiceSummaryDto
{
    public Guid InvoiceId { get; set; }
    public required string Number { get; set; }
    public required string Status { get; set; }
}

public class MilestonePaymentStatusResponse
{
    public decimal? Amount { get; set; }
    public decimal CommittedAmount { get; set; }
    public decimal? RemainingAmount { get; set; }
    public List<LinkedInvoiceSummaryDto> LinkedInvoices { get; set; } = new();
    public string? NextGate { get; set; }
}
```

- [ ] **Step 6: Update the controller's `GetPaymentStatus` mapping**

In `api/src/SurveyorLedger.API/Controllers/MilestoneController.cs`, replace:

```csharp
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

with:

```csharp
[HttpGet("{id}/payment-status")]
public async Task<ActionResult<ApiResponse<MilestonePaymentStatusResponse>>> GetPaymentStatus(Guid workspaceId, Guid jobId, Guid id)
{
    var status = await _milestoneService.GetPaymentStatusAsync(workspaceId, CallerId(), jobId, id);
    return Ok(ApiResponse<MilestonePaymentStatusResponse>.Ok(new MilestonePaymentStatusResponse
    {
        Amount = status.Amount,
        CommittedAmount = status.CommittedAmount,
        RemainingAmount = status.RemainingAmount,
        LinkedInvoices = status.LinkedInvoices.Select(i => new LinkedInvoiceSummaryDto { InvoiceId = i.InvoiceId, Number = i.Number, Status = i.Status }).ToList(),
        NextGate = status.NextGate
    }));
}
```

- [ ] **Step 7: Build**

Run: `cd api && dotnet build src/SurveyorLedger.API`
Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add api/src/SurveyorLedger.API/Services/MilestoneService.cs api/src/SurveyorLedger.API/Models/Milestone/MilestonePaymentRequirementDtos.cs api/src/SurveyorLedger.API/Controllers/MilestoneController.cs
git commit -m "feat: adapt milestone payment gating to multiple linked invoices

FindLinkedInvoiceAsync (singular, assumed exactly one invoice per
milestone) becomes FindLinkedInvoicesAsync (plural). Gate satisfaction
is now aggregate: Invoiced if any linked invoice is Sent+, PartiallyPaid
if any payment exists across them, FullyPaid requires the milestone
fully committed AND every linked invoice Paid. MilestonePaymentStatus
gains CommittedAmount/RemainingAmount and a list of linked invoices
instead of a single one.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 6: Milestone response fields, Expense wiring, profitability endpoint

**Files:**
- Modify: `api/src/SurveyorLedger.API/Models/Milestone/MilestoneResponse.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/MilestoneController.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Expense/ExpenseDtos.cs`
- Modify: `api/src/SurveyorLedger.API/Services/ExpenseService.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/ExpenseController.cs`

**Interfaces:**
- Consumes: `IMilestoneService.GetCommittedAmountAsync`, `IMilestoneService.ComputeProfitabilityAsync` (Task 2).
- Produces: `MilestoneResponse.CommittedAmount`/`RemainingAmount`; `ExpenseRequest`/`ExpenseResponse.MilestoneId`; new `GET .../milestone/{id}/profitability`.

- [ ] **Step 1: Add `CommittedAmount`/`RemainingAmount` to `MilestoneResponse`**

In `api/src/SurveyorLedger.API/Models/Milestone/MilestoneResponse.cs`, add:

```csharp
public DateTime UpdatedAt { get; set; }
public decimal CommittedAmount { get; set; }
public decimal? RemainingAmount { get; set; }
```

- [ ] **Step 2: Turn `MilestoneController.ToResponse` into an async instance method and update all four call sites**

In `MilestoneController.cs`, inject `IMilestoneService` is already present (constructor already takes it). Replace the `private static MilestoneResponse ToResponse(Milestone m)` method with:

```csharp
private async Task<MilestoneResponse> ToResponseAsync(Milestone m)
{
    var committed = await _milestoneService.GetCommittedAmountAsync(m.JobId, m.Id);
    return new MilestoneResponse
    {
        MilestoneId = m.Id,
        JobId = m.JobId,
        Title = m.Title,
        Description = m.Description,
        DueDate = m.DueDate,
        Amount = m.Amount,
        Status = m.Status,
        SortOrder = m.SortOrder,
        CompletedAt = m.CompletedAt,
        CompletedBy = m.CompletedBy,
        CreatedBy = m.CreatedBy,
        CreatedAt = m.CreatedAt,
        UpdatedAt = m.UpdatedAt,
        CommittedAmount = committed,
        RemainingAmount = m.Amount.HasValue ? m.Amount.Value - committed : null
    };
}
```

Replace each of the four call sites. `List`:

```csharp
[HttpGet]
public async Task<ActionResult<ApiResponse<List<MilestoneResponse>>>> List(Guid workspaceId, Guid jobId)
{
    var milestones = await _milestoneService.GetMilestonesAsync(workspaceId, CallerId(), jobId);
    var responses = new List<MilestoneResponse>();
    foreach (var m in milestones)
        responses.Add(await ToResponseAsync(m));
    return Ok(ApiResponse<List<MilestoneResponse>>.Ok(responses));
}
```

`GetById`, `Create`, `Update`, `UpdateStatus` each change `ToResponse(milestone)` to `await ToResponseAsync(milestone)` (the surrounding method is already `async Task<...>`, so `await` is valid there).

`Reorder`:

```csharp
[HttpPut("reorder")]
public async Task<ActionResult<ApiResponse<List<MilestoneResponse>>>> Reorder(Guid workspaceId, Guid jobId, [FromBody] MilestoneReorderRequest request)
{
    var milestones = await _milestoneService.ReorderAsync(workspaceId, CallerId(), jobId, request.MilestoneIds);
    var responses = new List<MilestoneResponse>();
    foreach (var m in milestones)
        responses.Add(await ToResponseAsync(m));
    return Ok(ApiResponse<List<MilestoneResponse>>.Ok(responses));
}
```

Note: build these sequentially (`foreach` + `await`), never `Select(...).ToList()` with an async lambda or `Task.WhenAll` - the injected `ApplicationDbContext` is scoped per-request and not safe for concurrent operations.

- [ ] **Step 3: Add the profitability endpoint**

Add a new response DTO to `MilestonePaymentRequirementDtos.cs` (or a new file `MilestoneProfitabilityDtos.cs` - either is fine, keep it next to the other milestone DTOs):

```csharp
public class MilestoneProfitabilityResponse
{
    public decimal Revenue { get; set; }
    public decimal Expenses { get; set; }
    public decimal Profit { get; set; }
}
```

Add to `MilestoneController.cs`:

```csharp
[HttpGet("{id}/profitability")]
public async Task<ActionResult<ApiResponse<MilestoneProfitabilityResponse>>> GetProfitability(Guid workspaceId, Guid jobId, Guid id)
{
    var (revenue, expenses, profit) = await _milestoneService.ComputeProfitabilityAsync(workspaceId, CallerId(), jobId, id);
    return Ok(ApiResponse<MilestoneProfitabilityResponse>.Ok(new MilestoneProfitabilityResponse { Revenue = revenue, Expenses = expenses, Profit = profit }));
}
```

- [ ] **Step 4: Add `MilestoneId` to `ExpenseRequest`/`ExpenseResponse`**

In `api/src/SurveyorLedger.API/Models/Expense/ExpenseDtos.cs`:

```csharp
public class ExpenseRequest
{
    public string Category { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime IncurredDate { get; set; }
    public Guid? PayeeId { get; set; }
    public string? PayeeType { get; set; }
    public Guid? MilestoneId { get; set; }
}
```

```csharp
public class ExpenseResponse
{
    public Guid ExpenseId { get; set; }
    public Guid JobId { get; set; }
    public string Category { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime IncurredDate { get; set; }
    public bool HasReceipt { get; set; }
    public Guid? PayeeId { get; set; }
    public string? PayeeName { get; set; }
    public string? PayeeType { get; set; }
    public Guid? MilestoneId { get; set; }
    public string RecordedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 5: Wire `MilestoneId` through `ExpenseService`**

In `api/src/SurveyorLedger.API/Services/ExpenseService.cs`, add a validation helper and call it from `CreateAsync`/`UpdateAsync`:

```csharp
private async Task ValidateMilestoneAsync(Guid jobId, Guid? milestoneId)
{
    if (milestoneId == null)
        return;
    var exists = await _context.Milestones.AnyAsync(m => m.Id == milestoneId && m.JobId == jobId && m.IsActive);
    if (!exists)
        throw new ValidationException("MilestoneId must reference an active milestone on this same job.");
}
```

In `CreateAsync`, after `await ValidateAndNormalizePayeeAsync(request);` add `await ValidateMilestoneAsync(jobId, request.MilestoneId);`, and in the `new Expense { ... }` initializer add `MilestoneId = request.MilestoneId,` after `PayeeType = request.PayeeType,`.

In `UpdateAsync`, after `await ValidateAndNormalizePayeeAsync(request);` add `await ValidateMilestoneAsync(jobId, request.MilestoneId);`, and after `expense.PayeeType = request.PayeeType;` add `expense.MilestoneId = request.MilestoneId;`.

- [ ] **Step 6: Wire `MilestoneId` through `ExpenseController`'s response mapping**

Find the `ToResponse` (or equivalent) mapping in `api/src/SurveyorLedger.API/Controllers/ExpenseController.cs` and add `MilestoneId = e.MilestoneId,` to the `ExpenseResponse` initializer, matching the existing field-by-field style.

- [ ] **Step 7: Build**

Run: `cd api && dotnet build src/SurveyorLedger.API`
Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add api/src/SurveyorLedger.API/Models/Milestone api/src/SurveyorLedger.API/Controllers/MilestoneController.cs api/src/SurveyorLedger.API/Models/Expense/ExpenseDtos.cs api/src/SurveyorLedger.API/Services/ExpenseService.cs api/src/SurveyorLedger.API/Controllers/ExpenseController.cs
git commit -m "feat: surface milestone committed/remaining amount, tag expenses to milestones, add profitability endpoint

MilestoneResponse gains CommittedAmount/RemainingAmount. Expense gains
an optional MilestoneId tag, validated against the same job. New
GET .../milestone/{id}/profitability returns Revenue (invoiced amount)
minus Expenses (milestone-tagged) = Profit.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 7: Fix tests broken by the ceiling and gating changes

**Files:**
- Modify: `api/tests/SurveyorLedger.API.Tests/Services/MilestoneBillingLinkTests.cs`
- Modify: `api/tests/SurveyorLedger.API.Tests/Services/MilestonePaymentGatingTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 2-5.

- [ ] **Step 1: Build the test project to see what's actually broken**

Run: `cd api && dotnet build`
Expected: compile errors in `MilestoneBillingLinkTests.cs` (uses `status.LinkedInvoiceId`/`status.InvoiceStatus`, which no longer exist) and possibly `MilestonePaymentGatingTests.cs` (same). Read the actual error list before editing - don't guess at line numbers, the file may have shifted since this plan was written.

- [ ] **Step 2: Rewrite `MilestoneBillingLinkTests.cs`'s two now-wrong tests**

`SecondInvoice_CannotClaimSameMilestone_WhileFirstIsActive` still throws (a second 25000 direct-invoice line against a 25000-fee milestone exceeds the ceiling: 25000 + 25000 > 25000), but the reason changed from exclusivity to the fee ceiling - rename and re-comment it:

```csharp
[Fact]
public async Task SecondInvoice_ExceedingMilestoneFee_IsRejected()
{
    await SeedAsync();
    await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceRequestFor(_milestoneId));

    // Milestone fee is 25000, already fully committed by the first invoice - a second
    // direct-invoice line for the same milestone would push the total to 50000.
    await Assert.ThrowsAsync<ValidationException>(
        () => _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceRequestFor(_milestoneId)));
}
```

`Quotation_And_Invoice_CanEachHoldTheirOwnActiveLink_Simultaneously` is now wrong by design - the whole point of the fee ceiling is that both routes share it. Replace it:

```csharp
[Fact]
public async Task Quotation_And_DirectInvoice_ShareTheSameFeeCeiling()
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

    // The milestone's 25000 fee is already fully committed by the quotation line above -
    // a direct invoice for the same milestone must be rejected, not allowed alongside it.
    await Assert.ThrowsAsync<ValidationException>(
        () => _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceRequestFor(_milestoneId)));
}
```

Add `services.AddScoped<IMilestoneService, MilestoneService>();` to this file's `ConfigureServices` if not already present (it already constructs `_milestoneService` via `GetService<IMilestoneService>()` in `SeedAsync`, so it should already be registered - verify, don't duplicate if it's already there).

- [ ] **Step 3: Rewrite `MilestonePaymentGatingTests.cs`'s `GetPaymentStatus_ReflectsLinkedInvoice`**

Replace:

```csharp
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
```

with:

```csharp
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
    Assert.Single(status.LinkedInvoices);
    Assert.Equal(invoice.Id, status.LinkedInvoices[0].InvoiceId);
    Assert.Equal("Draft", status.LinkedInvoices[0].Status);
    Assert.Equal(25000m, status.CommittedAmount);
    Assert.Equal(0m, status.RemainingAmount);
}
```

The other three tests in this file (`NoRequirements_TransitionsFreely_EvenWithUnpaidLinkedInvoice`, `FeelessMilestone_NeverGated`, `FullyPaidRequirement_BlocksUntilInvoicePaid_ThenSucceeds`) should keep passing unchanged - their assertions don't touch the removed fields. Leave them as-is.

- [ ] **Step 4: Build and run both files**

Run: `cd api && dotnet build`
Expected: 0 errors.

Run: `cd api && dotnet test --filter "MilestoneBillingLinkTests|MilestonePaymentGatingTests"`
Expected: PASS, all tests in both files.

- [ ] **Step 5: Commit**

```bash
git add api/tests/SurveyorLedger.API.Tests/Services/MilestoneBillingLinkTests.cs api/tests/SurveyorLedger.API.Tests/Services/MilestonePaymentGatingTests.cs
git commit -m "test: fix milestone tests for the fee-ceiling and multi-invoice gating changes

Quotation_And_Invoice_CanEachHoldTheirOwnActiveLink_Simultaneously
tested behavior the fee ceiling now deliberately forbids - replaced
with a test proving the two routes share the same ceiling.
GetPaymentStatus_ReflectsLinkedInvoice updated for the
LinkedInvoiceId/InvoiceStatus -> LinkedInvoices list DTO change.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 8: New coverage — ceiling combinations, auto-copy, profitability, multi-invoice gating

**Files:**
- Create: `api/tests/SurveyorLedger.API.Tests/Services/MilestoneFeeCeilingTests.cs`
- Create: `api/tests/SurveyorLedger.API.Tests/Services/MilestoneProfitabilityTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 2-6.

- [ ] **Step 1: Write `MilestoneFeeCeilingTests.cs`**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.Milestone;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class MilestoneFeeCeilingTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IMilestoneService _milestoneService = null!;
    private IQuotationService _quotationService = null!;
    private IInvoiceService _invoiceService = null!;
    private Guid _jobId;
    private Guid _clientPersonId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IMilestoneService, MilestoneService>();
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
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-milestone-ceiling-test-{Guid.NewGuid():N}"),
                    ["AppSettings:UiBaseUrl"] = "https://test.local"
                })
                .Build());
    }

    private async Task<Milestone> SeedMilestoneAsync(decimal? amount)
    {
        _jobService = GetService<IJobService>();
        _milestoneService = GetService<IMilestoneService>();
        _quotationService = GetService<IQuotationService>();
        _invoiceService = GetService<IInvoiceService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        _jobId = job.Id;
        _clientPersonId = await GrantClientBillingRoleAsync(_jobId);
        return await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobId, new MilestoneRequest { Title = "Land Survey", Amount = amount });
    }

    private InvoiceRequest DirectInvoiceFor(Guid milestoneId, decimal amount) => new()
    {
        ClientId = _clientPersonId, JobId = _jobId,
        LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = amount, MilestoneId = milestoneId } },
        TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
    };

    [Fact]
    public async Task QuotationLine_PlusDirectInvoice_UnderTheFee_BothSucceed()
    {
        var milestone = await SeedMilestoneAsync(80000m);
        await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 30000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0
        });

        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, DirectInvoiceFor(milestone.Id, 40000m));
        Assert.Equal(milestone.Id, invoice.LineItems.Single().MilestoneId);
    }

    [Fact]
    public async Task QuotationLine_PlusDirectInvoice_OverTheFee_InvoiceRejected()
    {
        var milestone = await SeedMilestoneAsync(80000m);
        await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 50000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0
        });

        await Assert.ThrowsAsync<ValidationException>(
            () => _invoiceService.CreateAsync(WorkspaceId, AdminId, DirectInvoiceFor(milestone.Id, 40000m)));
    }

    [Fact]
    public async Task DirectInvoice_ThenQuotationLine_OverTheFee_QuotationRejected()
    {
        var milestone = await SeedMilestoneAsync(80000m);
        await _invoiceService.CreateAsync(WorkspaceId, AdminId, DirectInvoiceFor(milestone.Id, 50000m));

        var request = new QuotationRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 40000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0
        };

        await Assert.ThrowsAsync<ValidationException>(() => _quotationService.CreateAsync(WorkspaceId, AdminId, request));
    }

    [Fact]
    public async Task QuotationDrawnInvoiceLine_DoesNotDoubleCount()
    {
        var milestone = await SeedMilestoneAsync(80000m);
        var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 80000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0
        });
        var quotationLineId = quotation.LineItems[0].Id;

        // Drawing the full 80000 from the quotation line should succeed - it's already
        // counted via the quotation line, not double-charged against the ceiling.
        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 80000m, QuotationLineId = quotationLineId } },
            TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
        });

        // MilestoneId auto-copied from the quotation line onto the invoice line.
        Assert.Equal(milestone.Id, invoice.LineItems.Single().MilestoneId);
    }

    [Fact]
    public async Task MilestoneWithNoFee_AllowsUnlimitedLines()
    {
        var milestone = await SeedMilestoneAsync(null);
        await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 999999m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0
        });

        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, DirectInvoiceFor(milestone.Id, 999999m));
        Assert.Equal(milestone.Id, invoice.LineItems.Single().MilestoneId);
    }

    [Fact]
    public async Task ConflictingExplicitMilestoneId_OnQuotationDrawnLine_IsRejected()
    {
        var milestone = await SeedMilestoneAsync(80000m);
        var otherMilestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobId, new MilestoneRequest { Title = "Plan Preparation", Amount = 20000m });
        var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 80000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0
        });
        var quotationLineId = quotation.LineItems[0].Id;

        var request = new InvoiceRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 80000m, QuotationLineId = quotationLineId, MilestoneId = otherMilestone.Id } },
            TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
        };

        await Assert.ThrowsAsync<ValidationException>(() => _invoiceService.CreateAsync(WorkspaceId, AdminId, request));
    }
}
```

- [ ] **Step 2: Write `MilestoneProfitabilityTests.cs`**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Expense;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.Milestone;
using SurveyorLedger.API.Services;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class MilestoneProfitabilityTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IMilestoneService _milestoneService = null!;
    private IInvoiceService _invoiceService = null!;
    private IExpenseService _expenseService = null!;
    private Guid _jobId;
    private Guid _clientPersonId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IMilestoneService, MilestoneService>();
        services.AddScoped<IQuotationService, QuotationService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IPdfService, PdfService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-milestone-profit-test-{Guid.NewGuid():N}"),
                    ["AppSettings:UiBaseUrl"] = "https://test.local"
                })
                .Build());
    }

    [Fact]
    public async Task ComputeProfitabilityAsync_RevenueMinusExpenses()
    {
        _jobService = GetService<IJobService>();
        _milestoneService = GetService<IMilestoneService>();
        _invoiceService = GetService<IInvoiceService>();
        _expenseService = GetService<IExpenseService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        _jobId = job.Id;
        _clientPersonId = await GrantClientBillingRoleAsync(_jobId);
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobId, new MilestoneRequest { Title = "Land Survey", Amount = 50000m });

        await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 50000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
        });
        await _expenseService.CreateAsync(WorkspaceId, AdminId, _jobId, new ExpenseRequest
        {
            Category = "Transport", Amount = 15000m, IncurredDate = DateTime.UtcNow, MilestoneId = milestone.Id
        });
        // Untagged expense - must not count against this milestone's profitability.
        await _expenseService.CreateAsync(WorkspaceId, AdminId, _jobId, new ExpenseRequest
        {
            Category = "Other", Amount = 999m, IncurredDate = DateTime.UtcNow
        });

        var (revenue, expenses, profit) = await _milestoneService.ComputeProfitabilityAsync(WorkspaceId, AdminId, _jobId, milestone.Id);
        Assert.Equal(50000m, revenue);
        Assert.Equal(15000m, expenses);
        Assert.Equal(35000m, profit);
    }
}
```

- [ ] **Step 3: Run both new files**

Run: `cd api && dotnet test --filter "MilestoneFeeCeilingTests|MilestoneProfitabilityTests"`
Expected: PASS, all tests.

- [ ] **Step 4: Commit**

```bash
git add api/tests/SurveyorLedger.API.Tests/Services/MilestoneFeeCeilingTests.cs api/tests/SurveyorLedger.API.Tests/Services/MilestoneProfitabilityTests.cs
git commit -m "test: cover the milestone fee ceiling across both billing routes and profitability

Quotation-then-invoice and invoice-then-quotation ordering, no
double-counting for a quotation-drawn invoice line, unlimited lines on
a fee-less milestone, the MilestoneId auto-copy plus its conflict
rejection, and ComputeProfitabilityAsync with a tagged vs untagged
expense.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 9: Full suite verification

**Files:** none (verification only)

- [ ] **Step 1: Run the full backend test suite**

Run: `cd api && dotnet test`
Expected: all tests pass, 0 failures.

- [ ] **Step 2: Confirm no leftover references to the removed singular gating API**

Run: `cd api && grep -rn "FindLinkedInvoiceAsync\b\|LinkedInvoiceId\|LinkedInvoiceNumber\|InvoiceStatus =" src/SurveyorLedger.API/Services/MilestoneService.cs src/SurveyorLedger.API/Models/Milestone src/SurveyorLedger.API/Controllers/MilestoneController.cs`
Expected: no matches (the plural `FindLinkedInvoicesAsync` and `LinkedInvoiceSummary`/`LinkedInvoiceSummaryDto` are fine and won't match these patterns - if something does match, it's leftover from before Task 5's rewrite).

- [ ] **Step 3: No commit for this task** - verification only. If Step 1 or 2 surfaces a problem, fix it in the relevant earlier task's files with a small follow-up commit.
