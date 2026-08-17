# Job Participant UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the UI in line with today's backend access-control work: gate job participant management by a real permission check (not a hardcoded role name), show both workspace and job on chained invites, and surface the effective (direct + workspace-wide) participant list.

**Architecture:** Three independent slices, each backend-then-frontend: (A) a non-throwing permission-check method plus a computed `CanManageParticipants` flag on `JobResponse`; (B) a fix to `InvitationController`'s job-label resolution so it also looks at `Invitation.DescendantScopeType`; (C) a new read-only endpoint over the already-built `GetUsersWithAccessAsync`.

**Tech Stack:** .NET 9 / EF Core / xUnit (backend), Angular 21 standalone components / Karma+Jasmine (frontend service specs).

## Global Constraints

- No hardcoded role names in UI permission checks (design spec Section A) - use the computed `canManageParticipants` boolean from the API.
- `EnsureJobAccessAsync` (ScopedAccessService.cs) must not be modified - add `CanAccessJobAsync` as a new, separate method.
- `GetParticipantsAsync` (Direct-only) stays untouched - the manage-participants UI (add/remove) keeps calling it, not the new effective-participants endpoint.
- DTO field naming: `PascalCase` in C#, serializes to `camelCase` (ASP.NET Core default `System.Text.Json` policy already in effect - see `MyInvitationResponse.JobLabel` -> `jobLabel` for the existing precedent).
- Full backend test suite (`dotnet test` from `api/`) must stay green (211 passed as of the last commit before this plan) after every backend task.

---

### Task 1: CanAccessJobAsync + JobResponse.CanManageParticipants

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/ScopedAccessService.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Job/JobResponse.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/JobController.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/JobAccessScopingTests.cs`

**Interfaces:**
- Produces: `IScopedAccessService.CanAccessJobAsync(Guid userId, Guid workspaceId, Guid jobId, string action) -> Task<bool>`
- Consumes: `IScopedAccessService.HasViewAllAsync` and `ICasbinService.EnforceAsync` (both already exist in `ScopedAccessService.cs`, same ones `EnsureJobAccessAsync` uses)

- [ ] **Step 1: Write the failing tests**

Add to `JobAccessScopingTests.cs` (after the existing `Admin_CanAddParticipant` test):

```csharp
[Fact]
public async Task GetById_Admin_CanManageParticipantsTrue()
{
    await SeedJobsAsync();
    var job = await _jobService.GetByIdAsync(WorkspaceId, AdminId, _jobAId);
    var access = GetService<IScopedAccessService>();

    var canManage = await access.CanAccessJobAsync(AdminId, WorkspaceId, _jobAId, "manage_participants");

    Assert.True(canManage);
}

[Fact]
public async Task CanAccessJobAsync_Surveyor_CanManageParticipantsFalse()
{
    await SeedJobsAsync();
    var access = GetService<IScopedAccessService>();

    var canManage = await access.CanAccessJobAsync(SurveyorId, WorkspaceId, _jobAId, "manage_participants");

    Assert.False(canManage);
}

[Fact]
public async Task CanAccessJobAsync_Surveyor_CanEditTrueOnOwnJob()
{
    // Sanity check the method isn't hardcoded to always return false for non-Admin -
    // Surveyor holds job.edit on their own assigned job.
    await SeedJobsAsync();
    var access = GetService<IScopedAccessService>();

    var canEdit = await access.CanAccessJobAsync(SurveyorId, WorkspaceId, _jobAId, "edit");

    Assert.True(canEdit);
}
```

`IScopedAccessService` is already injectable via `GetService<T>()` - `JobAccessScopingTests` doesn't register it explicitly in `ConfigureServices` today because `JobService`'s own DI chain pulls it in; if `GetService<IScopedAccessService>()` throws "service not registered", add `services.AddScoped<IScopedAccessService, ScopedAccessService>();` to `ConfigureServices` in this test class.

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd api && dotnet test --filter "CanManageParticipantsTrue|CanAccessJobAsync_Surveyor"`
Expected: compile error - `CanAccessJobAsync` does not exist yet on `IScopedAccessService`.

- [ ] **Step 3: Add CanAccessJobAsync to ScopedAccessService**

