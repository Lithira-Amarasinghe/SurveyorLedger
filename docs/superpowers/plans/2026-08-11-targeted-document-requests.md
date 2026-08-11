# Targeted Document Requests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A document request can target a specific role or a specific job participant instead of always being open. Targeting is an addition to the existing `DocumentRequest` entity/service/UI, not a new resource.

**Architecture:** Two nullable columns + a DB CHECK constraint on the existing `DocumentRequest` table (matches the entity's existing nullable-until-set pattern: `FulfilledDocumentId`/`FulfilledAt`/`FulfilledBy`). Fulfillment authorization gets two extra guard clauses in the existing `FulfillAsync`, not a parallel code path. UI adds one selector to the existing create form and one badge to the existing row template — no new components.

**Tech Stack:** Same as the parent Document Requests feature — .NET 9/EF Core 9, Angular 21 standalone/signals.

## Global Constraints

- This is a modification of existing files (`DocumentRequest.cs`, `DocumentRequestConfiguration.cs`, `DocumentRequestService.cs`, `DocumentRequestController.cs`, `document-request.service.ts`, `job-detail.component.ts`) — no new entities, services, or components.
- Targeting is a hard lock on fulfillment, including for Admin/Surveyor. Open requests (no targeting) keep today's behavior unchanged.
- No client-side current-user-id plumbing — the backend is the sole authority on entitlement; the UI just surfaces whatever error it returns.
- Migrations generated via `dotnet ef migrations add`, never hand-edited.
- Do not run `git commit` for any step — commit only when the user explicitly says to.
- Spec: `docs/superpowers/specs/2026-08-11-targeted-document-requests-design.md`.

---

### Task 1: Entity, migration, service validation and fulfillment lock

**Files:**
- Modify: `api/src/SurveyorLedger.Data/Entities/DocumentRequest.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/DocumentRequestConfiguration.cs`
- Modify: `api/src/SurveyorLedger.API/Services/DocumentRequestService.cs`
- Modify: `api/tests/SurveyorLedger.API.Tests/Services/DocumentRequestServiceTests.cs`
- Migration: generated under `api/src/SurveyorLedger.Data/Migrations/`

**Interfaces:**
- Consumes: `Constants.SystemRoles` (existing), `Constants.ScopeTypes.Job` (existing).
- Produces: `IDocumentRequestService.CreateAsync` gains two optional trailing parameters, `string? targetRole = null, Guid? targetUserId = null` (existing 5-arg call sites keep compiling unchanged — this is additive, not a breaking signature change). `DocumentRequest.TargetRole`/`TargetUserId` fields Task 2/3 read.

- [ ] **Step 1: Write the failing tests**

Add to `api/tests/SurveyorLedger.API.Tests/Services/DocumentRequestServiceTests.cs` (inside the existing `DocumentRequestServiceTests` class, alongside the current tests):

```csharp
[Fact]
public async Task Create_WithBothTargetRoleAndTargetUserId_ThrowsValidation()
{
    await SeedJobsAsync();
    await Assert.ThrowsAsync<ValidationException>(() =>
        _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument, Constants.SystemRoles.Client, ClientId));
}

[Fact]
public async Task Create_TargetingNonParticipant_ThrowsValidation()
{
    await SeedJobsAsync();
    // SurveyorId/ClientId are assigned to Job A only; nobody is assigned to Job B, so
    // targeting AdminId (who has full access but no job-scoped UserAccess row for Job A) works
    // as the non-participant case here since Admin never gets an explicit job assignment.
    await Assert.ThrowsAsync<ValidationException>(() =>
        _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument, null, AdminId));
}

[Fact]
public async Task Fulfill_RoleTargeted_WrongRole_ThrowsForbidden_EvenForAdmin()
{
    await SeedJobsAsync();
    var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument, Constants.SystemRoles.Client, null);

    await Assert.ThrowsAsync<ForbiddenException>(() =>
        _requestService.FulfillAsync(WorkspaceId, AdminId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible));
}

[Fact]
public async Task Fulfill_RoleTargeted_CorrectRole_Succeeds()
{
    await SeedJobsAsync();
    var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument, Constants.SystemRoles.Client, null);

    var fulfilled = await _requestService.FulfillAsync(WorkspaceId, ClientId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible);

    Assert.Equal("Fulfilled", fulfilled.Status);
}

[Fact]
public async Task Fulfill_PersonTargeted_WrongPerson_ThrowsForbidden_EvenForSurveyor()
{
    await SeedJobsAsync();
    var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument, null, ClientId);

    await Assert.ThrowsAsync<ForbiddenException>(() =>
        _requestService.FulfillAsync(WorkspaceId, SurveyorId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible));
}

[Fact]
public async Task Fulfill_PersonTargeted_CorrectPerson_Succeeds()
{
    await SeedJobsAsync();
    var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument, null, ClientId);

    var fulfilled = await _requestService.FulfillAsync(WorkspaceId, ClientId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible);

    Assert.Equal("Fulfilled", fulfilled.Status);
}

[Fact]
public async Task Fulfill_OpenRequest_StaffCanStillFulfillOnBehalf()
{
    // Regression guard: targeting must not change open-request behavior.
    await SeedJobsAsync();
    var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument, null, null);

    var fulfilled = await _requestService.FulfillAsync(WorkspaceId, AdminId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible);

    Assert.Equal("Fulfilled", fulfilled.Status);
}
```

- [ ] **Step 2: Run tests to verify they fail (compile error)**

```bash
cd api && dotnet test tests/SurveyorLedger.API.Tests --filter DocumentRequestServiceTests
```

Expected: build failure — `CreateAsync` doesn't accept 6 arguments yet.

- [ ] **Step 3: Add the entity fields**

In `api/src/SurveyorLedger.Data/Entities/DocumentRequest.cs`, add to the class (after `RequestedBy`):

```csharp
public string? TargetRole { get; set; }
public Guid? TargetUserId { get; set; }
```

And add the navigation property alongside the existing `FulfilledByUser`:

```csharp
public User? TargetUser { get; set; }
```

- [ ] **Step 4: Add the EF configuration**

In `api/src/SurveyorLedger.Data/Configurations/DocumentRequestConfiguration.cs`, add inside `Configure`:

```csharp
builder.Property(x => x.TargetRole).HasMaxLength(20);

builder.HasOne(x => x.TargetUser)
    .WithMany()
    .HasForeignKey(x => x.TargetUserId)
    .OnDelete(DeleteBehavior.Restrict);

// Two nullable columns following this entity's existing pattern (FulfilledDocumentId/
// FulfilledAt/FulfilledBy are already nullable-until-set the same way). App-level
// validation alone can't close the "both set" gap against a bug or a direct write,
// so it's enforced at the DB level too.
builder.ToTable(t => t.HasCheckConstraint(
    "CK_DocumentRequests_TargetExclusive",
    "[TargetRole] IS NULL OR [TargetUserId] IS NULL"));
```

- [ ] **Step 5: Generate and apply the migration**

```bash
cd api
dotnet ef migrations add AddDocumentRequestTargeting --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```

Expected: adds `TargetRole`, `TargetUserId` columns, the FK, and the CHECK constraint. Run the `migration-check` skill checklist.

- [ ] **Step 6: Update `CreateAsync` and `FulfillAsync`**

In `api/src/SurveyorLedger.API/Services/DocumentRequestService.cs`:

Update the interface method:

```csharp
Task<DocumentRequest> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, string title, string? description, DocumentCategory category, string? targetRole = null, Guid? targetUserId = null);
```

Replace `CreateAsync`'s body (keep the existing `FindJobAsync`/`EnsureJobAccessAsync`/title-validation lines, add the targeting validation right after them, before constructing `request`):

```csharp
public async Task<DocumentRequest> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, string title, string? description, DocumentCategory category, string? targetRole = null, Guid? targetUserId = null)
{
    await FindJobAsync(workspaceId, jobId);
    await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

    if (string.IsNullOrWhiteSpace(title))
        throw new ValidationException("Title is required.");

    if (targetRole != null && targetUserId.HasValue)
        throw new ValidationException("A request can target a role or a person, not both.");

    if (targetRole != null && targetRole != Constants.SystemRoles.Admin && targetRole != Constants.SystemRoles.Surveyor && targetRole != Constants.SystemRoles.Client)
        throw new ValidationException($"Unknown target role '{targetRole}'.");

    if (targetUserId.HasValue && !await IsAssignedToJobAsync(targetUserId.Value, jobId))
        throw new ValidationException("The targeted person is not assigned to this job.");

    var request = new DocumentRequest
    {
        Id = Guid.NewGuid(),
        JobId = jobId,
        Title = title.Trim(),
        Description = description,
        Category = category,
        Status = "Pending",
        TargetRole = targetRole,
        TargetUserId = targetUserId,
        RequestedBy = callerUserId,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    await _context.DocumentRequests.AddAsync(request);
    await _context.SaveChangesAsync();
    return request;
}
```

`IsAssignedToJobAsync` is the existing private helper this class already has for the caller's own assignment check — reused here for the target's assignment check, same query shape, no new method (DRY: one job-assignment query used from two call sites, not duplicated).

Add the entitlement check to `FulfillAsync`, right after `FindRequestAsync` and before the `_documentService.UploadAsync` call:

```csharp
if (request.TargetUserId.HasValue && request.TargetUserId != callerUserId)
    throw new ForbiddenException("This request is for a specific person.");

if (request.TargetRole != null)
{
    var callerRole = await GetCallerRoleAsync(callerUserId, workspaceId);
    if (callerRole != request.TargetRole)
        throw new ForbiddenException($"This request is for the {request.TargetRole} role.");
}
```

Add the private helper `DocumentRequestService` doesn't have yet (copy of `DocumentService.GetCallerRoleAsync` — same reasoning as this class's existing doc-comment already gives for duplicating `EnsureJobAccessAsync`'s shape: two call sites, no shared abstraction justified yet):

```csharp
private async Task<string> GetCallerRoleAsync(Guid callerUserId, Guid workspaceId)
{
    var role = await _context.UserAccesses
        .Where(ua => ua.UserId == callerUserId && ua.IsActive &&
                     ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId)
        .Select(ua => ua.Role.Name)
        .FirstOrDefaultAsync();

    return role ?? throw new ForbiddenException("You are not a member of this workspace.");
}
```

- [ ] **Step 7: Run tests to verify they pass**

```bash
cd api && dotnet test tests/SurveyorLedger.API.Tests --filter DocumentRequestServiceTests
```

Expected: 14 passed (8 existing + 6 new).

- [ ] **Step 8: Run the full backend suite**

```bash
cd api && dotnet build SurveyorLedger.sln && dotnet test tests/SurveyorLedger.API.Tests
```

Expected: build succeeds, 102 passed (96 existing + 6 new).

---

### Task 2: API surface

**Files:**
- Modify: `api/src/SurveyorLedger.API/Models/DocumentRequest/DocumentRequestCreateRequest.cs`
- Modify: `api/src/SurveyorLedger.API/Models/DocumentRequest/DocumentRequestResponse.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/DocumentRequestController.cs`

**Interfaces:**
- Consumes: `IDocumentRequestService.CreateAsync`'s new optional parameters (Task 1).
- Produces: `TargetRole`/`TargetUserId` on both the create request and response DTOs. Task 3's Angular service consumes the response shape.

- [ ] **Step 1: Add the fields to the create request DTO**

In `DocumentRequestCreateRequest.cs`, add after `Category`:

```csharp
public string? TargetRole { get; set; }
public Guid? TargetUserId { get; set; }
```

No `[Required]` — both are optional, mutual exclusivity is enforced server-side in the service (Task 1), not via a DataAnnotation (cross-field validation doesn't fit `[Required]`'s single-field model cleanly, and the service already owns this check).

- [ ] **Step 2: Add the fields to the response DTO**

In `DocumentRequestResponse.cs`, add after `Category`:

```csharp
public string? TargetRole { get; set; }
public Guid? TargetUserId { get; set; }
```

- [ ] **Step 3: Wire the controller**

In `DocumentRequestController.cs`, update the `Create` action's service call:

```csharp
var created = await _requestService.CreateAsync(workspaceId, CallerId(), jobId, request.Title, request.Description, request.Category, request.TargetRole, request.TargetUserId);
```

And add the two fields to `ToResponse`:

```csharp
TargetRole = r.TargetRole,
TargetUserId = r.TargetUserId,
```

- [ ] **Step 4: Build**

```bash
cd api && dotnet build SurveyorLedger.sln
```

Expected: succeeds.

---

### Task 3: UI — target selector and badge

**Files:**
- Modify: `ui/src/app/core/document-request.service.ts`
- Modify: `ui/src/app/pages/job/job-detail.component.ts`

**Interfaces:**
- Consumes: `DocumentRequestResponse`'s new fields via the existing `list()`/`create()` calls (Task 2).
- Produces: nothing further — UI integration point.

- [ ] **Step 1: Add the fields to the Angular interface and `create()`**

In `ui/src/app/core/document-request.service.ts`, add to the `DocumentRequest` interface (after `category`):

```ts
targetRole: 'Admin' | 'Surveyor' | 'Client' | null;
targetUserId: string | null;
```

Update `create()`'s signature and body:

```ts
create(workspaceId: string, jobId: string, title: string, description: string | null, category: string, targetRole: string | null, targetUserId: string | null): Observable<DocumentRequest> {
  return this.http
    .post<ApiResponse<DocumentRequest>>(this.base(workspaceId, jobId), { title, description, category, targetRole, targetUserId })
    .pipe(map(res => res.data));
}
```

- [ ] **Step 2: Add the target-selector fields to the request form**

In `ui/src/app/pages/job/job-detail.component.ts`, add two draft signals/fields alongside the existing `requestTitleDraft`/`requestDescriptionDraft`/`requestCategoryDraft`:

```ts
requestTargetKind: 'anyone' | 'role' | 'person' = 'anyone';
requestTargetRoleDraft = 'Client';
requestTargetUserIdDraft = '';
```

In the request form's template block (inside `@if (requestingDocument())`), add after the category `<select>`:

```html
<select class="input-field text-sm" [(ngModel)]="requestTargetKind">
  <option value="anyone">Anyone</option>
  <option value="role">By role</option>
  <option value="person">Specific person</option>
</select>
@if (requestTargetKind === 'role') {
  <select class="input-field text-sm" [(ngModel)]="requestTargetRoleDraft">
    <option value="Admin">Admin</option>
    <option value="Surveyor">Surveyor</option>
    <option value="Client">Client</option>
  </select>
} @else if (requestTargetKind === 'person') {
  <select class="input-field text-sm" [(ngModel)]="requestTargetUserIdDraft">
    <option value="" disabled>Select a person</option>
    @for (p of participants(); track p.userId) {
      <option [value]="p.userId">{{ p.firstName }} {{ p.lastName }}</option>
    }
  </select>
}
```

Reuses `participants()` — already loaded on this page, no new fetch.

- [ ] **Step 3: Update `submitRequest()` and `cancelAddRequest()`**

Replace `submitRequest()`'s body:

```ts
submitRequest(): void {
  if (!this.requestTitleDraft.trim()) {
    this.requestError.set('Title is required.');
    return;
  }
  if (this.requestTargetKind === 'person' && !this.requestTargetUserIdDraft) {
    this.requestError.set('Select a person to target, or switch to Anyone/By role.');
    return;
  }
  this.requestError.set('');

  const targetRole = this.requestTargetKind === 'role' ? this.requestTargetRoleDraft : null;
  const targetUserId = this.requestTargetKind === 'person' ? this.requestTargetUserIdDraft : null;

  this.documentRequestService
    .create(this.workspaceId, this.jobId, this.requestTitleDraft.trim(), this.requestDescriptionDraft.trim() || null, this.requestCategoryDraft, targetRole, targetUserId)
    .subscribe({
      next: (request) => {
        this.documentRequests.update(list => [request, ...list]);
        this.cancelAddRequest();
      },
      error: (err) => this.requestError.set(err.error?.message ?? 'Could not create request.')
    });
}
```

Add the three new fields' reset to `cancelAddRequest()`:

```ts
cancelAddRequest(): void {
  this.requestingDocument.set(false);
  this.requestTitleDraft = '';
  this.requestDescriptionDraft = '';
  this.requestCategoryDraft = 'Other';
  this.requestTargetKind = 'anyone';
  this.requestTargetRoleDraft = 'Client';
  this.requestTargetUserIdDraft = '';
  this.requestError.set('');
}
```

- [ ] **Step 4: Add the target badge to pending request rows**

In the pending-request row template (the `@if (row.kind === 'request' && row.request!.status === 'Pending')` block), add after the category badge:

```html
@if (row.request!.targetRole) {
  <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">for {{ row.request!.targetRole }}</span>
} @else if (row.request!.targetUserId; as targetId) {
  <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">for {{ targetPersonName(targetId) }}</span>
}
```

Add the small lookup helper (reuses `participants()`, no new fetch or state):

```ts
targetPersonName(userId: string): string {
  const p = this.participants().find(x => x.userId === userId);
  return p ? `${p.firstName} ${p.lastName}` : 'a specific person';
}
```

The Upload button on the row stays exactly as it is today — no entitlement check added client-side, per the spec's decision (backend is sole authority; a wrong-target upload attempt surfaces the backend's rejection message through the existing `documentError` signal, same as any other failed action on this page).

