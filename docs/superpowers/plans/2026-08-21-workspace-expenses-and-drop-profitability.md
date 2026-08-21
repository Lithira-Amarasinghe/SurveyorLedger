# Workspace-Level Expenses, Drop Milestone Profitability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Expense exist at workspace level (no job), additive to the existing job-scoped routes; remove the milestone profitability feature entirely.

**Architecture:** `Expense.JobId` becomes nullable. `ExpenseService` gains workspace-level counterparts of each method that skip `FindJobAsync` and set `JobId = null`. `ExpenseController` gains sibling routes at `/workspace/{id}/expense`. `ComputeProfitabilityAsync` and everything wired to it is deleted outright.

**Tech Stack:** .NET 9, EF Core 9, SQL Server LocalDB, xUnit integration tests.

## Global Constraints

- Job-scoped expense routes/behavior stay byte-for-byte unchanged.
- `MilestoneId` on a workspace-level expense (no `JobId`) is rejected.
- Migrations via `dotnet ef migrations add`, never hand-edited.
- Commit after each task.

---

### Task 1: Schema — nullable `Expense.JobId`, migration

**Files:**
- Modify: `api/src/SurveyorLedger.Data/Entities/Expense.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/ExpenseConfiguration.cs`
- Create (generated): migration under `api/src/SurveyorLedger.Data/Migrations/`

- [ ] **Step 1: Make `JobId` nullable, `Job` nav nullable**

In `Expense.cs`, change `public Guid JobId { get; set; }` to `public Guid? JobId { get; set; }`, and `public Job Job { get; set; }` to `public Job? Job { get; set; }`. Update the doc comment's first line to note workspace-level expenses exist.

- [ ] **Step 2: Mark the FK optional**

In `ExpenseConfiguration.cs`, change:

```csharp
builder.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
```

to:

```csharp
builder.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
```

- [ ] **Step 3: Build, generate and apply migration**

Run: `cd api && dotnet build src/SurveyorLedger.Data`
Expected: 0 errors.

Run: `cd api && dotnet ef migrations add MakeExpenseJobIdNullable --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`
Expected: `Up()` alters `Expenses.JobId` to nullable (and its FK), `Down()` reverses it. Note this will only succeed once Task 2 (service/controller code) also compiles, since `dotnet ef` builds the whole startup project - do Task 2 first if this fails to build, then come back and generate the migration as the last step of Task 2 instead.

- [ ] **Step 4: Commit (deferred until Task 2's code compiles - see Task 2 Step 6 for the actual migration generation/commit)**

---

### Task 2: `ExpenseService` workspace-level methods + `ExpenseController` routes + `MilestoneId` guard

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/ExpenseService.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/ExpenseController.cs`

- [ ] **Step 1: Add workspace-level methods to `IExpenseService`**

```csharp
Task<Expense> CreateWorkspaceLevelAsync(Guid workspaceId, Guid callerUserId, ExpenseRequest request);
Task<List<Expense>> GetAllWorkspaceLevelAsync(Guid workspaceId, Guid callerUserId);
Task<Expense> GetWorkspaceLevelByIdAsync(Guid workspaceId, Guid callerUserId, Guid expenseId);
Task<Expense> UpdateWorkspaceLevelAsync(Guid workspaceId, Guid callerUserId, Guid expenseId, ExpenseRequest request);
Task DeleteWorkspaceLevelAsync(Guid workspaceId, Guid callerUserId, Guid expenseId);
Task<Expense> UploadWorkspaceLevelReceiptAsync(Guid workspaceId, Guid callerUserId, Guid expenseId, IFormFile file);
Task<(Expense expense, Stream content)> GetWorkspaceLevelReceiptFileAsync(Guid workspaceId, Guid callerUserId, Guid expenseId);
```

- [ ] **Step 2: Implement the workspace-level methods, and a `FindExpenseAsync` overload that doesn't require a `jobId`**

Add to `ExpenseService`:

```csharp
public async Task<Expense> CreateWorkspaceLevelAsync(Guid workspaceId, Guid callerUserId, ExpenseRequest request)
{
    await _access.EnsureAllowedAsync(callerUserId, "expense", "create", workspaceId);
    await ValidateAndNormalizePayeeAsync(request);
    if (request.MilestoneId != null)
        throw new ValidationException("MilestoneId cannot be set on a workspace-level expense - milestones belong to a job.");
    var callerPersonId = await _access.ResolvePersonIdAsync(callerUserId);

    var expense = new Expense
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        JobId = null,
        Category = request.Category,
        Amount = request.Amount,
        Description = request.Description,
        IncurredDate = request.IncurredDate,
        PayeeId = request.PayeeId,
        PayeeType = request.PayeeType,
        MilestoneId = null,
        RecordedBy = callerPersonId,
        CreatedAt = DateTime.UtcNow
    };

    await _context.Expenses.AddAsync(expense);
    await _context.SaveChangesAsync();

    _logger.LogInformation("Workspace-level expense {ExpenseId} recorded in workspace {WorkspaceId} by {UserId}", expense.Id, workspaceId, callerUserId);
    return await FindWorkspaceLevelExpenseAsync(workspaceId, expense.Id);
}

