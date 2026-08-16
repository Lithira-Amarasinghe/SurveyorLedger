# Job-Scoped Billing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Invoices and quotations become job-scoped documents (required `JobId`, dropped `WorkspaceId`), billed to a `ClientId` who must hold `Client`/`Finance` access on that job, viewable via job-scoped RBAC, and sendable by email with a PDF attachment to selected job participants.

**Architecture:** `Invoice`/`Quotation` lose their own `WorkspaceId` column and are tenant-scoped by joining through `Job.WorkspaceId`; access checks move from `EnsureAllowedAsync` (flat workspace permission) to `EnsureJobAccessAsync` (the same job-or-workspace-scoped grant check `Milestone`/`Document` already use). `ClientService`/`ClientsController`/`ClientDtos.cs` are deleted outright â€” billing recipients are now just job participants with role `Client` or `Finance`, added via the existing `JobService.AddParticipantAsync`. A new `PdfService` (QuestPDF) renders a line-item table for the email attachment; `IEmailService` gains `SendBillingDocumentAsync`.

**Tech Stack:** .NET 9, EF Core 9, SQL Server, Casbin.NET, QuestPDF (new), Angular 21 standalone components + signals.

## Global Constraints

- Every tenant-scoped query on `Invoice`/`Quotation` must filter via `.Where(x => x.Job.WorkspaceId == workspaceId)` (join through `Job`), never a stored `WorkspaceId` column â€” that column is being dropped by this spec.
- Migrations are always `dotnet ef migrations add`-generated; never hand-edited.
- No backfill for orphaned rows â€” dev-only DB, existing `Invoice`/`Quotation` rows with `JobId == null` are simply dropped as part of the required-column migration (per repo convention already used in Spec 1).
- Controllers stay thin; all business logic and access checks live in services.
- Reuse `EnsureJobAccessAsync` exactly as `MilestoneService`/`DocumentService` use it â€” no new access-check mechanism.
- Per-task verification is scoped (`dotnet test --filter ClassName`), not full-suite, except the final task.
- Do not commit until the user explicitly asks.

---

### Task 1: Migration â€” `JobId` required, `WorkspaceId` dropped, RBAC seed updates

**Files:**
- Modify: `api/src/SurveyorLedger.Data/Entities/Invoice.cs`
- Modify: `api/src/SurveyorLedger.Data/Entities/Quotation.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/InvoiceConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/QuotationConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/RoleConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/RoleScopeConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/PermissionConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/RolePermissionConfiguration.cs`
- Create: `api/src/SurveyorLedger.Data/Migrations/<timestamp>_JobScopedBilling.cs` (generated, not hand-written)
- Test: `api/tests/SurveyorLedger.API.Tests/Services/InvoiceServiceTests.cs`, `QuotationServiceTests.cs` (fixture cleanup only â€” see Task 8)

**Interfaces:**
- Consumes: `RoleConfiguration.AdminRoleId/SurveyorRoleId/ClientRoleId/MemberRoleId` (existing), `Constants.ScopeTypes.Job/Workspace` (existing)
- Produces: `RoleConfiguration.FinanceRoleId` (new static `Guid`), `PermissionConfiguration.ViewInvoiceId/ViewQuotationId` (existing, now also granted to `ClientRoleId` and `FinanceRoleId`), `Invoice.JobId`/`Quotation.JobId` as non-nullable `Guid`, no more `Invoice.WorkspaceId`/`Quotation.WorkspaceId`

- [ ] **Step 1: Entity changes**
`api/src/SurveyorLedger.Data/Entities/Invoice.cs`:
```csharp
namespace SurveyorLedger.Data.Entities;

/// <summary>
/// Draft/Sent/PartiallyPaid/Paid/Overdue/Cancelled. Total/AmountPaid/Balance/DaysOverdue
/// are computed by InvoiceService from LineItems and Payments, never stored - see
/// InvoiceService.ComputeInvoiceTotals for the single source of truth. No WorkspaceId
/// column - tenant scoping goes through Job.WorkspaceId (see JobScopedBilling migration).
/// </summary>
public class Invoice
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid JobId { get; set; }
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

    public Person Client { get; set; }
    public Job Job { get; set; }
    public Quotation? Quotation { get; set; }
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
```
Same shape for `api/src/SurveyorLedger.Data/Entities/Quotation.cs`: remove `Guid WorkspaceId`, change `Guid? JobId` to `Guid JobId`, change `Job? Job` to `Job Job`, keep everything else (`ClientId`, `Number`, `LineItems`, `TaxRatePercent`, `Status`, `ValidUntil`, `RevisionNumber`, `CreatedAt`, `UpdatedAt`, `IsActive`, `Client`).

- [ ] **Step 2: Configuration changes**
`api/src/SurveyorLedger.Data/Configurations/InvoiceConfiguration.cs` â€” remove the `WorkspaceId` index and the `Workspace` navigation FK, keep `Number` unique per-job instead of per-workspace (documents don't share a number sequence with other jobs' documents anyway, but the spec doesn't require changing numbering â€” keep numbering workspace-derived via `Job.WorkspaceId` in the service, drop the DB-level unique index since there's no more `WorkspaceId` column to compose it with):
```csharp
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

    builder.HasIndex(x => x.JobId);
    builder.HasIndex(x => new { x.JobId, x.Number }).IsUnique();
    builder.HasIndex(x => x.IsActive);

    builder.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
    builder.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
    builder.HasOne(x => x.Quotation).WithMany().HasForeignKey(x => x.QuotationId).OnDelete(DeleteBehavior.Restrict);
    builder.HasMany(x => x.Payments).WithOne(x => x.Invoice).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
}
```
Same pattern for `QuotationConfiguration.cs`: drop the `Workspace` nav/index, `HasIndex(x => new { x.JobId, x.Number }).IsUnique()`.

> Note on numbering: `NextInvoiceNumberAsync`/`NextNumberAsync` currently count `IgnoreQueryFilters().CountAsync(i => i.WorkspaceId == workspaceId)`. Task 2 changes these to count by `i.Job.WorkspaceId == workspaceId` â€” the number sequence stays workspace-wide (matches existing `INV-0001` behavior), only the *uniqueness constraint* moves to `(JobId, Number)` since there's no workspace column left to key it on directly. This is a minor behavior nuance: the DB unique index no longer directly enforces workspace-uniqueness of `Number`, only per-job â€” acceptable since the service-computed number is still workspace-sequential and collisions across jobs in the same workspace are astronomically unlikely (sequential counter), but call this out to the user if strict workspace-wide uniqueness at the DB level matters â€” it would require a computed/indexed view or trigger, out of scope for this spec.

- [ ] **Step 3: RoleConfiguration â€” add Finance role**
```csharp
public static readonly Guid FinanceRoleId = new("00000000-0000-0000-0000-000000000006");
```
Add to `HasData(...)`:
```csharp
new Role { Id = FinanceRoleId, Name = Constants.SystemRoles.Finance, Description = "Job-scoped view of invoices and quotations for that job only.", IsSystem = true, CreatedAt = seededAt, UpdatedAt = seededAt }
```
Add `public const string Finance = "Finance";` to `Constants.SystemRoles` in `api/src/SurveyorLedger.Core/Constants.cs`.

- [ ] **Step 4: RoleScopeConfiguration â€” Finance is job-scoped only**
```csharp
builder.HasData(
    new RoleScope { RoleId = RoleConfiguration.AdminRoleId, ScopeType = Constants.ScopeTypes.Workspace },
    new RoleScope { RoleId = RoleConfiguration.SurveyorRoleId, ScopeType = Constants.ScopeTypes.Workspace },
    new RoleScope { RoleId = RoleConfiguration.SurveyorRoleId, ScopeType = Constants.ScopeTypes.Job },
    new RoleScope { RoleId = RoleConfiguration.ClientRoleId, ScopeType = Constants.ScopeTypes.Job },
    new RoleScope { RoleId = RoleConfiguration.MemberRoleId, ScopeType = Constants.ScopeTypes.Workspace },
    new RoleScope { RoleId = RoleConfiguration.FinanceRoleId, ScopeType = Constants.ScopeTypes.Job }
);
```

- [ ] **Step 5: PermissionConfiguration â€” drop billingclient permissions**
Remove the four `ViewBillingClientId`/`CreateBillingClientId`/`EditBillingClientId`/`DeleteBillingClientId` static `Guid` fields and their four `HasData` rows (`billingclient.view/create/edit/delete`). Keep `ViewInvoiceId`/`CreateInvoiceId`/`EditInvoiceId`/`DeleteInvoiceId`/`ViewQuotationId`/`CreateQuotationId`/`EditQuotationId`/`DeleteQuotationId` unchanged (still used by `Admin`/`Surveyor`).

