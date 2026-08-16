# Dashboard Job Access Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give job-only users (job-scope grant, no workspace membership) a way to find and open their job, and add a scope-hierarchy-agnostic dashboard section + cross-workspace jobs list that will extend cleanly when an Organization level is added later.

**Architecture:** One new backend method (`ScopedAccessService.GetAccessibleJobsAsync`) walks the access hierarchy broadest-first and tags each job with the real `Constants.ScopeTypes` value the access was found at. Two new endpoints (`GET /api/jobs/mine`, `GET /api/jobs/{jobId}`) expose it. The dashboard renders a Workspaces section (unchanged) plus a Jobs section fed by the new endpoint, with a view toggle and filters. A new minimal route `/app/job/:workspaceId/:jobId` reuses the existing `JobDetailComponent` unmodified in its rendering, with one added fallback line for resolving `workspaceId`, and deliberately does **not** populate `CurrentWorkspaceService` - doing so would make `SidebarComponent` show the full workspace tab list (Overview/Land/Billing/Members) to a user who can't use most of it, since `SidebarComponent` renders that list off any truthy `currentWorkspace.current()` with no per-tab permission check.

**Tech Stack:** .NET 9 / EF Core 9 / xUnit (backend), Angular 21 standalone components / Vitest (frontend). No new dependencies.

## Global Constraints

- Every tenant-scoped query goes through `WorkspaceId` filtering - **except** `GetAccessibleJobsAsync`, a deliberate, documented exception (user-scoped, not workspace-scoped, same category as the existing `GetUserWorkspacesAsync`/`GetMyInvitationsAsync`). Say so in a code comment at the query site.
- Migrations are generated via `dotnet ef migrations add`, never hand-edited. This plan needs **zero** migrations - nothing here touches the schema.
- No hardcoded two-level (Workspace/Job) assumptions where the spec's "Scaling mechanism" section says otherwise - always use `Constants.ScopeTypes.*` and the new `Constants.ScopeHierarchy`, never a synthetic label.
- Backend: `dotnet build src/SurveyorLedger.API` after each backend task. Frontend: `npx tsc --noEmit -p tsconfig.app.json` after each frontend task. Both from their respective root (`api/` or `ui/`).

---

### Task 1: `Constants.ScopeHierarchy` + `ScopedAccessService.GetAccessibleJobsAsync`

**Files:**
- Modify: `api/src/SurveyorLedger.Core/Constants.cs`
- Modify: `api/src/SurveyorLedger.API/Services/ScopedAccessService.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/AccessibleJobsTests.cs` (new)

**Interfaces:**
- Consumes: `Constants.ScopeTypes.{Workspace,Job,Organization}` (existing), `ScopedAccessService.AccessibleJobIds(Guid userId): IQueryable<Guid>` (existing), `WorkspaceIntegrationTestBase` (existing test fixture - `WorkspaceId`, `AdminId`, `SurveyorId`, `ClientId`, `GrantService`, `GetService<T>()`).
- Produces: `Constants.ScopeHierarchy: string[]`, `ScopedAccessService.GetAccessibleJobsAsync(Guid userId): Task<List<AccessibleJob>>`, `public record AccessibleJob(Guid JobId, string JobNumber, string Title, string Status, Guid WorkspaceId, string WorkspaceName, string AccessScopeType)` - all consumed by Task 3's controller.

- [ ] **Step 1: Add `ScopeHierarchy` to `Constants.cs`**

Open `api/src/SurveyorLedger.Core/Constants.cs`, find the `ScopeTypes` nested class (`Workspace`, `Job`, `Organization` constants already there), add directly below it inside the outer `Constants` class:

```csharp
/// <summary>Root-to-leaf order of the access hierarchy - the one place this order is
/// declared. GetAccessibleJobsAsync (ScopedAccessService) walks it broadest-first.</summary>
public static readonly string[] ScopeHierarchy =
    { ScopeTypes.Organization, ScopeTypes.Workspace, ScopeTypes.Job };
```

- [ ] **Step 2: Write the failing tests**