public async Task<List<Expense>> GetAllWorkspaceLevelAsync(Guid workspaceId, Guid callerUserId)
{
    await _access.EnsureAllowedAsync(callerUserId, "expense", "view", workspaceId);

    var query = _context.Expenses.Include(e => e.RecordedByUser).Include(e => e.Payee)
        .Where(e => e.WorkspaceId == workspaceId && e.JobId == null);

    if (!await _access.HasViewAllAsync(callerUserId, "expense", workspaceId))
    {
        var callerPersonId = await _access.ResolvePersonIdAsync(callerUserId);
        query = query.Where(e => e.Category != StaffCostCategory || e.PayeeId == callerPersonId);
    }

    return await query.OrderByDescending(e => e.IncurredDate).ToListAsync();
}

public async Task<Expense> GetWorkspaceLevelByIdAsync(Guid workspaceId, Guid callerUserId, Guid expenseId)
{
    await _access.EnsureAllowedAsync(callerUserId, "expense", "view", workspaceId);
    var expense = await FindWorkspaceLevelExpenseAsync(workspaceId, expenseId);

    if (expense.Category == StaffCostCategory && !await _access.HasViewAllAsync(callerUserId, "expense", workspaceId))
    {
        var callerPersonId = await _access.ResolvePersonIdAsync(callerUserId);
        if (expense.PayeeId != callerPersonId)
            throw new NotFoundException("Expense not found");
    }

    return expense;
}

public async Task<Expense> UpdateWorkspaceLevelAsync(Guid workspaceId, Guid callerUserId, Guid expenseId, ExpenseRequest request)
{
    await _access.EnsureAllowedAsync(callerUserId, "expense", "edit", workspaceId);
    await ValidateAndNormalizePayeeAsync(request);
    if (request.MilestoneId != null)
        throw new ValidationException("MilestoneId cannot be set on a workspace-level expense - milestones belong to a job.");
    var expense = await FindWorkspaceLevelExpenseAsync(workspaceId, expenseId);

    expense.Category = request.Category;
    expense.Amount = request.Amount;
    expense.Description = request.Description;
    expense.IncurredDate = request.IncurredDate;
    expense.PayeeId = request.PayeeId;
    expense.PayeeType = request.PayeeType;

    await _context.SaveChangesAsync();
    return expense;
}

public async Task DeleteWorkspaceLevelAsync(Guid workspaceId, Guid callerUserId, Guid expenseId)
{
    await _access.EnsureAllowedAsync(callerUserId, "expense", "delete", workspaceId);
    var expense = await FindWorkspaceLevelExpenseAsync(workspaceId, expenseId);

    if (expense.ReceiptFilePath != null)
        await _fileStorage.DeleteAsync(expense.ReceiptFilePath, CancellationToken.None);

    _context.Expenses.Remove(expense);
    await _context.SaveChangesAsync();
}