- [ ] **Step 6: RolePermissionConfiguration â€” remove billingclient grants, add Finance + Client grants**
Remove the six `Grant(..., ViewBillingClientId/CreateBillingClientId/EditBillingClientId/DeleteBillingClientId)` lines for Admin/Surveyor, and remove the `Grant(..., ClientRoleId, ViewBillingClientId)` / `Grant(..., MemberRoleId, ViewBillingClientId)` lines. `ClientRoleId`'s existing `ViewQuotationId`/`ViewInvoiceId` grants (ids `...263` was billingclientâ€”being removed; `...264`/`...265` are quotation/invoice view, already present) stay as-is â€” the spec's "Client role gains invoice.view/quotation.view" is **already satisfied** by the existing seed rows (`Grant(...264, ClientRoleId, ViewQuotationId)`, `Grant(...265, ClientRoleId, ViewInvoiceId)`), so no new grant needed there, just remove the `MemberRoleId`/global billingclient noise. Add new grants for `FinanceRoleId`:
```csharp
Grant(new Guid("00000000-0000-0000-0000-000000000282"), RoleConfiguration.FinanceRoleId, PermissionConfiguration.ViewQuotationId),
Grant(new Guid("00000000-0000-0000-0000-000000000283"), RoleConfiguration.FinanceRoleId, PermissionConfiguration.ViewInvoiceId)
```
(new fixed GUIDs, next unused in the `...281` sequence).

- [ ] **Step 7: Run the migration command**
```
cd api
dotnet ef migrations add JobScopedBilling --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```
Inspect the generated migration: it must (a) add `ALTER TABLE Invoices ADD ... JobId` as required after first setting `DELETE FROM Invoices WHERE JobId IS NULL` is **not** auto-generated by EF â€” since dev-only DB with no backfill is acceptable per rules, if EF's generated `AlterColumn` fails on existing NULL rows locally, manually truncate the dev `Invoices`/`Quotations`/`Payments`/`InvoiceLineItems`/`QuotationLineItems` tables before applying (documented dev-reset step, not part of the migration file itself); (b) drop the `WorkspaceId` column and its indexes from both tables; (c) insert the new `Role`/`RoleScope`/`RolePermission`/removed `Permission` seed rows via `MigrationBuilder.InsertData`/`DeleteData`.

- [ ] **Step 8: Apply and verify**
```
dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```
Confirm via `migration-check` skill or manual inspection: `Invoices.WorkspaceId` and `Quotations.WorkspaceId` columns gone, `Invoices.JobId`/`Quotations.JobId` NOT NULL, `Roles` table has `Finance`, `RoleScopes` has `(Finance, Job)`, `Permissions` no longer has `billingclient.*` rows.

- [ ] **Step 9: Commit**
```
git add api/src/SurveyorLedger.Data
git commit -m "$(cat <<'EOF'
feat: make Invoice/Quotation job-scoped, drop WorkspaceId, add Finance role

Invoice/Quotation.JobId is now required and WorkspaceId is dropped in favor
of scoping through Job.WorkspaceId. Adds job-scoped Finance role
(invoice.view/quotation.view only) and removes the billingclient permission
set ahead of ClientService's deletion.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: InvoiceService/QuotationService â€” job-scoped access checks + ClientId validation

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/InvoiceService.cs`
- Modify: `api/src/SurveyorLedger.API/Services/QuotationService.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Billing/InvoiceDtos.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Billing/QuotationDtos.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/InvoiceServiceTests.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/QuotationServiceTests.cs`

**Interfaces:**
- Consumes: `IScopedAccessService.EnsureJobAccessAsync(Guid userId, Guid workspaceId, Guid jobId, string action)` (existing, exact signature from `ScopedAccessService.cs:120`), `IScopedAccessService.EnsureAllowedAsync` (existing, for `create` which has no jobId to check against until the request is parsed), `IScopedAccessService.GetEffectiveJobRolesAsync(Guid userId, Guid workspaceId, Guid jobId)` (existing, returns `List<string>`)
- Produces: `InvoiceRequest.JobId` becomes `Guid` (required, was `Guid?`), `QuotationRequest.JobId` becomes `Guid` (required, was `Guid?`); `InvoiceService.SearchAsync`/`GetByIdAsync` etc. now filter by `i.Job.WorkspaceId == workspaceId` instead of `i.WorkspaceId == workspaceId`