Create `api/tests/SurveyorLedger.API.Tests/Services/AccessibleJobsTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Data.Configurations;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// ScopedAccessService.GetAccessibleJobsAsync - the cross-workspace "what jobs can this
/// user open" query backing the dashboard's Jobs list. Broadest-level-wins, deduped.
/// </summary>
public class AccessibleJobsTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IScopedAccessService _access = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
    }

    [Fact]
    public async Task Admin_SeesWorkspaceJobs_TaggedWorkspaceLevel()
    {
        _jobService = GetService<IJobService>();
        _access = GetService<IScopedAccessService>();
        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });

        var jobs = await _access.GetAccessibleJobsAsync(AdminId);

        var result = Assert.Single(jobs);
        Assert.Equal(job.Id, result.JobId);
        Assert.Equal(Constants.ScopeTypes.Workspace, result.AccessScopeType);
        Assert.Equal(WorkspaceId, result.WorkspaceId);
    }

    [Fact]
    public async Task PlainMember_WithNoJobViewAll_AndNoDirectGrant_SeesNoJobs()
    {
        // ClientId from the base fixture is a plain workspace Member - has a Workspace-scope
        // UserAccess row, but Member does not carry job.view_all. This is the exact case the
        // spec's "qualifying grant" definition exists to get right: holding a UserAccess row
        // at a level is NOT the same as holding a qualifying grant at that level.
        _jobService = GetService<IJobService>();
        _access = GetService<IScopedAccessService>();
        await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });

        var jobs = await _access.GetAccessibleJobsAsync(ClientId);

        Assert.Empty(jobs);
    }

    [Fact]
    public async Task DirectJobGrant_WithoutWorkspaceMembership_TaggedJobLevel()
    {
        _jobService = GetService<IJobService>();
        _access = GetService<IScopedAccessService>();
        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });

        var jobOnlyUserId = Guid.NewGuid();
        await Context.Users.AddAsync(new User
        {
            Id = jobOnlyUserId, FirstName = "Job", LastName = "Only", Email = "jobonly@test.local",
            EmailVerified = true, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();
        await GrantService.GrantAsync(jobOnlyUserId, RoleConfiguration.ClientRoleId, Constants.ScopeTypes.Job, job.Id, AdminId);

        var jobs = await _access.GetAccessibleJobsAsync(jobOnlyUserId);

        var result = Assert.Single(jobs);
        Assert.Equal(job.Id, result.JobId);
        Assert.Equal(Constants.ScopeTypes.Job, result.AccessScopeType);
    }

    [Fact]
    public async Task WorkspaceLevelAndDirectGrant_DedupesToWorkspaceLevel()
    {
        // Admin already sees every job via job.view_all; explicitly adding them as a job
        // participant too must not produce a duplicate row or downgrade the reported level.
        _jobService = GetService<IJobService>();
        _access = GetService<IScopedAccessService>();
        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, job.Id, AdminId, "Surveyor");

        var jobs = await _access.GetAccessibleJobsAsync(AdminId);

        var result = Assert.Single(jobs);
        Assert.Equal(Constants.ScopeTypes.Workspace, result.AccessScopeType);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run (from `api/`): `dotnet test tests/SurveyorLedger.API.Tests --filter AccessibleJobsTests`
Expected: FAIL to compile - `IScopedAccessService.GetAccessibleJobsAsync` doesn't exist yet.

- [ ] **Step 4: Implement `GetAccessibleJobsAsync`**

In `api/src/SurveyorLedger.API/Services/ScopedAccessService.cs`, add to the `IScopedAccessService` interface (near `AccessibleJobIds`):

```csharp
/// <summary>Every job this user can open, across every workspace, tagged with the
/// real Constants.ScopeTypes value the access was found at (broadest wins, deduped).
/// Deliberately not workspace-filtered - see Global Constraints.</summary>
Task<List<AccessibleJob>> GetAccessibleJobsAsync(Guid userId);
```

Add the record just above the interface (same file, matching the existing `MemberScopeGrant`-style record placement in `WorkspaceService.cs`):

```csharp
public record AccessibleJob(
    Guid JobId, string JobNumber, string Title, string Status,
    Guid WorkspaceId, string WorkspaceName, string AccessScopeType);