public async Task<Expense> UploadWorkspaceLevelReceiptAsync(Guid workspaceId, Guid callerUserId, Guid expenseId, IFormFile file)
{
    await _access.EnsureAllowedAsync(callerUserId, "expense", "edit", workspaceId);
    var expense = await FindWorkspaceLevelExpenseAsync(workspaceId, expenseId);

    var extension = Path.GetExtension(file.FileName);
    if (!AllowedReceiptExtensions.Contains(extension))
        throw new ValidationException($"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", AllowedReceiptExtensions)}.");
    if (file.Length > DocumentService.MaxFileSizeBytes)
        throw new ValidationException($"File exceeds the {DocumentService.MaxFileSizeBytes / (1024 * 1024)}MB size limit.");

    var storedFileName = $"{Guid.NewGuid():N}_{file.FileName}";
    var relativePath = $"{workspaceId}/expenses/{expenseId}/{storedFileName}";

    await using (var stream = file.OpenReadStream())
    {
        await _fileStorage.SaveAsync(stream, relativePath, CancellationToken.None);
    }

    expense.ReceiptFilePath = relativePath;
    await _context.SaveChangesAsync();
    return expense;
}

public async Task<(Expense expense, Stream content)> GetWorkspaceLevelReceiptFileAsync(Guid workspaceId, Guid callerUserId, Guid expenseId)
{
    await _access.EnsureAllowedAsync(callerUserId, "expense", "view", workspaceId);
    var expense = await FindWorkspaceLevelExpenseAsync(workspaceId, expenseId);

    if (expense.ReceiptFilePath == null)
        throw new NotFoundException("No receipt uploaded for this expense.");

    var content = await _fileStorage.OpenAsync(expense.ReceiptFilePath, CancellationToken.None);
    return (expense, content);
}

private async Task<Expense> FindWorkspaceLevelExpenseAsync(Guid workspaceId, Guid expenseId)
{
    return await _context.Expenses.Include(e => e.RecordedByUser).Include(e => e.Payee)
        .FirstOrDefaultAsync(e => e.Id == expenseId && e.WorkspaceId == workspaceId && e.JobId == null)
        ?? throw new NotFoundException("Expense not found");
}
```

- [ ] **Step 3: Guard `MilestoneId` on the job-scoped path too when `JobId` might be absent**

The job-scoped `CreateAsync`/`UpdateAsync` already call `ValidateMilestoneAsync(jobId, request.MilestoneId)` with a non-null `jobId` (the route requires it) - no change needed there, `ValidateMilestoneAsync` already only allows a milestone belonging to that specific job.

- [ ] **Step 4: Add the workspace-level routes to `ExpenseController`**

Add a second route group to the same controller class (ASP.NET Core supports multiple `[Route]`-independent action-level routes via full paths, or - simpler here - add a `[HttpGet("/api/workspace/{workspaceId}/expense")]` style absolute override per action). Use explicit absolute routes on each new action so they don't inherit the class-level `job/{jobId}` prefix:

```csharp
[HttpGet("/api/workspace/{workspaceId}/expense")]
public async Task<ActionResult<ApiResponse<List<ExpenseResponse>>>> GetAllWorkspaceLevel(Guid workspaceId)
{
    var expenses = await _expenseService.GetAllWorkspaceLevelAsync(workspaceId, CallerId());
    return Ok(ApiResponse<List<ExpenseResponse>>.Ok(expenses.Select(ToResponse).ToList()));
}

[HttpGet("/api/workspace/{workspaceId}/expense/{id}")]
public async Task<ActionResult<ApiResponse<ExpenseResponse>>> GetWorkspaceLevelById(Guid workspaceId, Guid id)
{
    var expense = await _expenseService.GetWorkspaceLevelByIdAsync(workspaceId, CallerId(), id);
    return Ok(ApiResponse<ExpenseResponse>.Ok(ToResponse(expense)));
}

[HttpPost("/api/workspace/{workspaceId}/expense")]
public async Task<ActionResult<ApiResponse<ExpenseResponse>>> CreateWorkspaceLevel(Guid workspaceId, [FromBody] ExpenseRequest request)
{
    var expense = await _expenseService.CreateWorkspaceLevelAsync(workspaceId, CallerId(), request);
    return CreatedAtAction(nameof(GetWorkspaceLevelById), new { workspaceId, id = expense.Id }, ApiResponse<ExpenseResponse>.Ok(ToResponse(expense)));
}