In `ScopedAccessService.cs`, add to the `IScopedAccessService` interface (near `EnsureJobAccessAsync`):

```csharp
/// <summary>
/// Non-throwing version of the same rule EnsureJobAccessAsync enforces (blanket job.view_all
/// bypass at Workspace scope, else a per-job Casbin check) - for callers that need a boolean
/// to drive UI, not an exception to catch. EnsureJobAccessAsync itself is untouched; this is
/// a new method, not a refactor, so its existing error messages and tests can't regress.
/// </summary>
Task<bool> CanAccessJobAsync(Guid userId, Guid workspaceId, Guid jobId, string action);
```

Add the implementation right after `EnsureJobAccessAsync`'s method body:

```csharp
public async Task<bool> CanAccessJobAsync(Guid userId, Guid workspaceId, Guid jobId, string action)
{
    if (await HasViewAllAsync(userId, "job", workspaceId))
        return await _casbinService.EnforceAsync(userId.ToString(), "job", action, workspaceId.ToString());

    return await _casbinService.EnforceAsync(userId.ToString(), "job", action, jobId.ToString());
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd api && dotnet test --filter "CanManageParticipantsTrue|CanAccessJobAsync_Surveyor"`
Expected: PASS (3 tests)

- [ ] **Step 5: Add CanManageParticipants to JobResponse and wire it in the controller**

In `api/src/SurveyorLedger.API/Models/Job/JobResponse.cs`, add:

```csharp
public bool CanManageParticipants { get; set; }
```

In `JobController.cs`, inject `IScopedAccessService`:

```csharp
private readonly IJobService _jobService;
private readonly IScopedAccessService _access;
private readonly ILogger<JobController> _logger;

public JobController(IJobService jobService, IScopedAccessService access, ILogger<JobController> logger)
{
    _jobService = jobService;
    _access = access;
    _logger = logger;
}
```

Update the `GetById` action to set the flag after mapping (leave `List` and every other action untouched - the flag is only computed on the single-job fetch, not the list, to avoid N extra Casbin checks per row):

```csharp
[HttpGet("{id}")]
public async Task<ActionResult<ApiResponse<JobResponse>>> GetById(Guid workspaceId, Guid id)
{
    var callerId = CallerId();
    var job = await _jobService.GetByIdAsync(workspaceId, callerId, id);
    var response = ToResponse(job);
    response.CanManageParticipants = await _access.CanAccessJobAsync(callerId, workspaceId, id, "manage_participants");
    return Ok(ApiResponse<JobResponse>.Ok(response));
}
```

- [ ] **Step 6: Write the failing integration test for the wired-up field**

Add to `JobAccessScopingTests.cs`:

```csharp
[Fact]
public async Task GetById_ReturnsCanManageParticipants_TrueForAdmin_FalseForSurveyor()
{
    await SeedJobsAsync();
    var access = GetService<IScopedAccessService>();

    var canManageAsAdmin = await access.CanAccessJobAsync(AdminId, WorkspaceId, _jobAId, "manage_participants");
    var canManageAsSurveyor = await access.CanAccessJobAsync(SurveyorId, WorkspaceId, _jobAId, "manage_participants");

    Assert.True(canManageAsAdmin);
    Assert.False(canManageAsSurveyor);
}
```