```

Add the implementation to the `ScopedAccessService` class:

```csharp
/// <summary>
/// Cross-workspace - deliberately not filtered by a single WorkspaceId, unlike every other
/// query in this codebase. This is user-scoped ("what can this caller see"), the same
/// category of exception as WorkspaceService.GetUserWorkspacesAsync and
/// InvitationService.GetMyInvitationsAsync, both of which also span every workspace for the
/// calling user. Every job returned is still independently permission-checked below.
/// </summary>
public async Task<List<AccessibleJob>> GetAccessibleJobsAsync(Guid userId)
{
    // Workspace-level: workspaces where a held role carries job.view_all. A plain
    // Workspace-scope UserAccess row (e.g. Member) does NOT qualify on its own - only a role
    // whose permissions include job.view_all does. See spec's "qualifying grant" definition.
    var workspaceAccesses = await _context.UserAccesses
        .Where(ua => ua.UserId == userId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace)
        .ToListAsync();
    var workspaceRoleIds = workspaceAccesses.Select(a => a.RoleId).Distinct().ToList();
    var viewAllRoleIds = await _context.RolePermissions
        .Include(rp => rp.Permission)
        .Where(rp => workspaceRoleIds.Contains(rp.RoleId) && rp.Permission.Resource == "job" && rp.Permission.Action == "view_all")
        .Select(rp => rp.RoleId)
        .ToListAsync();
    var viewAllWorkspaceIds = workspaceAccesses
        .Where(a => viewAllRoleIds.Contains(a.RoleId))
        .Select(a => a.ScopeId)
        .Distinct()
        .ToList();

    var workspaceLevelJobs = await _context.Jobs
        .Where(j => viewAllWorkspaceIds.Contains(j.WorkspaceId))
        .ToListAsync();
    var claimedJobIds = workspaceLevelJobs.Select(j => j.Id).ToHashSet();

    // Job-level: direct job-scope grants not already claimed above. (Organization level, when
    // it exists, inserts here - broader than Workspace, narrower than nothing above it - as
    // one more block following this same shape: find qualifying grants at that level, add
    // to claimedJobIds, tag Constants.ScopeTypes.Organization.)
    var directJobIds = await AccessibleJobIds(userId).ToListAsync();
    var jobLevelJobs = await _context.Jobs
        .Where(j => directJobIds.Contains(j.Id) && !claimedJobIds.Contains(j.Id))
        .ToListAsync();

    var tagged = workspaceLevelJobs.Select(j => (Job: j, Scope: Constants.ScopeTypes.Workspace))
        .Concat(jobLevelJobs.Select(j => (Job: j, Scope: Constants.ScopeTypes.Job)))
        .ToList();

    var workspaceIds = tagged.Select(t => t.Job.WorkspaceId).Distinct().ToList();
    var workspaceNames = await _context.Workspaces
        .Where(w => workspaceIds.Contains(w.Id))
        .ToDictionaryAsync(w => w.Id, w => w.Name);

    return tagged
        .Select(t => new AccessibleJob(
            t.Job.Id, t.Job.JobNumber, t.Job.Title, t.Job.Status,
            t.Job.WorkspaceId, workspaceNames.GetValueOrDefault(t.Job.WorkspaceId, "Unknown workspace"),
            t.Scope))
        .ToList();
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/SurveyorLedger.API.Tests --filter AccessibleJobsTests`
Expected: 4 tests PASS.

- [ ] **Step 6: Build and commit**

```bash
cd api && dotnet build src/SurveyorLedger.API
git add src/SurveyorLedger.Core/Constants.cs src/SurveyorLedger.API/Services/ScopedAccessService.cs tests/SurveyorLedger.API.Tests/Services/AccessibleJobsTests.cs
git commit -m "feat: add ScopedAccessService.GetAccessibleJobsAsync for cross-workspace job access"
```

---

### Task 2: `JobService.GetAccessibleJobDetailAsync`

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/JobService.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/AccessibleJobsTests.cs` (append)

**Interfaces:**
- Consumes: `IScopedAccessService.EnsureJobAccessAsync(Guid userId, Guid workspaceId, Guid jobId, string action)` (existing).
- Produces: `IJobService.GetAccessibleJobDetailAsync(Guid callerUserId, Guid jobId): Task<(Job Job, string WorkspaceName)>` - consumed by Task 3's controller.

- [ ] **Step 1: Write the failing tests**

Append to `AccessibleJobsTests.cs`:

```csharp
[Fact]
public async Task GetAccessibleJobDetail_JobOnlyUser_ReturnsJobAndWorkspaceName()
{
    _jobService = GetService<IJobService>();
    var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });

    var jobOnlyUserId = Guid.NewGuid();
    await Context.Users.AddAsync(new User
    {
        Id = jobOnlyUserId, FirstName = "Job", LastName = "Only", Email = "jobonly2@test.local",
        EmailVerified = true, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    });
    await Context.SaveChangesAsync();
    await GrantService.GrantAsync(jobOnlyUserId, RoleConfiguration.ClientRoleId, Constants.ScopeTypes.Job, job.Id, AdminId);

    var (result, workspaceName) = await _jobService.GetAccessibleJobDetailAsync(jobOnlyUserId, job.Id);

    Assert.Equal(job.Id, result.Id);
    Assert.Equal("Test Workspace", workspaceName);
}

[Fact]
public async Task GetAccessibleJobDetail_NoAccess_ThrowsForbidden()
{
    _jobService = GetService<IJobService>();
    var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });

    var strangerId = Guid.NewGuid();
    await Context.Users.AddAsync(new User
    {
        Id = strangerId, FirstName = "Stranger", LastName = "Person", Email = "stranger@test.local",
        EmailVerified = true, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    });
    await Context.SaveChangesAsync();

    await Assert.ThrowsAsync<SurveyorLedger.Core.Exceptions.ForbiddenException>(
        () => _jobService.GetAccessibleJobDetailAsync(strangerId, job.Id));
}

[Fact]
public async Task GetAccessibleJobDetail_UnknownJobId_ThrowsNotFound()
{
    _jobService = GetService<IJobService>();

    await Assert.ThrowsAsync<SurveyorLedger.Core.Exceptions.NotFoundException>(
        () => _jobService.GetAccessibleJobDetailAsync(AdminId, Guid.NewGuid()));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SurveyorLedger.API.Tests --filter AccessibleJobsTests`
Expected: FAIL to compile - `IJobService.GetAccessibleJobDetailAsync` doesn't exist yet.

- [ ] **Step 3: Implement `GetAccessibleJobDetailAsync`**

In `api/src/SurveyorLedger.API/Services/JobService.cs`, add to `IJobService`:

```csharp
/// <summary>
/// Cross-workspace single-job fetch for a caller who may not be a workspace member (a
/// job-only grant) - resolves the job's workspace internally instead of taking it as a
/// parameter. Same 404-vs-403 order as GetByIdAsync: unknown job -> NotFoundException,
/// real job with no access -> ForbiddenException (via EnsureJobAccessAsync).
/// </summary>
Task<(Job Job, string WorkspaceName)> GetAccessibleJobDetailAsync(Guid callerUserId, Guid jobId);
```

Add the implementation to the `JobService` class (near `GetByIdAsync`):

```csharp
public async Task<(Job Job, string WorkspaceName)> GetAccessibleJobDetailAsync(Guid callerUserId, Guid jobId)
{
    var job = await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId)
        ?? throw new NotFoundException("Job not found");

    await _access.EnsureJobAccessAsync(callerUserId, job.WorkspaceId, jobId, "view");

    var workspaceName = await _context.Workspaces
        .Where(w => w.Id == job.WorkspaceId)
        .Select(w => w.Name)
        .FirstOrDefaultAsync() ?? "Unknown workspace";

    return (job, workspaceName);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SurveyorLedger.API.Tests --filter AccessibleJobsTests`
Expected: 7 tests PASS (4 from Task 1 + 3 new).

- [ ] **Step 5: Build and commit**

```bash
cd api && dotnet build src/SurveyorLedger.API
git add src/SurveyorLedger.API/Services/JobService.cs tests/SurveyorLedger.API.Tests/Services/AccessibleJobsTests.cs
git commit -m "feat: add JobService.GetAccessibleJobDetailAsync for cross-workspace job fetch"
```

---

### Task 3: `GET /api/jobs/mine` and `GET /api/jobs/{jobId}` endpoints

**Files:**
- Create: `api/src/SurveyorLedger.API/Models/Job/AccessibleJobResponse.cs`
- Create: `api/src/SurveyorLedger.API/Controllers/JobsController.cs`

**Interfaces:**
- Consumes: `IScopedAccessService.GetAccessibleJobsAsync` (Task 1), `IJobService.GetAccessibleJobDetailAsync` (Task 2), `ApiResponse<T>.Ok` (existing, `Models/Responses`).
- Produces: `GET /api/jobs/mine -> ApiResponse<List<AccessibleJobResponse>>`, `GET /api/jobs/{jobId} -> ApiResponse<JobWithWorkspaceResponse>` - consumed by Task 4's Angular service.

- [ ] **Step 1: Add response DTOs**

Create `api/src/SurveyorLedger.API/Models/Job/AccessibleJobResponse.cs`:

```csharp
namespace SurveyorLedger.API.Models.Job;

/// <summary>One row of the cross-workspace "jobs I can open" list (GET /api/jobs/mine).</summary>
public class AccessibleJobResponse
{
    public Guid JobId { get; set; }
    public required string JobNumber { get; set; }
    public required string Title { get; set; }
    public required string Status { get; set; }
    public Guid WorkspaceId { get; set; }
    public required string WorkspaceName { get; set; }

    /// <summary>The real scope-type value (Constants.ScopeTypes) the access was found at - "Workspace" or "Job" today, "Organization" later.</summary>
    public required string AccessScopeType { get; set; }
}

/// <summary>A single job plus its workspace context, for a caller who may not be a workspace member (GET /api/jobs/{jobId}).</summary>
public class JobWithWorkspaceResponse
{
    public Guid JobId { get; set; }
    public required string JobNumber { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string Status { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid WorkspaceId { get; set; }
    public required string WorkspaceName { get; set; }
}
```

- [ ] **Step 2: Add the controller**

Create `api/src/SurveyorLedger.API/Controllers/JobsController.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;

namespace SurveyorLedger.API.Controllers
{
    /// <summary>
    /// Top-level (not nested under /workspace/{id}) - both routes here are user-scoped,
    /// not workspace-scoped, so they don't fit the nested pattern JobController uses.
    /// </summary>
    [ApiController]
    [Route("api/jobs")]
    [Authorize]
    public class JobsController : ControllerBase
    {
        private readonly IScopedAccessService _access;
        private readonly IJobService _jobService;

        public JobsController(IScopedAccessService access, IJobService jobService)
        {
            _access = access;
            _jobService = jobService;
        }

        [HttpGet("mine")]
        public async Task<ActionResult<ApiResponse<List<AccessibleJobResponse>>>> GetMine()
        {
            var userId = CallerId();
            var jobs = await _access.GetAccessibleJobsAsync(userId);

            var response = jobs.Select(j => new AccessibleJobResponse
            {
                JobId = j.JobId,
                JobNumber = j.JobNumber,
                Title = j.Title,
                Status = j.Status,
                WorkspaceId = j.WorkspaceId,
                WorkspaceName = j.WorkspaceName,
                AccessScopeType = j.AccessScopeType
            }).ToList();

            return Ok(ApiResponse<List<AccessibleJobResponse>>.Ok(response));
        }

        [HttpGet("{jobId}")]
        public async Task<ActionResult<ApiResponse<JobWithWorkspaceResponse>>> GetById(Guid jobId)
        {
            var userId = CallerId();
            var (job, workspaceName) = await _jobService.GetAccessibleJobDetailAsync(userId, jobId);

            return Ok(ApiResponse<JobWithWorkspaceResponse>.Ok(new JobWithWorkspaceResponse
            {
                JobId = job.Id,
                JobNumber = job.JobNumber,
                Title = job.Title,
                Description = job.Description,
                Status = job.Status,
                CreatedBy = job.CreatedBy,
                CreatedAt = job.CreatedAt,
                UpdatedAt = job.UpdatedAt,
                WorkspaceId = job.WorkspaceId,
                WorkspaceName = workspaceName
            }));
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
    }
}
```

- [ ] **Step 3: Build**

```bash
cd api && dotnet build src/SurveyorLedger.API
```
Expected: 0 errors. (No new tests here - both endpoints are thin wrappers over Task 1/2's already-tested service methods; correctness is verified end-to-end in Task 8.)

- [ ] **Step 4: Commit**

```bash
git add src/SurveyorLedger.API/Models/Job/AccessibleJobResponse.cs src/SurveyorLedger.API/Controllers/JobsController.cs
git commit -m "feat: add GET /api/jobs/mine and GET /api/jobs/{jobId} endpoints"
```

---

### Task 4: Angular `JobService` additions

**Files:**
- Modify: `ui/src/app/core/job.service.ts`
- Test: `ui/src/app/core/job.service.spec.ts`

**Interfaces:**
- Consumes: `GET /api/jobs/mine`, `GET /api/jobs/{jobId}` (Task 3).
- Produces: `AccessibleJob` interface, `JobWithWorkspace` interface, `JobService.getMine(): Observable<AccessibleJob[]>`, `JobService.getStandalone(jobId: string): Observable<JobWithWorkspace>` - consumed by Task 5 (guard) and Task 7 (dashboard).

- [ ] **Step 1: Write the failing tests**

Open `ui/src/app/core/job.service.spec.ts`, add near the other `describe`/`it` blocks (same file, same `TestBed` setup already there):

```typescript
it('getMine() gets the cross-workspace jobs list', () => {
  service.getMine().subscribe();
  const req = httpMock.expectOne(`${environment.apiBaseUrl}/jobs/mine`);
  expect(req.request.method).toBe('GET');
  req.flush({ success: true, data: [] });
});

it('getStandalone() gets a job by id with no workspace prefix', () => {
  service.getStandalone('j1').subscribe();
  const req = httpMock.expectOne(`${environment.apiBaseUrl}/jobs/j1`);
  expect(req.request.method).toBe('GET');
  req.flush({ success: true, data: {} });
});
```

(If `environment` isn't already imported in the spec file, add `import { environment } from '../../environments/environment';` alongside the existing imports.)

- [ ] **Step 2: Run tests to verify they fail**

Run (from `ui/`): `npx ng test --include '**/job.service.spec.ts'`
Expected: FAIL - `getMine`/`getStandalone` don't exist on `JobService`.

- [ ] **Step 3: Implement**

In `ui/src/app/core/job.service.ts`, add interfaces near the top (alongside `Job`, `JobParticipant`):

```typescript
export interface AccessibleJob {
  jobId: string;
  jobNumber: string;
  title: string;
  status: string;
  workspaceId: string;
  workspaceName: string;
  accessScopeType: string;
}

export interface JobWithWorkspace extends Job {
  workspaceId: string;
  workspaceName: string;
}
```

Add methods to the `JobService` class (near `list()`):

```typescript
/** Every job this user can open, across every workspace - backs the dashboard's Jobs section. */
getMine(): Observable<AccessibleJob[]> {
  return this.http
    .get<ApiResponse<AccessibleJob[]>>(`${environment.apiBaseUrl}/jobs/mine`)
    .pipe(map(res => res.data));
}

/** Fetch a single job with no workspace prefix - for a caller who may not be a workspace member. */
getStandalone(jobId: string): Observable<JobWithWorkspace> {
  return this.http
    .get<ApiResponse<JobWithWorkspace>>(`${environment.apiBaseUrl}/jobs/${jobId}`)
    .pipe(map(res => res.data));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx ng test --include '**/job.service.spec.ts'`
Expected: all tests PASS (existing + 2 new).

- [ ] **Step 5: Typecheck and commit**

```bash
cd ui && npx tsc --noEmit -p tsconfig.app.json
git add src/app/core/job.service.ts src/app/core/job.service.spec.ts
git commit -m "feat: add JobService.getMine and getStandalone"
```

---

### Task 5: `jobAccessGuard` + minimal job-only route

**Files:**
- Create: `ui/src/app/core/job-access.guard.ts`
- Modify: `ui/src/app/pages/job/job-detail.component.ts:601`
- Modify: `ui/src/app/app.routes.ts`

**Interfaces:**
- Consumes: `JobService.getStandalone` (Task 4), `CurrentWorkspaceService.clear()` (existing).
- Produces: `jobAccessGuard: CanActivateFn`, route `app/job/:workspaceId/:jobId` - consumed by Task 6 (redirect target).

- [ ] **Step 1: Create the guard**

Create `ui/src/app/core/job-access.guard.ts`:

```typescript
import { inject } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { JobService } from './job.service';
import { CurrentWorkspaceService } from './current-workspace.service';

/**
 * Guards /app/job/:workspaceId/:jobId - the minimal-shell route for a job-only grant (no
 * workspace membership). Deliberately does NOT call CurrentWorkspaceService.set() the way
 * workspaceResolveGuard does: SidebarComponent renders the full workspace tab list
 * (Overview/Land/Billing/Members) off any truthy currentWorkspace.current(), with no
 * per-tab permission check - setting it here would leak that nav to someone who can't use
 * most of it. Leaving it cleared keeps the sidebar in its no-workspace state instead.
 */
export const jobAccessGuard: CanActivateFn = (route) => {
  const jobService = inject(JobService);
  const currentWorkspace = inject(CurrentWorkspaceService);
  const router = inject(Router);
  const jobId = route.paramMap.get('jobId')!;

  currentWorkspace.clear();

  return jobService.getStandalone(jobId).pipe(
    map(() => true),
    catchError(() =>
      of(router.createUrlTree(['/app/dashboard'], { queryParams: { error: 'job-not-found' } }))
    )
  );
};
```

- [ ] **Step 2: Add the `workspaceId` fallback to `JobDetailComponent`**

In `ui/src/app/pages/job/job-detail.component.ts`, find line 601 (`ngOnInit`):

```typescript
this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
```

Replace with:

```typescript
// Falls back to the route param for /app/job/:workspaceId/:jobId (job-only access, no
// CurrentWorkspaceService set - see jobAccessGuard) - the normal workspace-shell route
// still resolves via CurrentWorkspaceService as before.
this.workspaceId = this.currentWorkspace.current()?.workspaceId
  ?? this.route.snapshot.paramMap.get('workspaceId') ?? '';
```

- [ ] **Step 3: Register the route**

In `ui/src/app/app.routes.ts`:
- Add import: `import { jobAccessGuard } from './core/job-access.guard';`
- Add a route as a sibling to `dashboard` (inside the `app` children array, same level, so it gets `AppShellComponent`'s top bar but not the workspace sidebar tabs):

```typescript
{ path: 'job/:workspaceId/:jobId', component: JobDetailComponent, canActivate: [jobAccessGuard], canDeactivate: [unsavedChangesGuard] },
```

- [ ] **Step 4: Typecheck**

```bash
cd ui && npx tsc --noEmit -p tsconfig.app.json
```
Expected: 0 errors. (Manual browser verification of the full guard behavior happens in Task 8 - this route touches shared shell/sidebar state that's easier to eyeball than unit-test given no existing guard-spec pattern in this codebase.)

- [ ] **Step 5: Commit**

```bash
git add src/app/core/job-access.guard.ts src/app/pages/job/job-detail.component.ts src/app/app.routes.ts
git commit -m "feat: add job-only route /app/job/:workspaceId/:jobId with minimal shell"
```

---

### Task 6: Invite-accept redirect to the new route

**Files:**
- Modify: `ui/src/app/pages/invite/accept-invite.component.ts`

**Interfaces:**
- Consumes: route `app/job/:workspaceId/:jobId` (Task 5), `AcceptInvitationResult.jobId`/`workspaceId` (existing, from earlier session's work).

- [ ] **Step 1: Update the redirect**

In `ui/src/app/pages/invite/accept-invite.component.ts`, find the `accept()` method's success handler (added earlier this session):

```typescript
next: (result) => {
  if (result.jobId) {
    this.router.navigate(['/app/workspace', result.workspaceId, 'jobs', result.jobId]);
  } else {
    this.router.navigate(['/app/workspace', result.workspaceId]);
  }
},
```

Replace the `if` branch's target - a job-scope accept was never reachable at the workspace-prefixed job route for a job-only accepter (that's the whole bug this plan exists to close), so route to the new minimal-shell route instead:

```typescript
next: (result) => {
  if (result.jobId) {
    this.router.navigate(['/app/job', result.workspaceId, result.jobId]);
  } else {
    this.router.navigate(['/app/workspace', result.workspaceId]);
  }
},
```

- [ ] **Step 2: Typecheck**

```bash
cd ui && npx tsc --noEmit -p tsconfig.app.json
```

- [ ] **Step 3: Commit**

```bash
git add src/app/pages/invite/accept-invite.component.ts
git commit -m "fix: route job-scope invite accept to the job-only route, not the workspace shell"
```

---

### Task 7: Dashboard - Jobs section, view toggle, filters

**Files:**
- Modify: `ui/src/app/pages/dashboard/dashboard.component.ts`

**Interfaces:**
- Consumes: `JobService.getMine()` (Task 4), `AccessibleJob` (Task 4), existing `WorkspaceService.list()`, `CurrentWorkspaceService`.

- [ ] **Step 1: Replace the component**

Full replacement of `ui/src/app/pages/dashboard/dashboard.component.ts`:

```typescript
import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { WorkspaceService, Workspace } from '../../core/workspace.service';
import { JobService, AccessibleJob } from '../../core/job.service';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';
import { CreateWorkspaceModalComponent } from '../workspace/create-modal/create-modal.component';

type ViewMode = 'both' | 'jobs' | 'workspace';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, CreateWorkspaceModalComponent],
  template: `
    <div class="p-lg max-w-4xl mx-auto">
      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Your workspaces</h1>
        <div class="flex items-center gap-sm">
          <div class="flex rounded border border-neutral-200 overflow-hidden text-xs">
            <button type="button" class="px-md py-xs" [class.bg-primary-50]="viewMode() === 'both'" [class.text-primary-600]="viewMode() === 'both'" (click)="viewMode.set('both')">Both</button>
            <button type="button" class="px-md py-xs border-l border-neutral-200" [class.bg-primary-50]="viewMode() === 'jobs'" [class.text-primary-600]="viewMode() === 'jobs'" (click)="viewMode.set('jobs')">Jobs</button>
            <button type="button" class="px-md py-xs border-l border-neutral-200" [class.bg-primary-50]="viewMode() === 'workspace'" [class.text-primary-600]="viewMode() === 'workspace'" (click)="viewMode.set('workspace')">Workspace</button>
          </div>
          <button class="btn-primary" (click)="modalOpen.set(true)">New workspace</button>
        </div>
      </div>

      @if (notFoundError()) {
        <div class="mb-lg text-sm text-primary-600 bg-primary-50 border border-primary-100 rounded px-md py-sm">
          That workspace couldn't be found, or you don't have access to it.
        </div>
      }

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else {
        @if (viewMode() !== 'jobs') {
          @if (workspaces().length === 0) {
            <div class="card text-center text-sm text-neutral-500">No workspaces yet. Create one to get started.</div>
          } @else {
            <div class="grid gap-md sm:grid-cols-2">
              @for (workspace of workspaces(); track workspace.workspaceId) {
                <button type="button" class="card text-left hover:border-primary-300 transition-colors" (click)="openWorkspace(workspace)">
                  <div class="flex items-center justify-between">
                    <span class="font-medium text-neutral-900">{{ workspace.name }}</span>
                    <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ workspace.tier }}</span>
                  </div>
                  @if (workspace.description) {
                    <p class="text-sm text-neutral-600 mt-xs">{{ workspace.description }}</p>
                  }
                  <p class="text-xs text-neutral-500 mt-sm">Role: {{ workspace.roles.join(', ') }}</p>
                </button>
              }
            </div>
          }
        }

        @if (viewMode() === 'both') {
          <h2 class="text-sm font-semibold text-neutral-900 mt-xl mb-md">Jobs (direct access)</h2>
          @if (directAccessJobs().length === 0) {
            <div class="card text-center text-sm text-neutral-500">No jobs outside your workspaces.</div>
          } @else {
            <div class="grid gap-sm">
              @for (job of directAccessJobs(); track job.jobId) {
                <button type="button" class="card text-left hover:border-primary-300 transition-colors flex items-center justify-between" (click)="openJob(job)">
                  <div>
                    <span class="font-medium text-neutral-900">{{ job.jobNumber }} · {{ job.title }}</span>
                    <p class="text-xs text-neutral-500">{{ job.workspaceName }}</p>
                  </div>
                  <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ job.status }}</span>
                </button>
              }
            </div>
          }
        }

        @if (viewMode() === 'jobs') {
          <div class="flex flex-wrap gap-sm mb-md">
            <select class="input-field py-xs text-xs w-40" [(ngModel)]="workspaceFilter">
              <option value="">All workspaces</option>
              @for (name of availableWorkspaceNames(); track name) {
                <option [value]="name">{{ name }}</option>
              }
            </select>
            <select class="input-field py-xs text-xs w-32" [(ngModel)]="statusFilter">
              <option value="">All statuses</option>
              @for (status of availableStatuses(); track status) {
                <option [value]="status">{{ status }}</option>
              }
            </select>
            <select class="input-field py-xs text-xs w-36" [(ngModel)]="accessTypeFilter">
              <option value="">All access types</option>
              @for (type of availableAccessTypes(); track type) {
                <option [value]="type">{{ type }}</option>
              }
            </select>
          </div>
          @if (filteredJobs().length === 0) {
            <div class="card text-center text-sm text-neutral-500">No jobs match these filters.</div>
          } @else {
            <div class="grid gap-sm">
              @for (job of filteredJobs(); track job.jobId) {
                <button type="button" class="card text-left hover:border-primary-300 transition-colors flex items-center justify-between" (click)="openJob(job)">
                  <div>
                    <span class="font-medium text-neutral-900">{{ job.jobNumber }} · {{ job.title }}</span>
                    <p class="text-xs text-neutral-500">{{ job.workspaceName }} · {{ job.accessScopeType }}</p>
                  </div>
                  <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ job.status }}</span>
                </button>
              }
            </div>
          }
        }
      }
    </div>

    @if (modalOpen()) {
      <app-create-workspace-modal (cancel)="modalOpen.set(false)" (created)="onCreated($event)" />
    }
  `
})
export class DashboardComponent implements OnInit {
  workspaces = signal<Workspace[]>([]);
  jobs = signal<AccessibleJob[]>([]);
  loading = signal(true);
  modalOpen = signal(false);
  notFoundError = signal(false);
  viewMode = signal<ViewMode>('both');

  workspaceFilter = '';
  statusFilter = '';
  accessTypeFilter = '';

  /** Job-level-only jobs - no workspace access, shown separately below the Workspaces section. */
  directAccessJobs = computed(() => this.jobs().filter(j => j.accessScopeType === 'Job'));

  availableWorkspaceNames = computed(() => [...new Set(this.jobs().map(j => j.workspaceName))].sort());
  availableStatuses = computed(() => [...new Set(this.jobs().map(j => j.status))].sort());
  availableAccessTypes = computed(() => [...new Set(this.jobs().map(j => j.accessScopeType))].sort());

  filteredJobs = computed(() =>
    this.jobs().filter(j =>
      (!this.workspaceFilter || j.workspaceName === this.workspaceFilter) &&
      (!this.statusFilter || j.status === this.statusFilter) &&
      (!this.accessTypeFilter || j.accessScopeType === this.accessTypeFilter)
    )
  );

  constructor(
    private workspaceService: WorkspaceService,
    private jobService: JobService,
    private currentWorkspace: CurrentWorkspaceService,
    private route: ActivatedRoute,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.currentWorkspace.clear();
    this.notFoundError.set(
      this.route.snapshot.queryParamMap.get('error') === 'workspace-not-found' ||
      this.route.snapshot.queryParamMap.get('error') === 'job-not-found'
    );
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    let remaining = 2;
    const done = () => { if (--remaining === 0) this.loading.set(false); };

    this.workspaceService.list().subscribe({
      next: (workspaces) => { this.workspaces.set(workspaces); done(); },
      error: () => done()
    });
    this.jobService.getMine().subscribe({
      next: (jobs) => { this.jobs.set(jobs); done(); },
      error: () => done()
    });
  }

  openWorkspace(workspace: Workspace): void {
    this.router.navigate(['/app/workspace', workspace.workspaceId]);
  }

  /** accessScopeType === 'Job' (leaf-level, nothing above confirmed) -> minimal job-only
   * route. Anything else ('Workspace' today, 'Organization' later) -> the full workspace
   * shell - same leaf-vs-not-leaf rule as the spec's "Scaling mechanism". */
  openJob(job: AccessibleJob): void {
    if (job.accessScopeType === 'Job') {
      this.router.navigate(['/app/job', job.workspaceId, job.jobId]);
    } else {
      this.router.navigate(['/app/workspace', job.workspaceId, 'jobs', job.jobId]);
    }
  }

  onCreated(workspace: Workspace): void {
    this.modalOpen.set(false);
    this.router.navigate(['/app/workspace', workspace.workspaceId]);
  }
}
```