[HttpPut("/api/workspace/{workspaceId}/expense/{id}")]
public async Task<ActionResult<ApiResponse<ExpenseResponse>>> UpdateWorkspaceLevel(Guid workspaceId, Guid id, [FromBody] ExpenseRequest request)
{
    var expense = await _expenseService.UpdateWorkspaceLevelAsync(workspaceId, CallerId(), id, request);
    return Ok(ApiResponse<ExpenseResponse>.Ok(ToResponse(expense)));
}

[HttpDelete("/api/workspace/{workspaceId}/expense/{id}")]
public async Task<IActionResult> DeleteWorkspaceLevel(Guid workspaceId, Guid id)
{
    await _expenseService.DeleteWorkspaceLevelAsync(workspaceId, CallerId(), id);
    return NoContent();
}

[HttpPost("/api/workspace/{workspaceId}/expense/{id}/receipt")]
[RequestSizeLimit(DocumentService.MaxFileSizeBytes)]
public async Task<ActionResult<ApiResponse<ExpenseResponse>>> UploadWorkspaceLevelReceipt(Guid workspaceId, Guid id, IFormFile file)
{
    var expense = await _expenseService.UploadWorkspaceLevelReceiptAsync(workspaceId, CallerId(), id, file);
    return Ok(ApiResponse<ExpenseResponse>.Ok(ToResponse(expense)));
}

[HttpGet("/api/workspace/{workspaceId}/expense/{id}/receipt")]
public async Task<IActionResult> GetWorkspaceLevelReceipt(Guid workspaceId, Guid id)
{
    var (expense, content) = await _expenseService.GetWorkspaceLevelReceiptFileAsync(workspaceId, CallerId(), id);
    return File(content, "application/octet-stream", Path.GetFileName(expense.ReceiptFilePath!));
}
```

`ToResponse`'s `JobId = e.JobId` assignment needs `ExpenseResponse.JobId` to become `Guid?` - update that DTO field too (in `ExpenseDtos.cs`, change `public Guid JobId { get; set; }` to `public Guid? JobId { get; set; }`).

- [ ] **Step 5: Build**

Run: `cd api && dotnet build`
Expected: 0 errors.

- [ ] **Step 6: Generate and apply the migration deferred from Task 1**

Run: `cd api && dotnet ef migrations add MakeExpenseJobIdNullable --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`
Expected: succeeds now that the whole solution compiles. Confirm `Up()`/`Down()` touch `Expenses.JobId` nullability and its FK, nothing else.

Run: `cd api && dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`
Expected: succeeds.

- [ ] **Step 7: Commit (covers Task 1 + Task 2 together, since the migration only builds once both are in place)**

```bash
git add api/src/SurveyorLedger.Data/Entities/Expense.cs api/src/SurveyorLedger.Data/Configurations/ExpenseConfiguration.cs api/src/SurveyorLedger.Data/Migrations api/src/SurveyorLedger.API/Services/ExpenseService.cs api/src/SurveyorLedger.API/Controllers/ExpenseController.cs api/src/SurveyorLedger.API/Models/Expense/ExpenseDtos.cs
git commit -m "feat: support workspace-level expenses alongside job-scoped ones