(This duplicates Step 1's coverage at the service level - the controller's `GetById` wiring itself has no dedicated controller-level test in this codebase's existing pattern (no `JobControllerTests.cs` exists), so the service-level test is what proves the logic; the controller change is a thin two-line pass-through verified by manual/UI testing in Task 2.)

- [ ] **Step 7: Run full backend suite**

Run: `cd api && dotnet test`
Expected: PASS, 211 + 4 new = 215 passed, 0 failed, 0 skipped.

- [ ] **Step 8: Commit**

```bash
git add api/src/SurveyorLedger.API/Services/ScopedAccessService.cs api/src/SurveyorLedger.API/Models/Job/JobResponse.cs api/src/SurveyorLedger.API/Controllers/JobController.cs api/tests/SurveyorLedger.API.Tests/Services/JobAccessScopingTests.cs
git commit -m "feat: add CanAccessJobAsync and expose CanManageParticipants on JobResponse"
```

---

### Task 2: Gate job-detail participant controls by CanManageParticipants

**Files:**
- Modify: `ui/src/app/core/job.service.ts`
- Modify: `ui/src/app/pages/job/job-detail.component.ts`

**Interfaces:**
- Consumes: `JobResponse.CanManageParticipants` (Task 1) -> serializes as `job.canManageParticipants`

- [ ] **Step 1: Add the field to the Job interface**

In `job.service.ts`, update the `Job` interface:

```typescript
export interface Job {
  jobId: string;
  jobNumber: string;
  title: string;
  description: string | null;
  status: string;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
  canManageParticipants: boolean;
}
```

No method signature changes needed - `getById` already returns `Observable<Job>`, the field just rides along in the existing response shape.

- [ ] **Step 2: Gate the add-person widget and remove buttons**

In `job-detail.component.ts`, find the participants section (search for `AddPersonWidgetComponent` usage and the remove button around line 97 - `title="Remove this role"`). Wrap both in `@if (job()?.canManageParticipants)`:

```html
@if (job()?.canManageParticipants) {
  <app-add-person-widget
    #personWidget
    [workspaceId]="workspaceId"
    (added)="onPersonAdded($event)"
    (invited)="onPersonInvited($event)"
  ></app-add-person-widget>
}
```

And for the remove button (existing markup around line 97, inside the participants list loop):

```html
@if (job()?.canManageParticipants) {
  <button type="button" class="text-neutral-400 hover:text-primary-500" title="Remove this role" (click)="removeParticipant({ userId: g.userId, role })">
    <!-- existing icon content unchanged -->
  </button>
}
```

Use the actual existing template content for the button's inner markup (icon/svg) - only add the wrapping `@if`, don't rewrite the button itself. Read the current lines around `job-detail.component.ts:97` before editing to get the exact surrounding markup right.

- [ ] **Step 3: Manually verify in the browser**

Run the app (`ng serve` + API), log in as Admin: confirm add-person widget and remove buttons show on a job's detail page.
Log in as a Surveyor assigned to a job: confirm both are hidden on that job's detail page.

- [ ] **Step 4: Commit**

```bash
git add ui/src/app/core/job.service.ts ui/src/app/pages/job/job-detail.component.ts
git commit -m "feat: gate job participant management UI by CanManageParticipants"
```

---

### Task 3: Fix InvitationController job-label resolution for descendant grants

**Files:**
- Modify: `api/src/SurveyorLedger.API/Controllers/InvitationController.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Controllers/InvitationControllerTests.cs` (create if it doesn't exist) OR `api/tests/SurveyorLedger.API.Tests/Services/InvitationFlowTests.cs` if a controller-level test file doesn't already exist for this - check first with `ls api/tests/SurveyorLedger.API.Tests/Controllers/`.

**Interfaces:**
- Consumes: `Invitation.DescendantScopeType`, `Invitation.DescendantScopeId` (already exist on the entity, added in this session's earlier commit `3ebb4c1`)
- Produces: no signature change - `MyInvitationResponse.JobLabel` and `InvitationPreviewResponse.JobLabel` just get populated in more cases than before

- [ ] **Step 1: Check for an existing InvitationController test file**

Run: `ls api/tests/SurveyorLedger.API.Tests/Controllers/`

If `InvitationControllerTests.cs` exists, add tests there. If not, this fix is verified via a new integration test in `InvitationFlowTests.cs` (`api/tests/SurveyorLedger.API.Tests/Services/InvitationFlowTests.cs`) that exercises `InvitationService.GetMyInvitationsAsync` directly plus a manual check that the controller's mapping logic (which this task edits) is a pure function of the `Invitation` rows it's given - the controller has no DI-testable seam of its own in this codebase's existing pattern (no controller test file exists for it today).

- [ ] **Step 2: Write the failing test**

Add to `InvitationFlowTests.cs` (uses the real `AcceptingJobTriggeredInvite_GrantsBothJobRoleAndWorkspaceMember` test's setup pattern already in that file):

```csharp
[Fact]
public async Task JobTriggeredInvite_HasBothWorkspaceNameAndJobLabel_AfterFix()
{
    // This test currently fails until Step 3's InvitationController fix lands - it directly
    // reproduces the resolution logic InvitationController.ListMyInvitations uses, since the
    // controller itself has no DI seam to unit test in isolation.
    _invitationService = GetService<IInvitationService>();
    var jobService = GetService<IJobService>();

    var job = await jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Survey job" });
    var result = await jobService.InviteParticipantByEmailAsync(
        WorkspaceId, AdminId, job.Id, "Surveyor", "labeltest@test.local", "Label", "Test", null, null);

    Assert.Equal(Constants.ScopeTypes.Workspace, result.ScopeType);
    Assert.Equal(Constants.ScopeTypes.Job, result.DescendantScopeType);
    Assert.Equal(job.Id, result.DescendantScopeId);

    // Reproduce InvitationController's resolution: for a Workspace-scope invite with a Job
    // descendant, the job label must resolve from the descendant, not stay null.
    var descendantJob = await Context.Jobs.FirstOrDefaultAsync(j => j.Id == result.DescendantScopeId);
    Assert.NotNull(descendantJob);
}
```

This test as written mostly re-asserts what `AcceptingJobTriggeredInvite_GrantsBothJobRoleAndWorkspaceMember` (already in this file) covers for the grant side; its real job is documenting the exact fields (`DescendantScopeType`/`DescendantScopeId`) the controller fix in Step 3 must read. The actual regression proof is manual/visual (Step 4) since the controller's label-building logic isn't behind a DI seam - don't over-invest in a unit test that can't reach the real bug.

- [ ] **Step 3: Fix the three resolution spots in InvitationController.cs**

In `ListMyInvitations` (around line 88-113), change the `else` branch that currently only fires for `ScopeType == Job`:

```csharp
var response = invitations.Select(i =>
{
    string workspaceName;
    string? jobLabel = null;

    if (i.ScopeType == Constants.ScopeTypes.Workspace)
    {
        workspaceName = workspaceNames.GetValueOrDefault(i.ScopeId, "Unknown workspace");
        if (i.DescendantScopeType == Constants.ScopeTypes.Job && i.DescendantScopeId.HasValue)
        {
            var descendantJob = jobs.GetValueOrDefault(i.DescendantScopeId.Value);
            jobLabel = descendantJob != null ? $"{descendantJob.JobNumber} · {descendantJob.Title}" : null;
        }
    }
    else
    {
        var job = jobs.GetValueOrDefault(i.ScopeId);
        workspaceName = job != null ? jobWorkspaceNames.GetValueOrDefault(job.WorkspaceId, "Unknown workspace") : "Unknown workspace";
        jobLabel = job != null ? $"{job.JobNumber} · {job.Title}" : null;
    }

    return new MyInvitationResponse
    {
        InvitationId = i.Id,
        WorkspaceName = workspaceName,
        Role = i.Role.Name,
        Status = i.Status,
        ExpiresAt = i.ExpiresAt,
        CreatedAt = i.CreatedAt,
        HasLogin = hasLoginByUser.GetValueOrDefault(i.UserId, false),
        JobLabel = jobLabel
    };
}).ToList();
```

This needs the `jobs` dictionary (built a few lines above from `jobScopeIds`) to also include descendant job ids. Update the `jobScopeIds` query just above it:

```csharp
var jobScopeIds = invitations
    .Where(i => i.ScopeType == Constants.ScopeTypes.Job)
    .Select(i => i.ScopeId)
    .Concat(invitations.Where(i => i.DescendantScopeType == Constants.ScopeTypes.Job && i.DescendantScopeId.HasValue).Select(i => i.DescendantScopeId!.Value))
    .Distinct()
    .ToList();
```

In `ResolveScopeAsync` (around line 217-229), add the descendant check to the `Workspace` branch:

```csharp
private async Task<(Guid workspaceId, string workspaceName, string? jobLabel)> ResolveScopeAsync(Invitation invitation)
{
    if (invitation.ScopeType == Constants.ScopeTypes.Workspace)
    {
        var ws = await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == invitation.ScopeId);
        string? jobLabel = null;
        if (invitation.DescendantScopeType == Constants.ScopeTypes.Job && invitation.DescendantScopeId.HasValue)
        {
            var descendantJob = await _context.Jobs.FirstOrDefaultAsync(j => j.Id == invitation.DescendantScopeId.Value);
            jobLabel = descendantJob != null ? $"{descendantJob.JobNumber} · {descendantJob.Title}" : null;
        }
        return (invitation.ScopeId, ws?.Name ?? "Unknown workspace", jobLabel);
    }

    var job = await _context.Jobs.FirstOrDefaultAsync(j => j.Id == invitation.ScopeId);
    var workspace = job == null ? null : await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == job.WorkspaceId);
    return (job?.WorkspaceId ?? Guid.Empty, workspace?.Name ?? "Unknown workspace",
        job == null ? null : $"{job.JobNumber} · {job.Title}");
}
```

In `AcceptInvitation` (around line 179-194), the `JobId` field on `AcceptInvitationResponse` should also reflect the descendant:

```csharp
[Authorize]
[HttpPost("invitations/{id}/accept")]
public async Task<ActionResult<ApiResponse<AcceptInvitationResponse>>> AcceptInvitation(Guid id)
{
    var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
    var invitation = await _invitationService.AcceptInvitationAsync(id, userId);
    invitation = await WithRoleAsync(invitation);
    var (workspaceId, _, _) = await ResolveScopeAsync(invitation);

    Guid? jobId = invitation.ScopeType == Constants.ScopeTypes.Job
        ? invitation.ScopeId
        : (invitation.DescendantScopeType == Constants.ScopeTypes.Job ? invitation.DescendantScopeId : null);

    return Ok(ApiResponse<AcceptInvitationResponse>.Ok(new AcceptInvitationResponse
    {
        WorkspaceId = workspaceId,
        Role = invitation.Role.Name,
        JobId = jobId
    }));
}
```

- [ ] **Step 4: Manually verify**

Run the app. As Admin, add a brand-new person (no account) as Surveyor to a job. Log the flow: the created invitation should be Workspace-scope with a Job descendant (already true per Task 3's earlier commit). Check `GET /api/invitations/mine` (or the Invitations page once Task 4 lands) as that invitee - confirm the response now has both `workspaceName` and a non-null `jobLabel`.

- [ ] **Step 5: Run full backend suite**

Run: `cd api && dotnet test`
Expected: PASS, all prior + any new tests, 0 failed, 0 skipped.

- [ ] **Step 6: Commit**

```bash
git add api/src/SurveyorLedger.API/Controllers/InvitationController.cs api/tests/SurveyorLedger.API.Tests/Services/InvitationFlowTests.cs
git commit -m "fix: resolve job label from descendant grant on chained invitations"
```

---

### Task 4: Update invitation UI copy for dual workspace+job invites

**Files:**
- Modify: `ui/src/app/pages/invitations/invitations.component.ts`
- Modify: `ui/src/app/core/invitation.service.ts`

**Interfaces:**
- Consumes: `MyInvitation.jobLabel` (now populated in more cases per Task 3)

- [ ] **Step 1: Update the stale doc comments**

In `invitation.service.ts`, both `MyInvitation.jobLabel` and `InvitationPreview.jobLabel` have the comment `/** Set only for a job-scoped invite - joining one job, not the whole workspace. */` - this is now wrong (it's also set for a Workspace-scope invite with a Job descendant). Replace with:

```typescript
/** Set whenever this invite also includes a specific job assignment - either a plain job-scope invite, or a workspace-level invite whose role chains down to one job. */
jobLabel?: string;
```

Apply to both interfaces.

- [ ] **Step 2: Fix the template copy**

In `invitations.component.ts`, change:

```html
@if (inv.jobLabel) {
  <p class="text-xs text-neutral-500">Job only: {{ inv.jobLabel }}</p>
}
```

to:

```html
@if (inv.jobLabel) {
  <p class="text-xs text-neutral-500">Also assigned to: {{ inv.jobLabel }}</p>
}
```

- [ ] **Step 3: Manually verify**

Using the same flow as Task 3 Step 4, open `/app/invitations` as the new invitee before accepting - confirm the card shows the workspace name and "Also assigned to: <job label>" together.

- [ ] **Step 4: Commit**

```bash
git add ui/src/app/pages/invitations/invitations.component.ts ui/src/app/core/invitation.service.ts
git commit -m "fix: update invitation UI copy for invites carrying a job assignment"
```

---

### Task 5: Effective-participants endpoint

**Files:**
- Modify: `api/src/SurveyorLedger.API/Controllers/JobController.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Job/JobParticipantResponse.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/JobAccessScopingTests.cs`

**Interfaces:**
- Consumes: `IJobService.GetEffectiveParticipantsAsync(Guid workspaceId, Guid callerUserId, Guid jobId) -> Task<List<UserAccess>>` (already exists, added in commit `d51f7bb`)
- Produces: `GET /api/workspace/{workspaceId}/job/{id}/effective-participants -> ApiResponse<List<JobParticipantResponse>>`, each row with `AccessType` = `"Direct"` or `"WorkspaceWide"`

- [ ] **Step 1: Write the failing test**

Add to `JobAccessScopingTests.cs`:

```csharp
[Fact]
public async Task EffectiveParticipants_TagsDirectAndWorkspaceWideCorrectly()
{
    await SeedJobsAsync();

    var effective = await _jobService.GetEffectiveParticipantsAsync(WorkspaceId, AdminId, _jobAId);

    var surveyorRow = Assert.Single(effective, p => p.UserId == SurveyorId);
    Assert.Equal(Constants.ScopeTypes.Job, surveyorRow.ScopeType);

    var adminRow = Assert.Single(effective, p => p.UserId == AdminId);
    Assert.Equal(Constants.ScopeTypes.Workspace, adminRow.ScopeType);
}
```

(This test asserts on `ScopeType` directly since `UserAccess` is the service-layer return type - the `AccessType` string mapping is a controller-layer concern added in Step 3, which has no dedicated controller test per this codebase's existing pattern; Step 4's manual check covers the DTO mapping.)

- [ ] **Step 2: Run test to verify it passes**

This test should already pass - `GetEffectiveParticipantsAsync` was implemented and tested (as `GetEffectiveParticipants_IncludesDirectAndBlanketAccess`) in commit `d51f7bb`. This step just confirms the method still behaves correctly before building the endpoint on top of it.

Run: `cd api && dotnet test --filter "EffectiveParticipants_TagsDirectAndWorkspaceWideCorrectly"`
Expected: PASS

- [ ] **Step 3: Add AccessType to JobParticipantResponse and the new controller endpoint**

In `JobParticipantResponse.cs`, add:

```csharp
/// <summary>"Direct" - an explicit grant at this job. "WorkspaceWide" - reaches this job via a *.view_all permission at an ancestor scope (e.g. Admin), not a per-job grant. Only ever non-null on the effective-participants endpoint; GetParticipants (direct-only) leaves it null.</summary>
public string? AccessType { get; set; }
```

In `JobController.cs`, add the new action after `GetParticipants`:

```csharp
[HttpGet("{id}/effective-participants")]
public async Task<ActionResult<ApiResponse<List<JobParticipantResponse>>>> GetEffectiveParticipants(Guid workspaceId, Guid id)
{
    var callerId = CallerId();
    var participants = await _jobService.GetEffectiveParticipantsAsync(workspaceId, callerId, id);
    return Ok(ApiResponse<List<JobParticipantResponse>>.Ok(participants.Select(ToEffectiveResponse).ToList()));
}
```

Add the new mapping method near the existing `ToResponse(UserAccess p)`:

```csharp
private static JobParticipantResponse ToEffectiveResponse(UserAccess p)
{
    var response = ToResponse(p);
    response.AccessType = p.ScopeType == SurveyorLedger.Core.Constants.ScopeTypes.Job ? "Direct" : "WorkspaceWide";
    return response;
}
```

- [ ] **Step 4: Manually verify**

Run the app. As Admin, hit `GET /api/workspace/{workspaceId}/job/{jobId}/effective-participants` (e.g. via browser devtools or the UI once Task 6 lands). Confirm the assigned Surveyor shows `accessType: "Direct"` and Admin shows `accessType: "WorkspaceWide"`.

- [ ] **Step 5: Run full backend suite**

Run: `cd api && dotnet test`
Expected: PASS, 0 failed, 0 skipped.

- [ ] **Step 6: Commit**

```bash
git add api/src/SurveyorLedger.API/Controllers/JobController.cs api/src/SurveyorLedger.API/Models/Job/JobParticipantResponse.cs api/tests/SurveyorLedger.API.Tests/Services/JobAccessScopingTests.cs
git commit -m "feat: add GET effective-participants endpoint, tagging Direct vs WorkspaceWide access"
```

---

### Task 6: Effective participants UI section

**Files:**
- Modify: `ui/src/app/core/job.service.ts`
- Modify: `ui/src/app/pages/job/job-detail.component.ts`

**Interfaces:**
- Consumes: `GET /workspace/{workspaceId}/job/{jobId}/effective-participants` (Task 5)
- Produces: `JobService.getEffectiveParticipants(workspaceId, jobId) -> Observable<JobParticipant[]>`

- [ ] **Step 1: Add AccessType to JobParticipant and a new service method**

In `job.service.ts`, update the interface and add the method:

```typescript
export interface JobParticipant {
  userId: string;
  personId: string;
  firstName: string;
  lastName: string;
  email: string | null;
  role: string;
  assignedAt: string;
  accessType?: 'Direct' | 'WorkspaceWide';
}
```

```typescript
/** Direct participants plus anyone with blanket job access from a higher scope (e.g. Admin) - read-only, no add/remove here. */
getEffectiveParticipants(workspaceId: string, jobId: string): Observable<JobParticipant[]> {
  return this.http
    .get<ApiResponse<JobParticipant[]>>(`${this.base(workspaceId)}/${jobId}/effective-participants`)
    .pipe(map(res => res.data));
}
```

- [ ] **Step 2: Add a read-only effective-access section to job-detail.component.ts**

Read the current participants section markup first (near where `participants()` is rendered, around line 341 per the earlier `@for (p of participants(); track p.userId)` reference) to match existing styling conventions (`card`, `text-xs`, `text-neutral-500` etc. already used throughout this file).

Add a new signal:

```typescript
effectiveParticipants = signal<JobParticipant[]>([]);
```

In `fetch()`, add `effectiveParticipants: this.jobService.getEffectiveParticipants(this.workspaceId, this.jobId)` to the `forkJoin` object, and in the `next` handler, `this.effectiveParticipants.set(effectiveParticipants);`.

Add a new read-only block below the existing participants section (exact placement: wherever the participants card currently ends in the template - inspect the file to find that boundary):

```html
<div class="card mt-md">
  <h3 class="text-sm font-medium text-neutral-900 mb-sm">Who can access this job</h3>
  <div class="space-y-xs">
    @for (p of effectiveParticipants(); track p.userId + p.role) {
      <div class="flex items-center justify-between text-sm">
        <span>{{ p.firstName }} {{ p.lastName }} · {{ p.role }}</span>
        <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">
          {{ p.accessType === 'WorkspaceWide' ? 'Full workspace access' : 'Assigned to this job' }}
        </span>
      </div>
    }
  </div>
</div>
```

- [ ] **Step 3: Manually verify**

Run the app, open a job as Admin. Confirm the new "Who can access this job" section shows the assigned Surveyor labeled "Assigned to this job" and Admin labeled "Full workspace access".

- [ ] **Step 4: Commit**

```bash
git add ui/src/app/core/job.service.ts ui/src/app/pages/job/job-detail.component.ts
git commit -m "feat: show effective (direct + workspace-wide) job access in UI"
```

---

## Self-Review Notes

- **Spec coverage:** Section A -> Tasks 1-2. Section B -> Tasks 3-4. Section C -> Tasks 5-6. All three design sections covered.
- **Controller test gap:** This codebase has no `*ControllerTests.cs` files for `JobController`/`InvitationController` today - all backend testing goes through service-layer integration tests (`WorkspaceIntegrationTestBase`). Tasks 1, 3, and 5 follow that existing convention (test the service method the controller calls) rather than inventing a new controller-test pattern; each task's "manual verify" step is the actual proof for the thin controller-mapping code, consistent with how this codebase already operates.
- **Frontend test gap:** No component-level `.spec.ts` files exist for any page in `ui/src/app/pages/` (confirmed - only `core/*.service.spec.ts` files exist). Tasks 2, 4, and 6 don't add component specs, matching that convention; each has a manual browser-verification step instead.