- [ ] **Step 2: Typecheck**

```bash
cd ui && npx tsc --noEmit -p tsconfig.app.json
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/app/pages/dashboard/dashboard.component.ts
git commit -m "feat: dashboard Jobs section, view toggle, and workspace/status/access-type filters"
```

---

### Task 8: End-to-end verification

No new files - this is the manual pass, following the same repro pattern used earlier this session (register a fresh admin, workspace, job, job-only invite, accept, inspect).

- [ ] **Step 1: Start both servers**

Use the Browser pane's `preview_start` with `name: "SurveyorLedger API"` and `name: "SurveyorLedger UI"` (from `.claude/launch.json`, already configured).

- [ ] **Step 2: Register a fresh admin, create a workspace and a job**

Register (temporarily add the same `DEV-ONLY` OTP debug log to `AuthService.VerifyOtpAsync` used earlier this session, revert after), log in, create a workspace, create a job.

- [ ] **Step 3: Invite a brand-new person to the job only**

Via `add-person-widget`'s invite-by-email fallback on the job page (Client role). Complete their account via the invite link, log in as them, click Accept.

- [ ] **Step 4: Verify the redirect lands correctly**

Confirm the URL after Accept is `/app/job/{workspaceId}/{jobId}` (not `/app/dashboard?error=...`), and the job page actually renders (title, People, Milestones sections visible) - this is the guard bug (task #31 from earlier this session) actually closed out.

- [ ] **Step 5: Verify the sidebar stayed minimal**

On that job page, confirm the left sidebar shows only Dashboard/Profile/Invitations/Logout - NOT Overview/Jobs/Land/Billing/Members/Roles. This is the leak `jobAccessGuard`'s design was built to avoid; confirm it actually didn't happen.

- [ ] **Step 6: Verify the dashboard's Jobs section**

Log back in as the admin, go to the dashboard. Confirm: the job-only invitee does NOT need re-checking (dashboard is admin's own view) - instead, log in as the job-only invitee and check their dashboard shows "Jobs (direct access)" with the job listed, `Both`/`Jobs`/`Workspace` toggle works, and clicking the job routes to `/app/job/...` correctly.

- [ ] **Step 7: Regression check**

As the admin (full workspace member), confirm their dashboard still shows the Workspaces grid as before, and clicking a job from the flattened "Jobs" view (toggle to `Jobs`) for a job they access via workspace membership routes to `/app/workspace/.../jobs/...` (full shell), not the minimal route.

- [ ] **Step 8: Clean up**

Revert the temporary `DEV-ONLY` OTP debug log. Delete all test data created (workspace, job, both users) via sqlcmd, respecting FK order (AuditLogs, UserAccesses, Invitations, Jobs, Subscriptions, Workspaces, AuthTokens, Users) - same pattern used for cleanup earlier this session. Rebuild the API. Stop both preview servers.

---

## Self-Review Notes

- **Spec coverage**: `Constants.ScopeHierarchy` (Task 1), `AccessScopeType` via real `ScopeTypes` values (Task 1), broadest-first dedup (Task 1, tested), qualifying-grant definition (Task 1, tested via `PlainMember_...` test), `GET /api/jobs/{jobId}` 404-vs-403 parity (Task 2, tested), not-a-tenant-isolation-violation comment (Task 1 code comment), no schema changes (confirmed - no migration task exists), dashboard two-section + toggle + three filters (Task 7), `/app/job/:jobId`-family route with minimal shell (Task 5, with the sidebar-leak risk explicitly caught and avoided), invite-accept redirect fix (Task 6).
- **Deviation from the spec's literal route** `/app/job/:jobId` **to** `/app/job/:workspaceId/:jobId`: discovered during planning that reusing `CurrentWorkspaceService` (the spec's implicit assumption for how `JobDetailComponent` would get `workspaceId`) would leak the full workspace sidebar, since `SidebarComponent` shows it off any truthy `currentWorkspace.current()`. Passing `workspaceId` via the route instead avoids touching that shared signal at all. Functionally equivalent, lower risk, no `SidebarComponent` changes needed.