Expense.JobId is now nullable - a workspace-level expense (JobId null)
is not tied to any job and cannot carry a MilestoneId (milestones
belong to a job). Existing job-scoped routes/methods are unchanged;
new sibling routes at /workspace/{id}/expense cover the workspace-level
case, reusing the same category/payee validation and the same
workspace-wide expense.* permission check the job-scoped path already
used (job-scoped access was never gated per-job for expenses).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: Remove milestone profitability

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/MilestoneService.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/MilestoneController.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Milestone/MilestonePaymentRequirementDtos.cs`
- Delete: `api/tests/SurveyorLedger.API.Tests/Services/MilestoneProfitabilityTests.cs`

- [ ] **Step 1: Remove `ComputeProfitabilityAsync`**

In `MilestoneService.cs`, delete the interface line `Task<(decimal Revenue, decimal Expenses, decimal Profit)> ComputeProfitabilityAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid milestoneId);` and the whole method implementation (the one whose doc comment starts "Revenue is invoiced-amount, not paid-amount...").

- [ ] **Step 2: Remove the controller endpoint**

In `MilestoneController.cs`, delete the `GetProfitability` action:

```csharp
[HttpGet("{id}/profitability")]
public async Task<ActionResult<ApiResponse<MilestoneProfitabilityResponse>>> GetProfitability(Guid workspaceId, Guid jobId, Guid id)
{
    var (revenue, expenses, profit) = await _milestoneService.ComputeProfitabilityAsync(workspaceId, CallerId(), jobId, id);
    return Ok(ApiResponse<MilestoneProfitabilityResponse>.Ok(new MilestoneProfitabilityResponse { Revenue = revenue, Expenses = expenses, Profit = profit }));
}
```

- [ ] **Step 3: Remove the DTO**

In `MilestonePaymentRequirementDtos.cs`, delete the `MilestoneProfitabilityResponse` class.

- [ ] **Step 4: Delete the test file**

Run: `rm api/tests/SurveyorLedger.API.Tests/Services/MilestoneProfitabilityTests.cs`

- [ ] **Step 5: Build**

Run: `cd api && dotnet build`
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add api/src/SurveyorLedger.API/Services/MilestoneService.cs api/src/SurveyorLedger.API/Controllers/MilestoneController.cs api/src/SurveyorLedger.API/Models/Milestone/MilestonePaymentRequirementDtos.cs
git rm api/tests/SurveyorLedger.API.Tests/Services/MilestoneProfitabilityTests.cs
git commit -m "refactor: remove milestone profitability feature

Out of scope per updated requirements - GetCommittedAmountAsync/
EnsureWithinFeeCeilingAsync (the fee ceiling) are unrelated and stay.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 4: New tests for workspace-level expenses + full suite verification

**Files:**
- Create: `api/tests/SurveyorLedger.API.Tests/Services/WorkspaceLevelExpenseTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Expense;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class WorkspaceLevelExpenseTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IExpenseService _expenseService = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-workspace-expense-test-{Guid.NewGuid():N}")
                })
                .Build());
    }

    [Fact]
    public async Task WorkspaceLevelExpense_DoesNotAppearInJobScopedList()
    {
        _jobService = GetService<IJobService>();
        _expenseService = GetService<IExpenseService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        await _expenseService.CreateAsync(WorkspaceId, AdminId, job.Id, new ExpenseRequest
        {
            Category = "Other", Amount = 100m, IncurredDate = DateTime.UtcNow
        });
        var workspaceExpense = await _expenseService.CreateWorkspaceLevelAsync(WorkspaceId, AdminId, new ExpenseRequest
        {
            Category = "Other", Amount = 500m, IncurredDate = DateTime.UtcNow
        });

        var jobScoped = await _expenseService.GetAllAsync(WorkspaceId, AdminId, job.Id);
        Assert.DoesNotContain(jobScoped, e => e.Id == workspaceExpense.Id);

        var workspaceScoped = await _expenseService.GetAllWorkspaceLevelAsync(WorkspaceId, AdminId);
        Assert.Contains(workspaceScoped, e => e.Id == workspaceExpense.Id);
        Assert.DoesNotContain(workspaceScoped, e => e.Amount == 100m);
    }

    [Fact]
    public async Task WorkspaceLevelExpense_WithMilestoneId_IsRejected()
    {
        _jobService = GetService<IJobService>();
        _expenseService = GetService<IExpenseService>();

        var request = new ExpenseRequest
        {
            Category = "Other", Amount = 100m, IncurredDate = DateTime.UtcNow, MilestoneId = Guid.NewGuid()
        };

        await Assert.ThrowsAsync<ValidationException>(
            () => _expenseService.CreateWorkspaceLevelAsync(WorkspaceId, AdminId, request));
    }
}
```

- [ ] **Step 2: Run the new tests**

Run: `cd api && dotnet test --filter WorkspaceLevelExpenseTests`
Expected: PASS, both tests.

- [ ] **Step 3: Run the full suite**

Run: `cd api && dotnet test`
Expected: all pass, 0 failures.

- [ ] **Step 4: Commit**

```bash
git add api/tests/SurveyorLedger.API.Tests/Services/WorkspaceLevelExpenseTests.cs
git commit -m "test: cover workspace-level expenses and the MilestoneId rejection

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```