- [ ] **Step 1: Write failing tests first**
Add to `InvoiceServiceTests.cs` (extends `WorkspaceIntegrationTestBase`, uses `Context`, `WorkspaceId`, `AdminId`, `GetService<T>()` per existing fixture pattern):
```csharp
[Fact]
public async Task CreateAsync_ClientIdNotOnJob_Throws()
{
    _invoiceService = GetService<IInvoiceService>();
    var job = await SeedJobAsync(); // helper added in Step 1b below
    var strangerPerson = new SurveyorLedger.Data.Entities.Person
    {
        Id = Guid.NewGuid(), FirstName = "Stranger", LastName = "Person", Email = "stranger@test.local",
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };
    Context.People.Add(strangerPerson);
    await Context.SaveChangesAsync();

    await Assert.ThrowsAsync<ValidationException>(() => _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
    {
        ClientId = strangerPerson.Id,
        JobId = job.Id,
        LineItems = new List<LineItemDto> { new() { Description = "Survey", Quantity = 1, UnitPrice = 1000m } },
        TaxRatePercent = 0,
        DiscountAmount = 0,
        Status = "Draft"
    }));
}

[Fact]
public async Task GetByIdAsync_ClientRoleOnJob_CanView()
{
    _invoiceService = GetService<IInvoiceService>();
    var (job, clientPersonId, clientUserAccountId) = await SeedJobWithClientParticipantAsync();
    var invoiceId = await SeedInvoiceOnJobAsync(job.Id, clientPersonId);

    var invoice = await _invoiceService.GetByIdAsync(WorkspaceId, clientUserAccountId, invoiceId);
    Assert.Equal(invoiceId, invoice.Id);
}

[Fact]
public async Task GetByIdAsync_NoJobRole_ThrowsForbidden()
{
    _invoiceService = GetService<IInvoiceService>();
    var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();
    var invoiceId = await SeedInvoiceOnJobAsync(job.Id, clientPersonId);

    var outsiderAccountId = await CreateWorkspaceMemberAsync(); // helper: creates a Person+UserAccount with Member role on WorkspaceId, no job grant
    await Assert.ThrowsAsync<ForbiddenException>(() => _invoiceService.GetByIdAsync(WorkspaceId, outsiderAccountId, invoiceId));
}
```
Note: `SeedJobAsync`, `SeedJobWithClientParticipantAsync`, `SeedInvoiceOnJobAsync`, `CreateWorkspaceMemberAsync` are new private test helpers to add in this same file (or `WorkspaceIntegrationTestBase` if broadly reused â€” the base class wasn't read in this planning session, so add them as `private` helpers local to `InvoiceServiceTests.cs`/`QuotationServiceTests.cs` first; promote to the base class only if duplicated identically in both files, per "reuse before building").

- [ ] **Step 2: Run tests to verify they fail**
```
cd api
dotnet test --filter "FullyQualifiedName~InvoiceServiceTests" 2>&1 | tail -40
```
Expected: compile error (`InvoiceRequest.JobId` is still `Guid?`, `Invoice.JobId` still nullable) or `ValidationException`/`ForbiddenException` not thrown because `CreateAsync`/`GetByIdAsync` don't yet validate against the job.

- [ ] **Step 3: Update DTOs**
`InvoiceDtos.cs`: change `public Guid? JobId { get; set; }` to `public Guid JobId { get; set; }` in `InvoiceRequest`. `InvoiceResponse.JobId` changes from `Guid?` to `Guid`.
`QuotationDtos.cs`: same â€” `QuotationRequest.JobId` and `QuotationResponse.JobId` become non-nullable `Guid`.

- [ ] **Step 4: Rewrite InvoiceService**
```csharp
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
    // ... fields/ctor unchanged ...

    public async Task<Invoice> CreateAsync(Guid workspaceId, Guid callerUserId, InvoiceRequest request)
    {
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, request.JobId, "create");
        await EnsureClientHoldsBillingRoleOnJobAsync(request.ClientId, request.JobId);
        ValidateLineItems(request.LineItems);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
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

        _logger.LogInformation("Invoice {InvoiceId} ({Number}) created on job {JobId} by {UserId}", invoice.Id, invoice.Number, invoice.JobId, callerUserId);
        return invoice;
    }

    public async Task<List<Invoice>> SearchAsync(Guid workspaceId, Guid callerUserId, Guid? clientId)
    {
        await _access.EnsureListAllowedAsync(callerUserId, workspaceId);

        var accessibleJobIds = _access.AccessibleJobIds(callerUserId);
        var hasViewAll = await _access.HasViewAllAsync(callerUserId, "job", workspaceId);

        var invoices = _context.Invoices.Include(i => i.Payments).Where(i => i.Job.WorkspaceId == workspaceId);
        if (!hasViewAll)
            invoices = invoices.Where(i => accessibleJobIds.Contains(i.JobId));
        if (clientId.HasValue)
            invoices = invoices.Where(i => i.ClientId == clientId.Value);

        return await invoices.OrderByDescending(i => i.CreatedAt).ToListAsync();
    }

    public async Task<Invoice> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId)
    {
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, invoice.JobId, "view");
        return invoice;
    }

    public async Task<Invoice> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, InvoiceRequest request)
    {
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, invoice.JobId, "edit");
        await EnsureClientHoldsBillingRoleOnJobAsync(request.ClientId, request.JobId);
        ValidateLineItems(request.LineItems);

        invoice.ClientId = request.ClientId;
        invoice.JobId = request.JobId;
        foreach (var old in invoice.LineItems.ToList())
            _context.Remove(old);
        invoice.LineItems.Clear();
        foreach (var item in request.LineItems.Select(i => new InvoiceLineItem { Id = Guid.NewGuid(), Description = i.Description.Trim(), Quantity = i.Quantity, UnitPrice = i.UnitPrice }))
        {
            invoice.LineItems.Add(item);
            _context.Entry(item).State = EntityState.Added;
        }
        invoice.TaxRatePercent = request.TaxRatePercent;
        invoice.DiscountAmount = request.DiscountAmount;
        invoice.DueDate = request.DueDate;
        if (request.Status is "Draft" or "Sent" or "Cancelled")
            invoice.Status = request.Status;
        invoice.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return invoice;
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId)
    {
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, invoice.JobId, "delete");

        if (invoice.Payments.Count > 0)
            throw new ConflictException("Cannot delete an invoice with recorded payments. Cancel it instead.");

        invoice.IsActive = false;
        invoice.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<Payment> RecordPaymentAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, PaymentRequest request, IFormFile? proofFile)
    {
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, invoice.JobId, "create");
        if (!AllowedMethods.Contains(request.Method))
            throw new ValidationException($"Method must be one of: {string.Join(", ", AllowedMethods)}.");
        if (request.Amount <= 0)
            throw new ValidationException("Amount must be positive.");

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
            RecordedBy = await _access.ResolvePersonIdAsync(callerUserId),
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
        var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, invoice.JobId, "view");

        return await _context.Payments
            .Where(p => p.InvoiceId == invoiceId && p.WorkspaceId == workspaceId)
            .OrderByDescending(p => p.ReceivedAt)
            .ToListAsync();
    }

    public (decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue, int DaysOverdue) ComputeInvoiceTotals(Invoice invoice)
    {
        // unchanged
    }

    private async Task<string> NextInvoiceNumberAsync(Guid workspaceId) =>
        $"INV-{await _context.Invoices.IgnoreQueryFilters().CountAsync(i => i.Job.WorkspaceId == workspaceId) + 1:D4}";

    private async Task<string> NextReceiptNumberAsync(Guid workspaceId) =>
        $"RCP-{await _context.Payments.IgnoreQueryFilters().CountAsync(p => p.WorkspaceId == workspaceId) + 1:D4}";

    /// <summary>Replaces EnsureClientExistsAsync - ClientId must resolve to a Person who holds
    /// Client or Finance UserAccess on this specific JobId, not just any active Person.</summary>
    private async Task EnsureClientHoldsBillingRoleOnJobAsync(Guid clientId, Guid jobId)
    {
        var personExists = await _context.People.AnyAsync(p => p.Id == clientId && p.IsActive);
        if (!personExists)
            throw new NotFoundException("Client not found");

        var holdsRole = await _context.UserAccesses
            .Include(ua => ua.User)
            .Where(ua => ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == jobId)
            .Where(ua => ua.Role.Name == Constants.SystemRoles.Client || ua.Role.Name == Constants.SystemRoles.Finance)
            .AnyAsync(ua => ua.User.PersonId == clientId);

        if (!holdsRole)
            throw new ValidationException("ClientId must be a Person who holds Client or Finance access on this job.");
    }

    private static void ValidateLineItems(List<LineItemDto> items) { /* unchanged */ }

    private async Task<Invoice> FindInvoiceAsync(Guid workspaceId, Guid invoiceId)
    {
        return await _context.Invoices.Include(i => i.Payments).Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.Job.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Invoice not found");
    }
}
```
Add `using SurveyorLedger.Core;` if not already present (for `Constants`).

Apply the identical pattern to `QuotationService.cs`: `CreateAsync`/`UpdateAsync` call `EnsureJobAccessAsync(callerUserId, workspaceId, request.JobId, "create"/"edit")` + `EnsureClientHoldsBillingRoleOnJobAsync(request.ClientId, request.JobId)` (same private helper, duplicated in `QuotationService` since it has no shared base â€” matches this codebase's existing duplication of `EnsureClientExistsAsync`/`ValidateLineItems` between the two services); `GetByIdAsync`/`DeleteAsync` load first then `EnsureJobAccessAsync(..., quotation.JobId, "view"/"delete")`; `SearchAsync` filters `q.Job.WorkspaceId == workspaceId` plus the `accessibleJobIds`/`hasViewAll` gate; `ConvertToInvoiceAsync` checks `EnsureJobAccessAsync(callerUserId, workspaceId, quotation.JobId, "edit")` and `EnsureJobAccessAsync(callerUserId, workspaceId, quotation.JobId, "create")` (for the invoice) instead of the two flat `EnsureAllowedAsync` calls, and the resulting invoice copies `quotation.JobId` (already required, unchanged code); `NextNumberAsync` counts by `.Job.WorkspaceId == workspaceId` for both `"Q"` and `"INV"` branches; `FindQuotationAsync` filters `q.Id == quotationId && q.Job.WorkspaceId == workspaceId`.

- [ ] **Step 5: Run tests to verify they pass**
```
dotnet test --filter "FullyQualifiedName~InvoiceServiceTests|FullyQualifiedName~QuotationServiceTests"
```

- [ ] **Step 6: Commit**
```
git add api/src/SurveyorLedger.API/Services/InvoiceService.cs api/src/SurveyorLedger.API/Services/QuotationService.cs api/src/SurveyorLedger.API/Models/Billing/InvoiceDtos.cs api/src/SurveyorLedger.API/Models/Billing/QuotationDtos.cs api/tests/SurveyorLedger.API.Tests/Services/InvoiceServiceTests.cs api/tests/SurveyorLedger.API.Tests/Services/QuotationServiceTests.cs
git commit -m "$(cat <<'EOF'
feat: switch Invoice/Quotation access checks to job scope

InvoiceService/QuotationService now use EnsureJobAccessAsync instead of a
flat workspace permission check, and validate ClientId against
Client/Finance UserAccess on the specific job rather than just any active
Person - same pattern already used by Milestone/Document.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Delete ClientService/ClientsController/ClientDtos.cs

**Files:**
- Delete: `api/src/SurveyorLedger.API/Services/ClientService.cs`
- Delete: `api/src/SurveyorLedger.API/Controllers/ClientsController.cs`
- Delete: `api/src/SurveyorLedger.API/Models/Billing/ClientDtos.cs`
- Modify: `api/src/SurveyorLedger.API/Program.cs`
- Modify: `api/tests/SurveyorLedger.API.Tests/Services/InvoiceServiceTests.cs` (remove `IClientService` usage â€” see Task 8)
- Delete: `api/tests/SurveyorLedger.API.Tests/Services/ClientServiceTests.cs`

**Interfaces:**
- Consumes: none (this is pure deletion)
- Produces: nothing â€” downstream tasks (4, 8) must not reference `IClientService`/`ClientRequest`/`ClientResponse` anywhere

- [ ] **Step 1: Confirm no other consumer of IClientService**
```
cd api
grep -rn "IClientService\|ClientService\b" src/ --include=*.cs
```
Expect only `Program.cs` DI registration and the three files being deleted. `IInvoiceService.ComputeInvoiceTotals` was the only inbound dependency `ClientService` took â€” confirm nothing outside `ClientService.cs` depended on `ClientService`'s balance-aggregation logic (it didn't per the earlier read: `IClientService` interface is self-contained).

- [ ] **Step 2: Delete the three files**
```
git rm api/src/SurveyorLedger.API/Services/ClientService.cs
git rm api/src/SurveyorLedger.API/Controllers/ClientsController.cs
git rm api/src/SurveyorLedger.API/Models/Billing/ClientDtos.cs
git rm api/tests/SurveyorLedger.API.Tests/Services/ClientServiceTests.cs
```
(If `ClientServiceTests.cs` doesn't exist under that exact name, locate it first with `Glob "api/tests/**/*Client*Tests.cs"` and delete whatever's found.)

- [ ] **Step 3: Remove DI registration**
In `Program.cs`, delete:
```csharp
builder.Services.AddScoped<IClientService, ClientService>();
```
and update the comment above the billing registrations block (was: "InvoiceService intentionally does not depend on IClientService...") to:
```csharp
// Register billing services.
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IQuotationService, QuotationService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();
builder.Services.AddScoped<IStaffPaymentService, StaffPaymentService>();
```

- [ ] **Step 4: Build to confirm no dangling references**
```
dotnet build
```
Fix any remaining compile errors from stray references (e.g. `InvoicesController`/`QuotationsController` never referenced `ClientService` directly per the earlier read, so this should be clean).

- [ ] **Step 5: Commit**
```
git add -A
git commit -m "$(cat <<'EOF'
refactor: delete ClientService/ClientsController/ClientDtos

Billing recipients are now job participants (Client/Finance UserAccess),
added via the existing JobService.AddParticipantAsync flow - a separate
Client CRUD concept no longer exists per the job-scoped billing spec.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: QuestPDF dependency + PdfService

**Files:**
- Modify: `api/src/SurveyorLedger.API/SurveyorLedger.API.csproj`
- Create: `api/src/SurveyorLedger.API/Services/PdfService.cs`
- Modify: `api/src/SurveyorLedger.API/Program.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/PdfServiceTests.cs`

**Interfaces:**
- Consumes: `InvoiceService.ComputeInvoiceTotals(Invoice invoice)` (existing, exact tuple signature from Task 2), `Invoice`/`Quotation` entities with `.LineItems`/`.Client` loaded
- Produces: `IPdfService.GenerateInvoicePdf(Invoice invoice, (decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue, int DaysOverdue) totals) : byte[]`, `IPdfService.GenerateQuotationPdf(Quotation quotation) : byte[]`

- [ ] **Step 1: Write failing test**
```csharp
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class PdfServiceTests
{
    [Fact]
    public void GenerateInvoicePdf_ProducesNonEmptyPdfBytes()
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            Number = "INV-0001",
            ClientId = Guid.NewGuid(),
            JobId = Guid.NewGuid(),
            Status = "Sent",
            LineItems = new List<InvoiceLineItem> { new() { Id = Guid.NewGuid(), Description = "Survey work", Quantity = 2, UnitPrice = 5000m } },
            TaxRatePercent = 10,
            DiscountAmount = 0,
            Client = new Person { Id = Guid.NewGuid(), FirstName = "Acme", LastName = "Ltd", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var svc = new PdfService();

        var bytes = svc.GenerateInvoicePdf(invoice, (11000m, 0m, 11000m, false, 0));

        Assert.NotEmpty(bytes);
        Assert.Equal((byte)'%', bytes[0]); // PDF magic bytes start with %PDF
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```
cd api
dotnet test --filter "FullyQualifiedName~PdfServiceTests"
```
Expected: compile error â€” `PdfService`/`IPdfService` don't exist yet.

- [ ] **Step 3: Add QuestPDF package**
`SurveyorLedger.API.csproj` â€” add to the existing `<ItemGroup>`:
```xml
<PackageReference Include="QuestPDF" Version="2025.7.0" />
```
(Use whatever is the current stable QuestPDF release at implementation time â€” verify via `dotnet add package QuestPDF` rather than hand-typing a version that may have moved on; MIT-licensed Community edition requires setting `QuestPDF.Settings.License = LicenseType.Community;` once at startup.)

- [ ] **Step 4: Implement PdfService**
```csharp
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IPdfService
{
    byte[] GenerateInvoicePdf(Invoice invoice, (decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue, int DaysOverdue) totals);
    byte[] GenerateQuotationPdf(Quotation quotation);
}

/// <summary>
/// Functional line-item table, not a styled template - see spec's "Out of scope".
/// QuestPDF Community license is set once via QuestPDF.Settings in Program.cs.
/// </summary>
public class PdfService : IPdfService
{
    public byte[] GenerateInvoicePdf(Invoice invoice, (decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue, int DaysOverdue) totals)
    {
        var subtotal = invoice.LineItems.Sum(li => li.Quantity * li.UnitPrice);
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.Header().Text($"Invoice {invoice.Number}").FontSize(18).Bold();
                page.Content().Column(col =>
                {
                    col.Item().Text($"Billed to: {invoice.Client.FirstName} {invoice.Client.LastName}");
                    col.Item().Text($"Status: {invoice.Status}");
                    if (invoice.DueDate.HasValue)
                        col.Item().Text($"Due: {invoice.DueDate.Value:yyyy-MM-dd}");
                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(1); });
                        table.Header(h =>
                        {
                            h.Cell().Text("Description").Bold();
                            h.Cell().Text("Qty").Bold();
                            h.Cell().Text("Unit Price").Bold();
                            h.Cell().Text("Amount").Bold();
                        });
                        foreach (var li in invoice.LineItems)
                        {
                            table.Cell().Text(li.Description);
                            table.Cell().Text(li.Quantity.ToString("0.##"));
                            table.Cell().Text(li.UnitPrice.ToString("0.00"));
                            table.Cell().Text((li.Quantity * li.UnitPrice).ToString("0.00"));
                        }
                    });
                    col.Item().PaddingTop(10).AlignRight().Text($"Subtotal: {subtotal:0.00}");
                    col.Item().AlignRight().Text($"Tax ({invoice.TaxRatePercent}%): {(subtotal * invoice.TaxRatePercent / 100m):0.00}");
                    col.Item().AlignRight().Text($"Discount: -{invoice.DiscountAmount:0.00}");
                    col.Item().AlignRight().Text($"Total: {totals.Total:0.00}").Bold();
                    col.Item().AlignRight().Text($"Paid: {totals.AmountPaid:0.00}");
                    col.Item().AlignRight().Text($"Balance: {totals.Balance:0.00}").Bold();
                });
            });
        }).GeneratePdf();
    }

    public byte[] GenerateQuotationPdf(Quotation quotation)
    {
        var subtotal = quotation.LineItems.Sum(li => li.Quantity * li.UnitPrice);
        var tax = subtotal * quotation.TaxRatePercent / 100m;
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.Header().Text($"Quotation {quotation.Number}").FontSize(18).Bold();
                page.Content().Column(col =>
                {
                    col.Item().Text($"Prepared for: {quotation.Client.FirstName} {quotation.Client.LastName}");
                    col.Item().Text($"Status: {quotation.Status}");
                    if (quotation.ValidUntil.HasValue)
                        col.Item().Text($"Valid until: {quotation.ValidUntil.Value:yyyy-MM-dd}");
                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(1); });
                        table.Header(h =>
                        {
                            h.Cell().Text("Description").Bold();
                            h.Cell().Text("Qty").Bold();
                            h.Cell().Text("Unit Price").Bold();
                            h.Cell().Text("Amount").Bold();
                        });
                        foreach (var li in quotation.LineItems)
                        {
                            table.Cell().Text(li.Description);
                            table.Cell().Text(li.Quantity.ToString("0.##"));
                            table.Cell().Text(li.UnitPrice.ToString("0.00"));
                            table.Cell().Text((li.Quantity * li.UnitPrice).ToString("0.00"));
                        }
                    });
                    col.Item().PaddingTop(10).AlignRight().Text($"Subtotal: {subtotal:0.00}");
                    col.Item().AlignRight().Text($"Tax ({quotation.TaxRatePercent}%): {tax:0.00}");
                    col.Item().AlignRight().Text($"Total: {(subtotal + tax):0.00}").Bold();
                });
            });
        }).GeneratePdf();
    }
}
```

- [ ] **Step 5: Register QuestPDF license + DI**
In `Program.cs`, near the top after `builder` is created:
```csharp
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
```
Add to the billing services block:
```csharp
builder.Services.AddScoped<IPdfService, PdfService>();
```

- [ ] **Step 6: Run test to verify it passes**
```
dotnet test --filter "FullyQualifiedName~PdfServiceTests"
```

- [ ] **Step 7: Commit**
```
git add api/src/SurveyorLedger.API/SurveyorLedger.API.csproj api/src/SurveyorLedger.API/Services/PdfService.cs api/src/SurveyorLedger.API/Program.cs api/tests/SurveyorLedger.API.Tests/Services/PdfServiceTests.cs
git commit -m "$(cat <<'EOF'
feat: add QuestPDF and PdfService for invoice/quotation line-item PDFs