- [ ] **Step 5: Build and manually verify**

```bash
cd ui && ng build --configuration development
```

Expected: succeeds.

Manually, with API + UI running:
1. Create a request targeted "By role: Client" — badge shows "for Client".
2. As Admin, try Upload on it — rejected with "This request is for the Client role." shown via the error line.
3. As Client, Upload on it — succeeds, row becomes a normal document row.
4. Create a request targeted at a specific participant — badge shows their name.
5. As a different job participant (not the target), try Upload — rejected with "This request is for a specific person."
6. Create an open request (Anyone) — unchanged behavior, any assigned participant or staff can fulfill it.

- [ ] **Step 6: Do not commit** (per Global Constraints — wait for explicit instruction)

---

## Self-Review Notes

- **Spec coverage:** entity/migration/CHECK constraint + validation + fulfillment lock (Task 1), API DTOs (Task 2), UI selector/badge (Task 3). All spec sections covered.
- **DRY:** `IsAssignedToJobAsync` reused for the new target-participant check rather than a second query; `GetCallerRoleAsync` copied once with the same documented reasoning this codebase already uses elsewhere for small, two-call-site duplication instead of a premature shared abstraction; `participants()` reused in the UI rather than a new person-picker.
- **KISS:** no new entity, service, controller, or component — purely additive fields and guard clauses on what already exists. No client-side entitlement duplication (backend stays sole authority).
- **Type consistency:** `CreateAsync`'s new parameters are optional and trail the existing ones, so no existing call site (Task 1 of the parent Document Requests plan, or its tests) needs to change.