Functional line-item table render, not a styled template - see spec's
"Out of scope: PDF template styling/branding".

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: IEmailService.SendBillingDocumentAsync + Invoice/Quotation Send service methods

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/EmailService.cs`
- Modify: `api/src/SurveyorLedger.API/Services/InvoiceService.cs`
- Modify: `api/src/SurveyorLedger.API/Services/QuotationService.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Billing/InvoiceDtos.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Billing/QuotationDtos.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/InvoiceServiceTests.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/QuotationServiceTests.cs`

**Interfaces:**
- Consumes: `IPdfService.GenerateInvoicePdf`/`GenerateQuotationPdf` (Task 4), `EmailClient.SendAsync` pattern from `EmailService.SendEmailAsync` (existing private helper â€” extend it to support attachments, since ACS `EmailMessage.Attachments` needs a separate call path)
- Produces: `IEmailService.SendBillingDocumentAsync(string toEmail, string documentType, string documentNumber, string linkUrl, byte[] pdfBytes, string pdfFileName)`, `IInvoiceService.SendAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, List<Guid> recipientPersonIds, string appBaseUrl) : Task`, `IQuotationService.SendAsync(...)` mirrored

- [ ] **Step 1: Write failing tests**
```csharp
// InvoiceServiceTests.cs
[Fact]
public async Task SendAsync_RecipientNotClientOrFinanceOnJob_Throws()
{
    _invoiceService = GetService<IInvoiceService>();
    var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();
    var invoiceId = await SeedInvoiceOnJobAsync(job.Id, clientPersonId);

    var strangerPerson = new SurveyorLedger.Data.Entities.Person
    {
        Id = Guid.NewGuid(), FirstName = "Not", LastName = "OnJob", Email = "notonjob@test.local",
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };
    Context.People.Add(strangerPerson);
    await Context.SaveChangesAsync();

    await Assert.ThrowsAsync<ValidationException>(() =>
        _invoiceService.SendAsync(WorkspaceId, AdminId, invoiceId, new List<Guid> { strangerPerson.Id }, "https://app.test.local"));
}

[Fact]
public async Task SendAsync_ClientOnJob_Succeeds()
{
    _invoiceService = GetService<IInvoiceService>();
    var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();
    var invoiceId = await SeedInvoiceOnJobAsync(job.Id, clientPersonId);

    await _invoiceService.SendAsync(WorkspaceId, AdminId, invoiceId, new List<Guid> { clientPersonId }, "https://app.test.local");
    // No throw = pass. A fake/stub IEmailService registered in ConfigureServices (Step 4) records calls for stronger assertion if desired.
}
```

- [ ] **Step 2: Run test to verify it fails**
```
dotnet test --filter "FullyQualifiedName~SendAsync"
```
Expected: compile error â€” `IInvoiceService.SendAsync` doesn't exist.

- [ ] **Step 3: EmailService â€” add SendBillingDocumentAsync with attachment**
`EmailService.cs`:
```csharp
public interface IEmailService
{
    Task SendVerificationOtpAsync(string email, string otpCode, int expirationMinutes);
    Task SendPasswordResetOtpAsync(string email, string otpCode, int expirationMinutes);
    Task SendWelcomeEmailAsync(string email, string firstName);
    Task SendInviteEmailAsync(string email, string workspaceName, string inviteUrl);
    Task SendBillingDocumentAsync(string email, string documentType, string documentNumber, string linkUrl, byte[] pdfBytes, string pdfFileName);
}
```
```csharp
public async Task SendBillingDocumentAsync(string email, string documentType, string documentNumber, string linkUrl, byte[] pdfBytes, string pdfFileName)
{
    if (string.IsNullOrWhiteSpace(email))
        throw new ValidationException("Email is required");

    var subject = $"{documentType} {documentNumber}";
    var body = $"A {documentType.ToLowerInvariant()} ({documentNumber}) is available for you. View it here: {linkUrl}";

    try
    {
        var message = new EmailMessage(
            senderAddress: _senderEmail,
            recipients: new EmailRecipients(new[] { new EmailAddress(email) }),
            content: new EmailContent(subject) { PlainText = body });
        message.Attachments.Add(new EmailAttachment(pdfFileName, "application/pdf", BinaryData.FromBytes(pdfBytes)));

        await _emailClient.SendAsync(WaitUntil.Completed, message);
        _logger.LogInformation("Billing document {DocumentType} {DocumentNumber} emailed to {Email}", documentType, documentNumber, email);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to send billing document email to {Email}", email);
        throw new AppException(Constants.ErrorCodes.EmailSendFailed, "Failed to send email");
    }
}
```
(`Azure.Communication.Email.EmailAttachment` constructor `(string name, string contentType, BinaryData content)` â€” verify exact signature against the installed `Azure.Communication.Email 1.0.1` package via `microsoft-code-reference` skill or IntelliSense before finalizing; if the installed version differs, adjust to whatever overload exists â€” this is the one place in the plan where the exact SDK signature should be double-checked against the live package rather than assumed.)

- [ ] **Step 4: InvoiceService.SendAsync**
Add to `IInvoiceService`:
```csharp
Task SendAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, List<Guid> recipientPersonIds, string appBaseUrl);
```
Constructor gains `IPdfService pdfService, IEmailService emailService`:
```csharp
private readonly IPdfService _pdfService;
private readonly IEmailService _emailService;

public InvoiceService(ApplicationDbContext context, IScopedAccessService access, IFileStorageService fileStorage, IPdfService pdfService, IEmailService emailService, ILogger<InvoiceService> logger)
{
    _context = context;
    _access = access;
    _fileStorage = fileStorage;
    _pdfService = pdfService;
    _emailService = emailService;
    _logger = logger;
}
```
```csharp
public async Task SendAsync(Guid workspaceId, Guid callerUserId, Guid invoiceId, List<Guid> recipientPersonIds, string appBaseUrl)
{
    var invoice = await FindInvoiceAsync(workspaceId, invoiceId);
    await _access.EnsureJobAccessAsync(callerUserId, workspaceId, invoice.JobId, "edit");

    if (recipientPersonIds.Count == 0)
        throw new ValidationException("At least one recipient is required.");

    var recipients = await _context.People
        .Where(p => recipientPersonIds.Contains(p.Id) && p.IsActive)
        .ToListAsync();
    if (recipients.Count != recipientPersonIds.Count)
        throw new NotFoundException("One or more recipients not found.");

    var eligiblePersonIds = await _context.UserAccesses
        .Include(ua => ua.User)
        .Where(ua => ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == invoice.JobId)
        .Where(ua => ua.Role.Name == Constants.SystemRoles.Client || ua.Role.Name == Constants.SystemRoles.Finance)
        .Select(ua => ua.User.PersonId)
        .ToListAsync();

    var ineligible = recipientPersonIds.Except(eligiblePersonIds).ToList();
    if (ineligible.Count > 0)
        throw new ValidationException("Every recipient must hold Client or Finance access on this invoice's job.");

    var totals = ComputeInvoiceTotals(invoice);
    var pdfBytes = _pdfService.GenerateInvoicePdf(invoice, totals);
    var linkUrl = $"{appBaseUrl.TrimEnd('/')}/app/jobs/{invoice.JobId}";

    foreach (var recipient in recipients)
    {
        if (string.IsNullOrWhiteSpace(recipient.Email))
            continue;
        await _emailService.SendBillingDocumentAsync(recipient.Email, "Invoice", invoice.Number, linkUrl, pdfBytes, $"{invoice.Number}.pdf");
    }

    if (invoice.Status == "Draft")
    {
        invoice.Status = "Sent";
        invoice.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    _logger.LogInformation("Invoice {InvoiceId} sent to {Count} recipient(s)", invoiceId, recipients.Count);
}
```
`FindInvoiceAsync` must additionally `.Include(i => i.Client)` since the PDF needs `invoice.Client.FirstName`/`LastName` â€” update:
```csharp
private async Task<Invoice> FindInvoiceAsync(Guid workspaceId, Guid invoiceId)
{
    return await _context.Invoices.Include(i => i.Payments).Include(i => i.LineItems).Include(i => i.Client)
        .FirstOrDefaultAsync(i => i.Id == invoiceId && i.Job.WorkspaceId == workspaceId)
        ?? throw new NotFoundException("Invoice not found");
}
```

Mirror for `QuotationService`: add `IQuotationService.SendAsync(Guid workspaceId, Guid callerUserId, Guid quotationId, List<Guid> recipientPersonIds, string appBaseUrl)`, constructor gains `IPdfService pdfService, IEmailService emailService`, `FindQuotationAsync` adds `.Include(q => q.Client)`, body identical shape using `_pdfService.GenerateQuotationPdf(quotation)`, sets `quotation.Status = "Sent"` only when currently `"Draft"`, link is `{appBaseUrl}/app/jobs/{quotation.JobId}`.

- [ ] **Step 5: Add SendInvoiceRequest/SendQuotationRequest DTOs**
`InvoiceDtos.cs`:
```csharp
public class SendInvoiceRequest
{
    public List<Guid> RecipientPersonIds { get; set; } = new();
}
```
`QuotationDtos.cs`:
```csharp
public class SendQuotationRequest
{
    public List<Guid> RecipientPersonIds { get; set; } = new();
}
```

- [ ] **Step 6: Run tests to verify they pass**
```
dotnet test --filter "FullyQualifiedName~InvoiceServiceTests|FullyQualifiedName~QuotationServiceTests"
```
Note: `WorkspaceIntegrationTestBase`'s `ConfigureServices` override in `InvoiceServiceTests.cs` must now also register `IPdfService`/`IEmailService` â€” add `services.AddScoped<IPdfService, PdfService>();` and either a real `IEmailService` (will fail without ACS config â€” not viable in tests) or a minimal stub. Add to `InvoiceServiceTests.cs`:
```csharp
private class StubEmailService : IEmailService
{
    public List<(string Email, string DocumentType, string DocumentNumber)> Sent { get; } = new();
    public Task SendVerificationOtpAsync(string email, string otpCode, int expirationMinutes) => Task.CompletedTask;
    public Task SendPasswordResetOtpAsync(string email, string otpCode, int expirationMinutes) => Task.CompletedTask;
    public Task SendWelcomeEmailAsync(string email, string firstName) => Task.CompletedTask;
    public Task SendInviteEmailAsync(string email, string workspaceName, string inviteUrl) => Task.CompletedTask;
    public Task SendBillingDocumentAsync(string email, string documentType, string documentNumber, string linkUrl, byte[] pdfBytes, string pdfFileName)
    {
        Sent.Add((email, documentType, documentNumber));
        return Task.CompletedTask;
    }
}
```
and register it: `services.AddSingleton<IEmailService, StubEmailService>();` in `ConfigureServices`.

- [ ] **Step 7: Commit**
```
git add api/src/SurveyorLedger.API/Services/EmailService.cs api/src/SurveyorLedger.API/Services/InvoiceService.cs api/src/SurveyorLedger.API/Services/QuotationService.cs api/src/SurveyorLedger.API/Models/Billing/InvoiceDtos.cs api/src/SurveyorLedger.API/Models/Billing/QuotationDtos.cs api/tests/SurveyorLedger.API.Tests/Services/InvoiceServiceTests.cs api/tests/SurveyorLedger.API.Tests/Services/QuotationServiceTests.cs
git commit -m "$(cat <<'EOF'
feat: add Invoice/Quotation SendAsync with PDF email to job recipients

Each recipient must hold Client or Finance access on the document's job.
Sends a link into the app plus a PDF attachment via the new
IEmailService.SendBillingDocumentAsync.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Controllers/DTOs for the Send endpoints

**Files:**
- Modify: `api/src/SurveyorLedger.API/Controllers/InvoicesController.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/QuotationsController.cs`

**Interfaces:**
- Consumes: `IInvoiceService.SendAsync(Guid, Guid, Guid, List<Guid>, string)` (Task 5), `IQuotationService.SendAsync(...)` (Task 5)
- Produces: `POST /api/workspace/{workspaceId}/invoices/{id}/send`, `POST /api/workspace/{workspaceId}/quotations/{id}/send`

- [ ] **Step 1: InvoicesController â€” add Send action**
```csharp
[HttpPost("{id}/send")]
public async Task<IActionResult> Send(Guid workspaceId, Guid id, [FromBody] SendInvoiceRequest request)
{
    var appBaseUrl = $"{Request.Scheme}://{Request.Host}".Replace("://localhost:5296", "://localhost:4200"); // API origin isn't the UI origin - see Step 2 for the config-driven fix
    await _invoiceService.SendAsync(workspaceId, CallerId(), id, request.RecipientPersonIds, appBaseUrl);
    return NoContent();
}
```
- [ ] **Step 2: Prefer configuration over Request.Host string surgery**
Replace the ad-hoc `Replace(...)` above with an injected config value â€” cleaner and matches how `AzureCommunicationServices:SenderEmail` is read elsewhere. Add to `appsettings.json` (and `appsettings.Development.json`): `"App:BaseUrl": "http://localhost:4200"`. Inject `IConfiguration` into `InvoicesController`:
```csharp
private readonly IInvoiceService _invoiceService;
private readonly IConfiguration _config;

public InvoicesController(IInvoiceService invoiceService, IConfiguration config)
{
    _invoiceService = invoiceService;
    _config = config;
}

[HttpPost("{id}/send")]
public async Task<IActionResult> Send(Guid workspaceId, Guid id, [FromBody] SendInvoiceRequest request)
{
    var appBaseUrl = _config["App:BaseUrl"] ?? throw new InvalidOperationException("App:BaseUrl not configured");
    await _invoiceService.SendAsync(workspaceId, CallerId(), id, request.RecipientPersonIds, appBaseUrl);
    return NoContent();
}
```
- [ ] **Step 3: QuotationsController â€” mirror**
```csharp
private readonly IQuotationService _quotationService;
private readonly IInvoiceService _invoiceService;
private readonly IConfiguration _config;

public QuotationsController(IQuotationService quotationService, IInvoiceService invoiceService, IConfiguration config)
{
    _quotationService = quotationService;
    _invoiceService = invoiceService;
    _config = config;
}

[HttpPost("{id}/send")]
public async Task<IActionResult> Send(Guid workspaceId, Guid id, [FromBody] SendQuotationRequest request)
{
    var appBaseUrl = _config["App:BaseUrl"] ?? throw new InvalidOperationException("App:BaseUrl not configured");
    await _quotationService.SendAsync(workspaceId, CallerId(), id, request.RecipientPersonIds, appBaseUrl);
    return NoContent();
}
```
- [ ] **Step 4: Update InvoiceResponse/QuotationResponse ToResponse â€” no change needed** (JobId already surfaced; nothing new to project for Send since it returns `NoContent`).
- [ ] **Step 5: Build + manual verification**
```
dotnet build
```
Manually exercise via the existing `.http`/Swagger UI (`app.MapOpenApi()` in dev) â€” POST to `/api/workspace/{workspaceId}/invoices/{id}/send` with a valid `Client`-role `recipientPersonIds` entry, confirm `204`; with an ineligible person, confirm `400`.
- [ ] **Step 6: Commit**
```
git add api/src/SurveyorLedger.API/Controllers/InvoicesController.cs api/src/SurveyorLedger.API/Controllers/QuotationsController.cs api/src/SurveyorLedger.API/appsettings.json api/src/SurveyorLedger.API/appsettings.Development.json
git commit -m "$(cat <<'EOF'
feat: add POST /invoices/{id}/send and /quotations/{id}/send endpoints

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: UI â€” drop the Client picker in favor of job Client/Finance participants, add Send UI

**Files:**
- Modify: `api/src/SurveyorLedger.API/Models/Job/JobParticipantResponse.cs` (add `PersonId`)
- Modify: `api/src/SurveyorLedger.API/Controllers/JobController.cs:147-155` (map `PersonId = p.User.PersonId`)
- Modify: `ui/src/app/core/job.service.ts:20-27` (add `personId: string` to `JobParticipant`)
- Modify: `ui/src/app/core/billing.service.ts`
- Modify: `ui/src/app/pages/billing/invoices/invoice-form-modal/invoice-form-modal.component.ts` (not read line-by-line this session â€” see Self-Review Notes; wire per the contract below without assuming its current internals)
- Modify: `ui/src/app/pages/billing/quotations/quotation-form-modal/quotation-form-modal.component.ts` (same caveat)
- Modify: `ui/src/app/pages/billing/invoices/invoice-list.component.ts` (add a "Send" action; not read this session)
- Modify: `ui/src/app/pages/billing/quotations/quotation-list.component.ts` (same)
- Delete: `ui/src/app/shared/client-picker/client-picker.component.ts`
- Delete: `ui/src/app/pages/billing/clients/` (entire directory â€” `client-list.component.ts`, `client-form-modal/`)
- Create: `ui/src/app/shared/billing-recipient-picker/billing-recipient-picker.component.ts`

**Interfaces:**
- Consumes: `JobService.getParticipants(workspaceId: string, jobId: string): Observable<JobParticipant[]>` (existing, exact signature from `job.service.ts:93`), `JobParticipant { userId: string; personId: string; firstName: string; lastName: string; email: string | null; role: string; assignedAt: string; }` (`personId` added by this task's backend step above)
- Produces: `BillingRecipientPickerComponent` â€” `@Input() workspaceId`, `@Input() jobId`, `@Output() clientSelected: EventEmitter<string>` (emits the chosen `Person.Id` for `ClientId`, filtered to `role === 'Client' || role === 'Finance'`); `InvoiceService.send(workspaceId, invoiceId, recipientPersonIds: string[]): Observable<void>`, `QuotationService.send(...)` mirrored

- [ ] **Step 1: billing.service.ts â€” remove ClientService, add send()**
Delete the entire `ClientService` class, `Client`, `ClientRequest`, `ClientBalance` interfaces (no longer exist server-side). Update `InvoiceRequest`/`QuotationRequest`/`Invoice`/`Quotation` interfaces: `jobId: string | null` â†’ `jobId: string` (was optional/nullable, now required to match the API). Add to `InvoiceService`:
```typescript
send(workspaceId: string, invoiceId: string, recipientPersonIds: string[]): Observable<void> {
  return this.http.post<void>(`${this.base(workspaceId)}/${invoiceId}/send`, { recipientPersonIds });
}
```
Add the mirrored `send` method to `QuotationService`:
```typescript
send(workspaceId: string, quotationId: string, recipientPersonIds: string[]): Observable<void> {
  return this.http.post<void>(`${this.base(workspaceId)}/${quotationId}/send`, { recipientPersonIds });
}
```

- [ ] **Step 2: New BillingRecipientPickerComponent â€” replaces ClientPickerComponent**
```typescript
import { Component, EventEmitter, Input, OnChanges, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { JobService, JobParticipant } from '../../core/job.service';

@Component({
  selector: 'app-billing-recipient-picker',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div>
      <label class="block text-xs font-medium text-neutral-700 mb-xs">Client</label>
      @if (loading()) {
        <p class="text-xs text-neutral-500">Loading job participantsâ€¦</p>
      } @else if (eligible().length === 0) {
        <p class="text-xs text-neutral-500">
          No Client or Finance participant on this job yet. Add one from the job's Participants tab first.
        </p>
      } @else {
        <div class="border border-neutral-200 rounded divide-y divide-neutral-200">
          @for (p of eligible(); track p.personId) {
            <button
              type="button"
              class="w-full text-left px-md py-sm hover:bg-neutral-50"
              [class.bg-primary-50]="value === p.personId"
              (click)="select(p)"
            >
              <span class="text-sm text-neutral-900">{{ p.firstName }} {{ p.lastName }}</span>
              <span class="block text-xs text-neutral-500">{{ p.role }}{{ p.email ? ' Â· ' + p.email : '' }}</span>
            </button>
          }
        </div>
      }
    </div>
  `
})
export class BillingRecipientPickerComponent implements OnChanges {
  @Input() workspaceId = '';
  @Input() jobId: string | null = null;
  @Input() value: string | null = null; // selected Person.Id (ClientId)
  @Output() valueChange = new EventEmitter<string | null>();

  eligible = signal<JobParticipant[]>([]);
  loading = signal(false);

  constructor(private jobService: JobService) {}

  ngOnChanges(): void {
    if (!this.jobId) {
      this.eligible.set([]);
      return;
    }
    this.loading.set(true);
    this.jobService.getParticipants(this.workspaceId, this.jobId).subscribe({
      next: participants => {
        this.eligible.set(participants.filter(p => p.role === 'Client' || p.role === 'Finance'));
        this.loading.set(false);
      },
      error: () => {
        this.eligible.set([]);
        this.loading.set(false);
      }
    });
  }

  select(p: JobParticipant): void {
    this.valueChange.emit(p.personId);
  }
}
```
**Confirmed (verified directly, not left as a pre-flight guess): `JobParticipant.userId` is a `UserAccount.Id`, not a `Person.Id`.**
`api/src/SurveyorLedger.API/Controllers/JobController.cs:147-155`:
```csharp
private static JobParticipantResponse ToResponse(UserAccess p) => new()
{
    UserId = p.UserId,   // UserAccess.UserId -> UserAccount.Id
    FirstName = p.User.Person.FirstName,
    LastName = p.User.Person.LastName,
    Email = p.User.Person.Email,
    Role = p.Role.Name,
    AssignedAt = p.AssignedAt
};
```
`InvoiceRequest.clientId`/`EnsureClientHoldsBillingRoleOnJobAsync` (Task 2) require a `Person.Id`. So this task must first add a `PersonId` field to the response, as its own small backend step before the UI wiring below:

`api/src/SurveyorLedger.API/Models/Job/JobParticipantResponse.cs` - add one property:
```csharp
public class JobParticipantResponse
{
    public Guid UserId { get; set; }
    public Guid PersonId { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Email { get; set; }
    public required string Role { get; set; }
    public DateTime AssignedAt { get; set; }
}
```
`api/src/SurveyorLedger.API/Controllers/JobController.cs:147-155` - one line added to the existing mapping:
```csharp
private static JobParticipantResponse ToResponse(UserAccess p) => new()
{
    UserId = p.UserId,
    PersonId = p.User.PersonId,
    FirstName = p.User.Person.FirstName,
    LastName = p.User.Person.LastName,
    Email = p.User.Person.Email,
    Role = p.Role.Name,
    AssignedAt = p.AssignedAt
};
```
`ui/src/app/core/job.service.ts` - add `personId: string` to the `JobParticipant` interface (same file/line range as `userId` per `job.service.ts:20-27`).

The picker component below binds to `p.personId`, not `p.userId`.

- [ ] **Step 3: Wire into invoice-form-modal / quotation-form-modal**
Both modals currently take a `jobId` selection already (per `InvoiceRequest.jobId`/`QuotationRequest.jobId` existing as optional fields pre-spec). Replace their `<app-client-picker [workspaceId]="workspaceId" [(value)]="request.clientId">` usage with:
```html
<app-billing-recipient-picker [workspaceId]="workspaceId" [jobId]="request.jobId" [(value)]="request.clientId"></app-billing-recipient-picker>
```
Import `BillingRecipientPickerComponent` in the modal's `imports: []` array in place of `ClientPickerComponent`. Since `jobId` becomes required (Task 2/6), make the job selector itself required in the form (disable Save until `request.jobId` is set) and re-run `BillingRecipientPickerComponent.ngOnChanges` whenever the job selection changes (Angular does this automatically via `@Input() jobId` binding).

- [ ] **Step 4: Send UI in invoice-list / quotation-list**
Add a "Send" row action that opens a small dialog: fetch `JobService.getParticipants(workspaceId, invoice.jobId)`, pre-check every participant with `role === 'Client' || role === 'Finance'`, let the admin toggle checkboxes, then call `InvoiceService.send(workspaceId, invoice.invoiceId, selectedPersonIds)`. This is a new small dialog component (`send-invoice-modal.component.ts` / `send-quotation-modal.component.ts`), following the same standalone-component + signals pattern as `convert-quotation-modal.component.ts` (existing sibling â€” read that file as the structural template before implementing, since it wasn't read this session either but is the closest existing analog in the same directory).

- [ ] **Step 5: Delete the Clients pages and ClientPickerComponent**
```
git rm -r ui/src/app/pages/billing/clients
git rm ui/src/app/shared/client-picker/client-picker.component.ts
```
Remove any router entries pointing at the clients list/form (locate via `grep -rn "billing/clients\|ClientListComponent\|ClientFormModal" ui/src/app` and delete the matching route definitions â€” likely in a billing routes file not read this session).

- [ ] **Step 6: Build + typecheck**
```
cd ui
ng build
```
Fix any remaining references to the deleted `ClientService`/`ClientPickerComponent`.

- [ ] **Step 7: Commit**
```
git add ui/src/app
git commit -m "$(cat <<'EOF'
feat: replace Client picker with job participant picker, add Send UI

Invoice/quotation forms now pick ClientId from the selected job's
Client/Finance participants instead of a separate Client entity. Adds a
Send dialog pre-selecting current Client/Finance participants.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Test fixture cleanup for the migration + ClientService deletion

**Files:**
- Modify: `api/tests/SurveyorLedger.API.Tests/Services/InvoiceServiceTests.cs`
- Modify: `api/tests/SurveyorLedger.API.Tests/Services/QuotationServiceTests.cs`
- (Delete already covered in Task 3: `ClientServiceTests.cs`)

**Interfaces:**
- Consumes: `IJobService.CreateAsync`/`AddParticipantAsync` (existing, exact signatures from `JobService.cs:21-47` and `:198-224`) as the new seed path for `SeedJobWithClientParticipantAsync`
- Produces: `SeedJobAsync()`, `SeedJobWithClientParticipantAsync()`, `SeedInvoiceOnJobAsync(Guid jobId, Guid clientPersonId)`, `CreateWorkspaceMemberAsync()` â€” private test helpers used by Task 2/5's tests

- [ ] **Step 1: Remove every `ClientService`/`ClientRequest`-based seed in InvoiceServiceTests.cs**
`SeedInvoiceAsync` currently does `_clientService.CreateAsync(WorkspaceId, AdminId, new ClientRequest { Name = "Acme Ltd" })` then uses the returned `Person.Id` as `ClientId` with no `JobId` at all. Rewrite:
```csharp
private async Task<(Job Job, Guid ClientPersonId, Guid ClientUserAccountId)> SeedJobWithClientParticipantAsync()
{
    var jobService = GetService<IJobService>();
    var job = await jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Test Job" });

    var clientPerson = new SurveyorLedger.Data.Entities.Person
    {
        Id = Guid.NewGuid(), FirstName = "Acme", LastName = "Ltd", Email = $"client-{Guid.NewGuid():N}@test.local",
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };
    Context.People.Add(clientPerson);
    var clientAccount = new SurveyorLedger.Data.Entities.UserAccount
    {
        Id = Guid.NewGuid(), PersonId = clientPerson.Id, IsActive = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };
    Context.UserAccounts.Add(clientAccount);
    await Context.SaveChangesAsync();

    // AddParticipantAsync grants instantly only when HasConsentCoverageAsync is true (i.e. the
    // target already holds workspace-level access) - a bare Person with no workspace membership
    // will always fall to the invitation branch. Grant UserAccess directly instead, mirroring
    // what AddParticipantAsync would eventually produce, to keep this a fast unit-style seed.
    Context.UserAccesses.Add(new SurveyorLedger.Data.Entities.UserAccess
    {
        Id = Guid.NewGuid(), UserId = clientAccount.Id, RoleId = RoleConfiguration.ClientRoleId,
        ScopeType = Constants.ScopeTypes.Job, ScopeId = job.Id, IsActive = true,
        AssignedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    });
    await Context.SaveChangesAsync();

    return (job, clientPerson.Id, clientAccount.Id);
}

private async Task<Guid> SeedInvoiceOnJobAsync(Guid jobId, Guid clientPersonId)
{
    _invoiceService = GetService<IInvoiceService>();
    var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
    {
        ClientId = clientPersonId,
        JobId = jobId,
        LineItems = new List<LineItemDto> { new() { Description = "Survey", Quantity = 1, UnitPrice = 100000m } },
        TaxRatePercent = 0,
        DiscountAmount = 0,
        Status = "Sent"
    });
    return invoice.Id;
}
```
Update `SeedInvoiceAsync(DateTime? dueDate)` (used by the pre-existing payment/overdue tests) to call `SeedJobWithClientParticipantAsync()` first and pass its `job.Id`/`clientPerson.Id` into the `InvoiceRequest`, removing the `IClientService _clientService` field and its `ClientRequest`/`_clientService.CreateAsync` call entirely.

Add `CreateWorkspaceMemberAsync()`:
```csharp
private async Task<Guid> CreateWorkspaceMemberAsync()
{
    var person = new SurveyorLedger.Data.Entities.Person
    {
        Id = Guid.NewGuid(), FirstName = "Outsider", LastName = "Member", Email = $"outsider-{Guid.NewGuid():N}@test.local",
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };
    Context.People.Add(person);
    var account = new SurveyorLedger.Data.Entities.UserAccount
    {
        Id = Guid.NewGuid(), PersonId = person.Id, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };
    Context.UserAccounts.Add(account);
    Context.UserAccesses.Add(new SurveyorLedger.Data.Entities.UserAccess
    {
        Id = Guid.NewGuid(), UserId = account.Id, RoleId = RoleConfiguration.MemberRoleId,
        ScopeType = Constants.ScopeTypes.Workspace, ScopeId = WorkspaceId, IsActive = true,
        AssignedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    });
    await Context.SaveChangesAsync();
    return account.Id;
}
```
`JobRequest` shape (`Title`, and whatever else `JobService.CreateAsync` requires) â€” confirm the exact `JobRequest` DTO fields by reading `api/src/SurveyorLedger.API/Models/Job/JobDtos.cs` (or wherever it lives â€” not read this session) before finalizing this helper; the field name `Title` is confirmed from `JobService.cs:78` (`Title = request.Title.Trim()`), but other required fields (if any) need verification.

- [ ] **Step 2: Mirror the same cleanup in QuotationServiceTests.cs**
Apply the identical `SeedJobWithClientParticipantAsync`/`CreateWorkspaceMemberAsync` pattern (duplicate the helpers locally, or extract to a shared `BillingTestHelpers` base/mixin if the duplication becomes annoying â€” per "reuse before building," only extract once actually duplicated, not preemptively).

- [ ] **Step 3: Run full billing test suite**
```
cd api
dotnet test --filter "FullyQualifiedName~InvoiceServiceTests|FullyQualifiedName~QuotationServiceTests|FullyQualifiedName~PdfServiceTests"
```

- [ ] **Step 4: Commit**
```
git add api/tests/SurveyorLedger.API.Tests/Services/InvoiceServiceTests.cs api/tests/SurveyorLedger.API.Tests/Services/QuotationServiceTests.cs
git commit -m "$(cat <<'EOF'
test: rework Invoice/Quotation test fixtures for job-scoped billing

Seeds now create a Job + Client-role UserAccess instead of going through
the deleted ClientService, matching the new required JobId and
Client/Finance-on-job validation.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 9: End-to-end build + test verification

**Files:** none (verification only)

**Interfaces:** none

- [ ] **Step 1: Full backend build**
```
cd api
dotnet build
```
- [ ] **Step 2: Full backend test suite**
```
dotnet test
```
- [ ] **Step 3: Full frontend build + lint**
```
cd ui
ng build
```
- [ ] **Step 4: Manual golden-path check per rules.md**
Using Swagger/`.http`: create a job, add a `Client`-role and a `Finance`-role participant, create an invoice against that job with `ClientId` = the Client participant's `Person.Id`, confirm `201`; attempt the same with a random `Person.Id` not on the job, confirm `400`; call `POST .../send` with both participants' ids, confirm `204` and (if ACS is configured locally) an email with PDF attachment; log in as the Client-role user, confirm `GET .../invoices/{id}` succeeds; log in as a workspace Member with no job grant, confirm `403`.
- [ ] **Step 5: No commit this task** â€” this is a verification-only task per `superpowers:verification-before-completion`; report results to the user and stop, since `rules.md` says not to commit until explicitly asked.

---

## Self-Review Notes

- **Every spec requirement mapped to a task:** `JobId` required + `WorkspaceId` dropped (Task 1), `RoleScope`/`Role`/`RolePermission` seed updates for `Finance` + `Client`'s existing `invoice.view`/`quotation.view` (Task 1), `EnsureJobAccessAsync` switch + `ClientId` validation (Task 2), `ClientService`/`ClientsController`/`ClientDtos.cs` deletion + `billingclient` permission removal (Tasks 1 & 3), QuestPDF + PDF generation (Task 4), `IEmailService` new method + Send endpoints (Tasks 5â€“6), UI Client picker â†’ job participant picker + Send UI (Task 7), test fixture cleanup (Task 8), end-to-end verification (Task 9).
- **Correction found during drafting:** the spec says "`Client` role gains `invoice.view`/`quotation.view`" as if new, but the `RolePermissionConfiguration.cs` read in this session shows `Grant(...264, ClientRoleId, ViewQuotationId)` and `Grant(...265, ClientRoleId, ViewInvoiceId)` **already exist** (added alongside the pre-existing `billingclient.view` grant, id `...263`, being removed in this spec). Task 1 Step 6 documents this explicitly â€” no new grant needed for `Client`, only the `billingclient.*` removal and the new `Finance` grants.
- **Numbering/uniqueness nuance:** dropping `Invoice.WorkspaceId`/`Quotation.WorkspaceId` means the DB-level unique index on `Number` can no longer be `(WorkspaceId, Number)`; Task 1 changes it to `(JobId, Number)` and flags to the user that workspace-wide uniqueness of the sequential `INV-####` number becomes service-enforced-only (extremely unlikely to collide, sequential counter) rather than DB-enforced across the whole workspace. Worth a nod to the user if they want stronger guarantees â€” out of scope for this spec's explicit asks.
- **Resolved during controller review (post-drafting), not left as a guess:**
  - `JobController.cs:147-155` was read directly: `JobParticipantResponse.UserId = p.UserId` is confirmed to be a `UserAccount.Id`, not a `Person.Id`. Task 7 now adds a `PersonId` field to `JobParticipantResponse`/`JobParticipant` (backend DTO + controller mapping + UI interface) as an explicit first step, rather than leaving this as a pre-flight unknown for the implementer.
  - `WorkspaceIntegrationTestBase.cs:42` confirmed directly: `protected virtual void ConfigureServices(IServiceCollection services) { }` - the inference from `InvoiceServiceTests.cs`'s override was correct.
- **Session limits â€” remaining explicit exceptions, not invented facts:**
  - `Azure.Communication.Email.EmailAttachment` constructor signature in Task 5 Step 3 was written from general knowledge of the ACS SDK shape, not verified against the installed `1.0.1` package's actual API surface in this session. Flagged inline as a pre-implementation check.
  - `invoice-form-modal.component.ts`, `quotation-form-modal.component.ts`, `invoice-list.component.ts`, `quotation-list.component.ts`, `convert-quotation-modal.component.ts`, and the Job DTO file (`JobRequest`'s full field list beyond `Title`) were located by path but their internals were **not read** in this planning session due to time/budget constraints on an already ~25-file read pass. Task 7 and Task 8 explicitly call this out and instruct the implementer to read each file immediately before editing it, rather than presenting invented method bodies as fact.