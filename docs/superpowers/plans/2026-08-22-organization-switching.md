# Organization Switching Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an Azure-style organization switcher UI on top of the already-shipped Organization backend, while extending the existing declarative RBAC ancestor-chain engine so every user is guaranteed at least one organization (invited members currently never get one).

**Architecture:** Backend: extend `AssignmentPolicy`/`ScopeParentType`/`IScopeLinkProvider` (the engine already anticipates an Organization level) to reach Organization from any grant, then backfill pre-existing data and expose `organizationId` on the workspace/job DTOs. Frontend: a persisted `CurrentOrganizationService` (mirrors the existing `CurrentWorkspaceService`), a topbar quick-switcher, a full `/app/organizations` page, and org-scoping of the dashboard.

**Tech Stack:** .NET 9, EF Core 9, SQL Server LocalDB, Casbin.NET 2.0, Angular 21 (standalone components, signals).

## Global Constraints

- Migrations generated via `dotnet ef migrations add`, never hand-edited (hook-enforced).
- Every tenant-scoped query filters by `WorkspaceId`/`OrganizationId` as appropriate — no exceptions.
- Full test suite only at the start (clean baseline) and end (pre-merge gate); every other verification is scoped (`dotnet test --filter ClassName`, `ng test --include component-spec`).
- Client and Finance roles must never receive `WorkspaceMember` (workspace internals stay closed to externals) but must always receive `OrgMember` on invite-accept.
- `ResolveTopAncestorAsync`'s use in `JobService` (invitation targeting) must remain byte-identical in output to today — this is a regression guard, verified by a dedicated test.
- Reuse existing patterns: `AssignmentPolicy`/`ScopeParentType`/`IScopeLinkProvider` for the RBAC extension (not a bespoke grant in `InvitationService`); `CurrentWorkspaceService`/`WorkspaceService` (Angular) as the template for the Organization equivalents; `WorkspaceSettingsComponent` as the template for organization settings.

---

### Task 1: Register Workspace → Organization as a resolvable scope link

**Files:**
- Modify: `api/src/SurveyorLedger.Data/Configurations/ScopeParentTypeConfiguration.cs`
- Create: `api/src/SurveyorLedger.API/Services/WorkspaceOrganizationScopeLinkProvider.cs`
- Modify: `api/src/SurveyorLedger.API/Program.cs`
- Modify: `api/tests/SurveyorLedger.API.Tests/Services/WorkspaceIntegrationTestBase.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/ScopeIdResolverTests.cs`

**Interfaces:**
- Produces: `IScopeIdResolver.GetParentIdAsync("Workspace", workspaceId)` now resolves to the workspace's `OrganizationId` instead of `null`; `IScopeIdResolver.GetParentScopeType("Workspace")` now returns `"Organization"` instead of `null`.

`WorkspaceIntegrationTestBase.InitializeAsync` already registers `IScopeLinkProvider, JobWorkspaceScopeLinkProvider` and `IScopeIdResolver, ScopeIdResolver` directly (not per-test-class) — every test class deriving from it, including `ScopeIdResolverTests`, already gets these. The new provider registration goes in that same base-class list, not in an individual test file.

- [ ] **Step 1: Register the new provider in the shared test base**

In `WorkspaceIntegrationTestBase.cs`'s `InitializeAsync`, add the new provider registration alongside the existing one:

```csharp
services.AddScoped<IScopeLinkProvider, JobWorkspaceScopeLinkProvider>();
services.AddScoped<IScopeLinkProvider, WorkspaceOrganizationScopeLinkProvider>();
services.AddScoped<IScopeIdResolver, ScopeIdResolver>();
```

- [ ] **Step 2: Write the failing test**

Add to `api/tests/SurveyorLedger.API.Tests/Services/ScopeIdResolverTests.cs`:

```csharp
[Fact]
public async Task GetParentIdAsync_Workspace_ResolvesToOrganizationId()
{
    var resolver = GetService<IScopeIdResolver>();

    var parentId = await resolver.GetParentIdAsync(Constants.ScopeTypes.Workspace, WorkspaceId);

    Assert.NotNull(parentId);
}

[Fact]
public void GetParentScopeType_Workspace_ReturnsOrganization()
{
    var resolver = GetService<IScopeIdResolver>();

    var parentType = resolver.GetParentScopeType(Constants.ScopeTypes.Workspace);

    Assert.Equal(Constants.ScopeTypes.Organization, parentType);
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter ScopeIdResolverTests` (from `api/`)
Expected: FAIL — compile error (`WorkspaceOrganizationScopeLinkProvider` doesn't exist yet).

- [ ] **Step 4: Add the ScopeParentType row**

In `ScopeParentTypeConfiguration.cs`, add a second row to the existing `HasData(...)` call:

```csharp
builder.HasData(
    new ScopeParentType { ScopeType = Constants.ScopeTypes.Job, ParentScopeType = Constants.ScopeTypes.Workspace },
    new ScopeParentType { ScopeType = Constants.ScopeTypes.Workspace, ParentScopeType = Constants.ScopeTypes.Organization }
);
```

(This table is metadata only — it documents the hierarchy shape but isn't read by `ScopeIdResolver`, which dispatches purely off registered `IScopeLinkProvider`s. Keeping it in sync is still correct practice, matching the existing Job row.)

- [ ] **Step 5: Implement the provider**

`api/src/SurveyorLedger.API/Services/WorkspaceOrganizationScopeLinkProvider.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.Core;
using SurveyorLedger.Data;

namespace SurveyorLedger.API.Services;

/// <summary>A Workspace's parent is its Organization.</summary>
public class WorkspaceOrganizationScopeLinkProvider : IScopeLinkProvider
{
    private readonly ApplicationDbContext _context;

    public WorkspaceOrganizationScopeLinkProvider(ApplicationDbContext context)
    {
        _context = context;
    }

    public string ChildScopeType => Constants.ScopeTypes.Workspace;
    public string ParentScopeType => Constants.ScopeTypes.Organization;

    public Task<Guid?> GetParentIdAsync(Guid childScopeId) =>
        _context.Workspaces
            .AsNoTracking()
            .Where(w => w.Id == childScopeId)
            .Select(w => (Guid?)w.OrganizationId)
            .FirstOrDefaultAsync();

    public Task<List<Guid>> GetChildIdsAsync(Guid parentScopeId) =>
        _context.Workspaces
            .AsNoTracking()
            .Where(w => w.OrganizationId == parentScopeId)
            .Select(w => w.Id)
            .ToListAsync();
}
```

- [ ] **Step 6: Register it in Program.cs**

In `Program.cs`, add the new provider registration right after the existing one:

```csharp
builder.Services.AddScoped<IScopeLinkProvider, JobWorkspaceScopeLinkProvider>();
builder.Services.AddScoped<IScopeLinkProvider, WorkspaceOrganizationScopeLinkProvider>();
builder.Services.AddScoped<IScopeIdResolver, ScopeIdResolver>();
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test --filter ScopeIdResolverTests` (from `api/`)
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add api/src/SurveyorLedger.Data/Configurations/ScopeParentTypeConfiguration.cs api/src/SurveyorLedger.API/Services/WorkspaceOrganizationScopeLinkProvider.cs api/src/SurveyorLedger.API/Program.cs api/tests/SurveyorLedger.API.Tests/Services/WorkspaceIntegrationTestBase.cs api/tests/SurveyorLedger.API.Tests/Services/ScopeIdResolverTests.cs
git commit -m "feat: register Workspace-to-Organization as a resolvable scope link"
```

---

### Task 2: Rewrite the ancestor-chain walk to a scope-type-keyed grants map

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/UserAccessGrantService.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/UserAccessGrantServiceTests.cs`

**Interfaces:**
- Consumes: `IScopeIdResolver.GetParentIdAsync`/`GetParentScopeType` (Task 1).
- Produces: `AssignmentPolicy.RulesJson` now expected in the shape `{"grants":{"<ScopeType>":"<RoleId guid string>"}}` (a map, scope type absent from the map = transit hop, resolved but nothing granted there) instead of the old `{"ancestors":[{"scopeType":...,"grantRoleId":...}]}` array. `IUserAccessGrantService.ResolveTopAncestorAsync` gains a new optional parameter: `Task<(string ScopeType, Guid ScopeId, Guid RoleId)?> ResolveTopAncestorAsync(string scopeType, Guid scopeId, Guid roleId, string? stopAtScopeType = null)`.

This task changes the *engine* only — no `AssignmentPolicy` seed data changes yet (Task 3 does that). Write the tests against a policy created inline in the test itself, not against the real seeded `FullChain`/`SingleScope` rows, so this task is independently verifiable before touching seed data.

- [ ] **Step 1: Write the failing tests**

Add to `api/tests/SurveyorLedger.API.Tests/Services/UserAccessGrantServiceTests.cs`. `WorkspaceIntegrationTestBase` already registers `IUserAccessGrantService` (exposed as the protected `GrantService` property, used directly below — no `GetService<IUserAccessGrantService>()` call needed) and, after Task 1, both `IScopeLinkProvider`s:

```csharp
[Fact]
public async Task GrantAsync_JobStart_GrantsMapWalksToWorkspaceThenOrganization()
{
    // Arrange: a throwaway role using the grants-map shape, scoped to Job, granted at Job.
    var orgId = Guid.NewGuid();
    await Context.Organizations.AddAsync(new Organization
    {
        Id = orgId, Name = "Walk Test Org", OwnerId = AdminId, IsActive = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    });
    var workspace = await Context.Workspaces.SingleAsync(w => w.Id == WorkspaceId);
    workspace.OrganizationId = orgId;

    var jobId = Guid.NewGuid();
    await Context.Jobs.AddAsync(new Job
    {
        Id = jobId, WorkspaceId = WorkspaceId, JobNumber = "J-1", Title = "Walk Test Job",
        Status = "Open", CreatedBy = AdminId, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    });

    var policyId = Guid.NewGuid();
    await Context.AssignmentPolicies.AddAsync(new AssignmentPolicy
    {
        Id = policyId, Name = "TestFullChain",
        RulesJson = $"{{\"grants\":{{\"{Constants.ScopeTypes.Workspace}\":\"{RoleConfiguration.WorkspaceMemberRoleId}\",\"{Constants.ScopeTypes.Organization}\":\"{RoleConfiguration.OrgMemberRoleId}\"}}}}"
    });
    var testRoleId = Guid.NewGuid();
    await Context.Roles.AddAsync(new Role
    {
        Id = testRoleId, Name = "TestJobRole", Description = "test", IsSystem = false, PolicyId = policyId,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    });
    await Context.SaveChangesAsync();

    var target = await CreateUserAccountAsync("Walk", "Target", "walk-target@test.local");

    // Act
    await GrantService.GrantAsync(target, testRoleId, Constants.ScopeTypes.Job, jobId, AdminId);

    // Assert: chain-granted at both Workspace and Organization.
    var workspaceGrant = await Context.UserAccesses.SingleOrDefaultAsync(ua =>
        ua.UserId == target && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == WorkspaceId && ua.IsActive);
    Assert.NotNull(workspaceGrant);
    Assert.Equal(RoleConfiguration.WorkspaceMemberRoleId, workspaceGrant!.RoleId);

    var orgGrant = await Context.UserAccesses.SingleOrDefaultAsync(ua =>
        ua.UserId == target && ua.ScopeType == Constants.ScopeTypes.Organization && ua.ScopeId == orgId && ua.IsActive);
    Assert.NotNull(orgGrant);
    Assert.Equal(RoleConfiguration.OrgMemberRoleId, orgGrant!.RoleId);
}

[Fact]
public async Task GrantAsync_WorkspaceStart_SkipsWorkspaceHopGrantsOnlyOrganization()
{
    // The same policy, but granted directly AT Workspace scope (e.g. AddMemberRoleAsync) -
    // must not create a corrupted UserAccess row at Workspace using the Organization's guid.
    var orgId = Guid.NewGuid();
    await Context.Organizations.AddAsync(new Organization
    {
        Id = orgId, Name = "Walk Test Org 2", OwnerId = AdminId, IsActive = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    });
    var workspace = await Context.Workspaces.SingleAsync(w => w.Id == WorkspaceId);
    workspace.OrganizationId = orgId;

    var policyId = Guid.NewGuid();
    await Context.AssignmentPolicies.AddAsync(new AssignmentPolicy
    {
        Id = policyId, Name = "TestFullChain2",
        RulesJson = $"{{\"grants\":{{\"{Constants.ScopeTypes.Workspace}\":\"{RoleConfiguration.WorkspaceMemberRoleId}\",\"{Constants.ScopeTypes.Organization}\":\"{RoleConfiguration.OrgMemberRoleId}\"}}}}"
    });
    var testRoleId = Guid.NewGuid();
    await Context.Roles.AddAsync(new Role
    {
        Id = testRoleId, Name = "TestWorkspaceRole", Description = "test", IsSystem = false, PolicyId = policyId,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    });
    await Context.SaveChangesAsync();

    var target = await CreateUserAccountAsync("NoCorrupt", "Target", "no-corrupt@test.local");

    await GrantService.GrantAsync(target, testRoleId, Constants.ScopeTypes.Workspace, WorkspaceId, AdminId);

    // No row was created at Workspace scope using the org's guid as the scope id.
    var corrupted = await Context.UserAccesses.AnyAsync(ua =>
        ua.UserId == target && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == orgId);
    Assert.False(corrupted);

    var orgGrant = await Context.UserAccesses.SingleOrDefaultAsync(ua =>
        ua.UserId == target && ua.ScopeType == Constants.ScopeTypes.Organization && ua.ScopeId == orgId && ua.IsActive);
    Assert.NotNull(orgGrant);
    Assert.Equal(RoleConfiguration.OrgMemberRoleId, orgGrant!.RoleId);
}

[Fact]
public async Task ResolveTopAncestorAsync_WithStopAtScopeType_CapsWalkAtThatLevel()
{
    var orgId = Guid.NewGuid();
    await Context.Organizations.AddAsync(new Organization
    {
        Id = orgId, Name = "Cap Test Org", OwnerId = AdminId, IsActive = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    });
    var workspace = await Context.Workspaces.SingleAsync(w => w.Id == WorkspaceId);
    workspace.OrganizationId = orgId;

    var jobId = Guid.NewGuid();
    await Context.Jobs.AddAsync(new Job
    {
        Id = jobId, WorkspaceId = WorkspaceId, JobNumber = "J-2", Title = "Cap Test Job",
        Status = "Open", CreatedBy = AdminId, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    });

    var policyId = Guid.NewGuid();
    await Context.AssignmentPolicies.AddAsync(new AssignmentPolicy
    {
        Id = policyId, Name = "TestFullChain3",
        RulesJson = $"{{\"grants\":{{\"{Constants.ScopeTypes.Workspace}\":\"{RoleConfiguration.WorkspaceMemberRoleId}\",\"{Constants.ScopeTypes.Organization}\":\"{RoleConfiguration.OrgMemberRoleId}\"}}}}"
    });
    var testRoleId = Guid.NewGuid();
    await Context.Roles.AddAsync(new Role
    {
        Id = testRoleId, Name = "TestCapRole", Description = "test", IsSystem = false, PolicyId = policyId,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    });
    await Context.SaveChangesAsync();

    var top = await GrantService.ResolveTopAncestorAsync(
        Constants.ScopeTypes.Job, jobId, testRoleId, stopAtScopeType: Constants.ScopeTypes.Workspace);

    Assert.NotNull(top);
    Assert.Equal(Constants.ScopeTypes.Workspace, top!.Value.ScopeType);
    Assert.Equal(WorkspaceId, top.Value.ScopeId);
    Assert.Equal(RoleConfiguration.WorkspaceMemberRoleId, top.Value.RoleId);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter UserAccessGrantServiceTests` (from `api/`)
Expected: FAIL — current code reads `"ancestors"` (array), not `"grants"` (object); the seeded test policies use the new shape, so the old code finds no `"ancestors"` property and no-ops, leaving the assertions unmet.

- [ ] **Step 3: Rewrite `GrantAncestorRolesAsync`**

In `UserAccessGrantService.cs`, replace the entire method body:

```csharp
private async Task GrantAncestorRolesAsync(Guid userId, string scopeType, Guid scopeId, Role grantedRole, Guid assignedBy)
{
    try
    {
        var policy = grantedRole.Policy;
        if (policy == null)
            return;

        var policyDoc = JsonSerializer.Deserialize<JsonElement>(policy.RulesJson);
        if (!policyDoc.TryGetProperty("grants", out var grantsObj) || grantsObj.ValueKind != JsonValueKind.Object)
            return;

        // Walk the REAL scope hierarchy (via the resolver, not a fixed array position) all
        // the way to its top, granting only at scope types present in the map. A scope type
        // absent from the map is a transit hop - resolved to keep walking, nothing granted
        // there. This is what makes the same policy safe for a role granted at different
        // starting depths (e.g. Surveyor at Job scope vs directly at Workspace scope) - a
        // role never grants anything at its OWN starting scope, only at real ancestors of it.
        string currentScopeType = scopeType;
        Guid currentScopeId = scopeId;

        while (true)
        {
            var parentScopeType = _scopeIdResolver.GetParentScopeType(currentScopeType);
            if (parentScopeType == null)
                break;

            var parentScopeId = await _scopeIdResolver.GetParentIdAsync(currentScopeType, currentScopeId);
            if (parentScopeId == null)
            {
                _logger.LogWarning("No parent scope found for {ScopeType}:{ScopeId}. Ancestor chain stops.",
                    currentScopeType, currentScopeId);
                break;
            }

            if (grantsObj.TryGetProperty(parentScopeType, out var ancestorRoleIdEl) &&
                ancestorRoleIdEl.ValueKind == JsonValueKind.String &&
                Guid.TryParse(ancestorRoleIdEl.GetString(), out var ancestorRoleId))
            {
                // Check if user already has ANY active role at the ancestor scope - if so,
                // they've already earned baseline presence there and this policy shouldn't
                // add or touch anything.
                var hasAnyRoleAtAncestor = await _context.UserAccesses
                    .Where(ua => ua.UserId == userId && ua.ScopeType == parentScopeType &&
                                 ua.ScopeId == parentScopeId && ua.IsActive)
                    .AnyAsync();

                if (!hasAnyRoleAtAncestor)
                {
                    // Reactivate a prior chain-granted row if one exists (e.g. revoked, then
                    // re-granted) rather than inserting a duplicate that would collide with
                    // history and leave two rows chasing the same (user, role, scope).
                    // IgnoreQueryFilters: ApplicationDbContext filters UserAccess to IsActive
                    // by default - the one row we need to find here is exactly the inactive one.
                    var existingAncestorAccess = await _context.UserAccesses.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(ua => ua.UserId == userId && ua.RoleId == ancestorRoleId &&
                            ua.ScopeType == parentScopeType && ua.ScopeId == parentScopeId);

                    if (existingAncestorAccess != null)
                    {
                        existingAncestorAccess.IsActive = true;
                        existingAncestorAccess.IsChainGranted = true;
                        existingAncestorAccess.AssignedBy = assignedBy;
                        existingAncestorAccess.AssignedAt = DateTime.UtcNow;
                        existingAncestorAccess.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        existingAncestorAccess = new UserAccess
                        {
                            Id = Guid.NewGuid(),
                            UserId = userId,
                            RoleId = ancestorRoleId,
                            ScopeType = parentScopeType,
                            ScopeId = parentScopeId.Value,
                            AssignedAt = DateTime.UtcNow,
                            AssignedBy = assignedBy,
                            IsActive = true,
                            IsChainGranted = true,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        await _context.UserAccesses.AddAsync(existingAncestorAccess);
                    }

                    var ancestorRoleEntity = await _context.Roles.FirstOrDefaultAsync(r => r.Id == ancestorRoleId);
                    if (ancestorRoleEntity != null)
                    {
                        await SyncCasbinAsync(() => _casbinService.AddRoleForUserAsync(
                            userId.ToString(), ancestorRoleEntity.Name, parentScopeId.ToString()));
                    }
                }
            }

            // Move up the chain regardless of whether this hop granted anything.
            currentScopeType = parentScopeType;
            currentScopeId = parentScopeId.Value;
        }

        await _context.SaveChangesAsync();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error granting ancestor roles for user {UserId} at {ScopeType}:{ScopeId}",
            userId, scopeType, scopeId);
        throw;
    }
}
```

- [ ] **Step 4: Rewrite `ResolveTopAncestorAsync`**

Replace its body (and update the interface signature in `IUserAccessGrantService` to match):

```csharp
public async Task<(string ScopeType, Guid ScopeId, Guid RoleId)?> ResolveTopAncestorAsync(
    string scopeType, Guid scopeId, Guid roleId, string? stopAtScopeType = null)
{
    var role = await _context.Roles.Include(r => r.Policy).FirstOrDefaultAsync(r => r.Id == roleId);
    var policy = role?.Policy;
    if (policy == null)
        return null;

    JsonElement policyDoc;
    try
    {
        policyDoc = JsonSerializer.Deserialize<JsonElement>(policy.RulesJson);
    }
    catch (JsonException)
    {
        return null;
    }

    if (!policyDoc.TryGetProperty("grants", out var grantsObj) || grantsObj.ValueKind != JsonValueKind.Object)
        return null;

    string currentScopeType = scopeType;
    Guid currentScopeId = scopeId;
    (string ScopeType, Guid ScopeId, Guid RoleId)? top = null;

    while (true)
    {
        var parentScopeType = _scopeIdResolver.GetParentScopeType(currentScopeType);
        if (parentScopeType == null)
            break;

        var parentScopeId = await _scopeIdResolver.GetParentIdAsync(currentScopeType, currentScopeId);
        if (parentScopeId == null)
            break;

        if (grantsObj.TryGetProperty(parentScopeType, out var ancestorRoleIdEl) &&
            ancestorRoleIdEl.ValueKind == JsonValueKind.String &&
            Guid.TryParse(ancestorRoleIdEl.GetString(), out var ancestorRoleId))
        {
            top = (parentScopeType, parentScopeId.Value, ancestorRoleId);
        }

        currentScopeType = parentScopeType;
        currentScopeId = parentScopeId.Value;

        if (stopAtScopeType != null && currentScopeType == stopAtScopeType)
            break;
    }

    return top;
}
```

In `IUserAccessGrantService`, update the interface declaration to match:

```csharp
Task<(string ScopeType, Guid ScopeId, Guid RoleId)?> ResolveTopAncestorAsync(
    string scopeType, Guid scopeId, Guid roleId, string? stopAtScopeType = null);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter UserAccessGrantServiceTests` (from `api/`)
Expected: PASS

- [ ] **Step 6: Run the scoped suite for anything else touching this engine**

Run: `dotnet test --filter "FullyQualifiedName~UserAccessGrantService|FullyQualifiedName~ScopeIdResolver|FullyQualifiedName~InvitationFlow|FullyQualifiedName~AccessibleJobs"` (from `api/`)
Expected: PASS. Existing `AssignmentPolicy` seed rows still use the old `{"ancestors":[...]}` shape at this point (Task 3 changes that) — since `TryGetProperty("grants", ...)` returns false for those old-shaped rows, both rewritten methods simply return early (no grant, no top), which is a strict no-op for every currently-seeded policy. Every existing test that exercised real chain-granting through `Admin`/`Surveyor`/`Client`/`Member`/`Finance` will therefore behave as if chaining is temporarily disabled — expect some `InvitationFlowTests`/`AccessibleJobsTests` failures here (e.g. `AcceptingJobTriggeredInvite_GrantsBothJobRoleAndWorkspaceMember`) until Task 3 reseeds the policies in the new shape. If failures appear at this step, that is expected — do not attempt to fix them here; proceed to Task 3, which corrects it.

- [ ] **Step 7: Commit**

```bash
git add api/src/SurveyorLedger.API/Services/UserAccessGrantService.cs api/tests/SurveyorLedger.API.Tests/Services/UserAccessGrantServiceTests.cs
git commit -m "feat: rewrite ancestor-chain walk to a scope-type-keyed grants map

Fixes a real corruption risk: a role like Surveyor can be granted at
either Job scope (chains up) or directly at Workspace scope
(WorkspaceService.AddMemberRoleAsync) - a fixed-position ancestors
array can't safely describe both starting depths once Workspace has
a registered parent (Organization). The new grants map resolves the
real hierarchy at each hop instead of trusting array position, so a
role never grants anything at its own starting scope."
```

---

### Task 3: Reseed AssignmentPolicy/Role data in the new grants-map shape

**Files:**
- Modify: `api/src/SurveyorLedger.Data/Configurations/AssignmentPolicyConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/RoleConfiguration.cs`
- Create (generated, do not hand-edit): `api/src/SurveyorLedger.Data/Migrations/<timestamp>_ReseedPoliciesForOrganizationChain.cs`

**Interfaces:**
- Consumes: the `{"grants":{...}}` shape from Task 2.
- Produces: `AssignmentPolicyConfiguration.OrgOnlyId` (new fixed GUID), the real seeded `FullChain`/`SingleScope`/`OrgOnly` policies that every subsequent task and existing invite/grant flow relies on.

- [ ] **Step 1: Update AssignmentPolicyConfiguration**

Replace the whole file's `HasData` section:

```csharp
public static readonly Guid SingleScopeId = new("00000000-0000-0000-0000-000000000701");
public static readonly Guid FullChainId = new("00000000-0000-0000-0000-000000000702");
public static readonly Guid OrgOnlyId = new("00000000-0000-0000-0000-000000000703");

public void Configure(EntityTypeBuilder<AssignmentPolicy> builder)
{
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
    builder.Property(x => x.RulesJson).IsRequired();

    builder.HasMany(x => x.Roles).WithOne(x => x.Policy).HasForeignKey(x => x.PolicyId);

    builder.HasData(
        new AssignmentPolicy
        {
            Id = SingleScopeId,
            Name = "SingleScope",
            RulesJson = "{\"grants\":{}}"
        },
        new AssignmentPolicy
        {
            Id = FullChainId,
            Name = "FullChain",
            RulesJson = "{\"grants\":{\"Workspace\":\"00000000-0000-0000-0000-000000000801\",\"Organization\":\"00000000-0000-0000-0000-000000000010\"}}"
        },
        new AssignmentPolicy
        {
            Id = OrgOnlyId,
            Name = "OrgOnly",
            RulesJson = "{\"grants\":{\"Organization\":\"00000000-0000-0000-0000-000000000010\"}}"
        }
    );
}
```

(`00000000-0000-0000-0000-000000000801` is `RoleConfiguration.WorkspaceMemberRoleId`,
`00000000-0000-0000-0000-000000000010` is `RoleConfiguration.OrgMemberRoleId` — both
literal here since `AssignmentPolicyConfiguration` is configured before `RoleConfiguration`
in the model and the existing `FullChain` row already embeds `WorkspaceMemberRoleId`
literally the same way.)

- [ ] **Step 2: Reassign PolicyId on Client, Finance, and WorkspaceMember roles**

In `RoleConfiguration.cs`, in the `HasData(...)` call, change three `PolicyId` values (leave every other field on every row untouched):

```csharp
new Role { Id = ClientRoleId, Name = Constants.SystemRoles.Client, Description = "Views job status and results for their organization.", IsSystem = true, PolicyId = AssignmentPolicyConfiguration.OrgOnlyId, CreatedAt = seededAt, UpdatedAt = seededAt },
new Role { Id = MemberRoleId, Name = Constants.SystemRoles.Member, Description = "Workspace membership only. No access to jobs or land until assigned.", IsSystem = true, PolicyId = AssignmentPolicyConfiguration.FullChainId, CreatedAt = seededAt, UpdatedAt = seededAt },
new Role { Id = FinanceRoleId, Name = Constants.SystemRoles.Finance, Description = "Job-scoped view of invoices and quotations for that job only.", IsSystem = true, PolicyId = AssignmentPolicyConfiguration.OrgOnlyId, CreatedAt = seededAt, UpdatedAt = seededAt },
new Role { Id = WorkspaceMemberRoleId, Name = "WorkspaceMember", Description = "Least-privilege membership granted automatically when a role requires workspace-level presence.", IsSystem = true, PolicyId = AssignmentPolicyConfiguration.FullChainId, CreatedAt = seededAt, UpdatedAt = seededAt },
```

`Member`'s `PolicyId` value doesn't actually change (it was already `FullChainId`) — it's reprinted here only so the diff context around it is unambiguous; the real changes are `ClientRoleId` and `FinanceRoleId` to `OrgOnlyId`, and `WorkspaceMemberRoleId` from `SingleScopeId` to `FullChainId`.

- [ ] **Step 3: Generate the migration**

Run from `api/`:
```bash
dotnet ef migrations add ReseedPoliciesForOrganizationChain --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```
This should produce `UpdateData` operations for the 3 `AssignmentPolicies` rows and the 3 changed `Roles` rows, plus an `InsertData` for the new `OrgOnly` policy row. Do not hand-edit — if the diff looks wrong, fix the Configuration classes and regenerate (`dotnet ef migrations remove` then re-add).

- [ ] **Step 4: Apply it**

```bash
sqllocaldb start MSSQLLocalDB
dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```

- [ ] **Step 5: Run the scoped suite from Task 2 again — now expect full green**

Run: `dotnet test --filter "FullyQualifiedName~UserAccessGrantService|FullyQualifiedName~ScopeIdResolver|FullyQualifiedName~InvitationFlow|FullyQualifiedName~AccessibleJobs"` (from `api/`)
Expected: PASS — the failures noted at the end of Task 2 are now resolved.

- [ ] **Step 6: Write the regression-guard test**

Add to `api/tests/SurveyorLedger.API.Tests/Services/InvitationFlowTests.cs` — this class already has `_invitationService`/`jobService` set up the same way in every test (see `AcceptingJobTriggeredInvite_GrantsBothJobRoleAndWorkspaceMember`), a `GetAccountIdAsync(personId)` private helper, and `Context`/`WorkspaceId`/`AdminId` from `WorkspaceIntegrationTestBase`:

```csharp
[Fact]
public async Task PendingInvitationsList_StillShowsAChainingRoleInvite_AfterOrganizationHopAdded()
{
    // Regression guard: before this feature, a Surveyor job-invite's primary scope was
    // Workspace (chained from Job). The grants-map walk now reaches Organization too - this
    // confirms ResolveTopAncestorAsync's stopAtScopeType cap keeps the invite's PRIMARY scope
    // at Workspace, so it still shows up in the workspace's pending-invitations list.
    _invitationService = GetService<IInvitationService>();
    var jobService = GetService<IJobService>();

    var job = await jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Guard job" });
    var result = await jobService.InviteParticipantByEmailAsync(
        WorkspaceId, AdminId, job.Id, "Surveyor", "guard.surveyor@test.local", "Guard", "Surveyor", null, null);

    Assert.Equal(Constants.ScopeTypes.Workspace, result.ScopeType);

    var pending = await _invitationService.GetPendingInvitationsAsync(WorkspaceId, AdminId);

    Assert.Contains(pending, i => i.Email == "guard.surveyor@test.local" && i.ScopeType == Constants.ScopeTypes.Workspace);
}
```

- [ ] **Step 7: Run it**

Run: `dotnet test --filter InvitationFlowTests` (from `api/`)
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add api/src/SurveyorLedger.Data/Configurations/AssignmentPolicyConfiguration.cs api/src/SurveyorLedger.Data/Configurations/RoleConfiguration.cs api/src/SurveyorLedger.Data/Migrations/ api/tests/SurveyorLedger.API.Tests/Services/InvitationFlowTests.cs
git commit -m "feat: reseed RBAC policies so every role's chain reaches Organization"
```

---

### Task 4: Verify the Client invite-accept boundary (no WorkspaceMember, yes OrgMember)

**Files:**
- Test: `api/tests/SurveyorLedger.API.Tests/Services/InvitationFlowTests.cs`

**Interfaces:**
- Consumes: Task 3's seeded `OrgOnly` policy on `Client`.

- [ ] **Step 1: Write the test**

Add to `InvitationFlowTests.cs`, following the exact same three-step pattern (invite → `CompleteInvitationAsync` → `AcceptInvitationAsync`) as `AcceptingJobTriggeredInvite_GrantsBothJobRoleAndWorkspaceMember`, but for a Client (which — unlike Surveyor — does not chain to Workspace, so `InviteParticipantByEmailAsync`'s result targets Job scope directly, not Workspace):

```csharp
[Fact]
public async Task AcceptingClientInvite_GrantsOrgMember_NeverGrantsWorkspaceMember()
{
    _invitationService = GetService<IInvitationService>();
    var jobService = GetService<IJobService>();

    var job = await jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Client boundary job" });
    var result = await jobService.InviteParticipantByEmailAsync(
        WorkspaceId, AdminId, job.Id, "Client", "new.client@test.local", "New", "Client", null, null);

    // Client's OrgOnly policy has no ancestor at Workspace scope, so unlike a chaining role
    // the invite's primary scope is Job itself, not Workspace.
    Assert.Equal(Constants.ScopeTypes.Job, result.ScopeType);

    await _invitationService.CompleteInvitationAsync(result.Token, new CompleteInvitationRequest
    {
        Password = "SomePassword123!",
        ConfirmPassword = "SomePassword123!",
        FirstName = "New",
        LastName = "Client"
    });
    var accountId = await GetAccountIdAsync(result.UserId) ?? throw new Exception("Account should exist after completing invitation.");

    await _invitationService.AcceptInvitationAsync(result.Id, accountId);

    var organizationId = await Context.Workspaces.Where(w => w.Id == WorkspaceId).Select(w => w.OrganizationId).FirstAsync();

    var hasWorkspaceMember = await Context.UserAccesses.AnyAsync(ua =>
        ua.UserId == accountId && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == WorkspaceId && ua.IsActive);
    Assert.False(hasWorkspaceMember);

    var hasOrgMember = await Context.UserAccesses.AnyAsync(ua =>
        ua.UserId == accountId && ua.ScopeType == Constants.ScopeTypes.Organization && ua.ScopeId == organizationId && ua.IsActive);
    Assert.True(hasOrgMember);
}
```

`WorkspaceId`'s `OrganizationId` will be a real, non-`Guid.Empty` value at this point in the plan: `WorkspaceIntegrationTestBase` was updated to seed an `Organization` + `OrganizationSubscription` and set `Workspace.OrganizationId` as part of the earlier organization-layer plan (`2026-08-22-organization-layer.md`), which this plan builds on top of.

- [ ] **Step 2: Run it**

Run: `dotnet test --filter InvitationFlowTests` (from `api/`)
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add api/tests/SurveyorLedger.API.Tests/Services/InvitationFlowTests.cs
git commit -m "test: verify Client invite-accept reaches OrgMember without WorkspaceMember"
```

---

### Task 5: Backfill pre-existing workspace/job-scope members into their org

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/OrganizationBackfillService.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/OrganizationBackfillServiceTests.cs`

**Interfaces:**
- Consumes: `IUserAccessGrantService.GrantAsync` (existing), `RoleConfiguration.OrgMemberRoleId`.
- Produces: `OrganizationBackfillService.RunAsync()` now also closes the gap for pre-existing members (not just pre-existing workspace owners).

- [ ] **Step 1: Write the failing test**

Add to `OrganizationBackfillServiceTests.cs`:

```csharp
[Fact]
public async Task RunAsync_grants_OrgMember_to_a_preexisting_workspace_member_without_org_access()
{
    // Simulates data from before this feature: a Surveyor with active Workspace-scope access
    // but no Organization-scope grant at all (invites never used to reach Organization).
    var surveyor = await CreateUserAccountAsync("PreExisting", "Surveyor", "pre-existing-surveyor@test.local");
    await GrantService.GrantAsync(surveyor, RoleConfiguration.SurveyorRoleId, Constants.ScopeTypes.Workspace, WorkspaceId, AdminId);
    // Remove whatever org-chain grant just happened from the call above, so this test
    // reproduces the actual pre-existing-data scenario (no org grant at all yet).
    await Context.UserAccesses
        .Where(ua => ua.UserId == surveyor && ua.ScopeType == Constants.ScopeTypes.Organization)
        .ExecuteDeleteAsync();

    var backfill = GetService<IOrganizationBackfillService>();
    await backfill.RunAsync();

    var org = await Context.Organizations.SingleAsync(o => o.Id ==
        (await Context.Workspaces.Where(w => w.Id == WorkspaceId).Select(w => w.OrganizationId).FirstAsync()));

    var hasOrgMember = await Context.UserAccesses.AnyAsync(ua =>
        ua.UserId == surveyor && ua.ScopeType == Constants.ScopeTypes.Organization && ua.ScopeId == org.Id && ua.IsActive);
    Assert.True(hasOrgMember);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter OrganizationBackfillServiceTests` (from `api/`)
Expected: FAIL — the current `RunAsync` only backfills workspaces with `OrganizationId == Guid.Empty`; it has no pass for members without org access.

- [ ] **Step 3: Add the second backfill pass**

In `OrganizationBackfillService.cs`, add a new private method and call it at the end of `RunAsync`:

```csharp
public async Task RunAsync()
{
    // ... existing owner-org backfill pass, unchanged ...

    await BackfillMemberOrgAccessAsync();
}

/// <summary>
/// Idempotent: for every active Workspace-scope or Job-scope UserAccess row whose user has
/// no Organization-scope grant on that workspace's org yet, grants OrgMember there directly.
/// Covers data from before invites/direct grants started reaching Organization (this
/// feature's rollout) - safe to call on every startup, a no-op once every member has caught up.
/// </summary>
private async Task BackfillMemberOrgAccessAsync()
{
    var workspaceScopeUserWorkspacePairs = await _context.UserAccesses
        .Where(ua => ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace)
        .Select(ua => new { ua.UserId, WorkspaceId = ua.ScopeId })
        .Distinct()
        .ToListAsync();

    var jobScopeUserJobPairs = await _context.UserAccesses
        .Where(ua => ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Job)
        .Select(ua => new { ua.UserId, ua.ScopeId })
        .Distinct()
        .ToListAsync();
    var jobWorkspaceIds = jobScopeUserJobPairs.Select(p => p.ScopeId).Distinct().ToList();
    var jobToWorkspace = await _context.Jobs
        .Where(j => jobWorkspaceIds.Contains(j.Id))
        .ToDictionaryAsync(j => j.Id, j => j.WorkspaceId);

    var userWorkspacePairs = workspaceScopeUserWorkspacePairs
        .Select(p => (p.UserId, WorkspaceId: p.WorkspaceId))
        .Concat(jobScopeUserJobPairs
            .Where(p => jobToWorkspace.ContainsKey(p.ScopeId))
            .Select(p => (p.UserId, WorkspaceId: jobToWorkspace[p.ScopeId])))
        .Distinct()
        .ToList();

    if (userWorkspacePairs.Count == 0)
        return;

    var workspaceIds = userWorkspacePairs.Select(p => p.WorkspaceId).Distinct().ToList();
    var workspaceToOrg = await _context.Workspaces
        .Where(w => workspaceIds.Contains(w.Id))
        .ToDictionaryAsync(w => w.Id, w => w.OrganizationId);

    foreach (var (userId, workspaceId) in userWorkspacePairs)
    {
        if (!workspaceToOrg.TryGetValue(workspaceId, out var organizationId))
            continue;

        var alreadyMember = await _context.UserAccesses.AnyAsync(ua =>
            ua.UserId == userId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Organization && ua.ScopeId == organizationId);
        if (alreadyMember)
            continue;

        await _grantService.GrantAsync(userId, RoleConfiguration.OrgMemberRoleId, Constants.ScopeTypes.Organization, organizationId, userId);
        _logger.LogInformation("Backfilled OrgMember for user {UserId} on organization {OrganizationId}", userId, organizationId);
    }
}
```

Add `using SurveyorLedger.Data.Configurations;` to the file's usings if not already present (for `RoleConfiguration.OrgMemberRoleId`).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter OrganizationBackfillServiceTests` (from `api/`)
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add api/src/SurveyorLedger.API/Services/OrganizationBackfillService.cs api/tests/SurveyorLedger.API.Tests/Services/OrganizationBackfillServiceTests.cs
git commit -m "feat: backfill OrgMember for pre-existing workspace/job-scope members"
```

---

### Task 6: Expose organizationId on the workspace and job DTOs

**Files:**
- Modify: `api/src/SurveyorLedger.API/Models/Workspace/WorkspaceResponse.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/WorkspaceController.cs`
- Modify: `api/src/SurveyorLedger.API/Services/WorkspaceService.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Job/AccessibleJobResponse.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/JobsController.cs`
- Modify: `api/src/SurveyorLedger.API/Services/ScopedAccessService.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/WorkspaceServiceTests.cs`, `api/tests/SurveyorLedger.API.Tests/Services/AccessibleJobsTests.cs`

**Interfaces:**
- Produces: `GET /api/workspace` and `GET /api/workspace/{id}` responses gain `organizationId` (Guid) and `organizationName` (string). `GET /api/jobs/mine` responses gain `organizationId` (Guid).

- [ ] **Step 1: Write the failing workspace test**

Add to `WorkspaceServiceTests.cs`:

```csharp
[Fact]
public async Task GetUserWorkspacesAsync_IncludesOrganizationId()
{
    var svc = GetService<IWorkspaceService>();
    var workspaces = await svc.GetUserWorkspacesAsync(AdminId);

    var workspace = workspaces.Single(w => w.Workspace.Id == WorkspaceId);
    Assert.NotEqual(Guid.Empty, workspace.Workspace.OrganizationId);
}
```

(`WorkspaceWithAccess.Workspace.OrganizationId` already exists on the entity — this test just confirms the query loads it, which it already does via the `.Include(w => w.Organization)` added in the earlier organization-layer work. It's here as a guard, not new behavior on the service side; the real new work in this task is on the DTO/controller layer below.)

- [ ] **Step 2: Add organizationId/organizationName to WorkspaceResponse**

In `WorkspaceResponse.cs`, add two properties:

```csharp
/// <summary>The organization this workspace belongs to.</summary>
public Guid OrganizationId { get; set; }

/// <summary>The organization's name, for display without a second lookup.</summary>
public required string OrganizationName { get; set; }
```

- [ ] **Step 3: Update the controller mapping**

In `WorkspaceController.cs`, update the private `ToResponse(WorkspaceWithAccess w)` method:

```csharp
private static WorkspaceResponse ToResponse(WorkspaceWithAccess w) => new()
{
    WorkspaceId = w.Workspace.Id,
    Name = w.Workspace.Name,
    Description = w.Workspace.Description,
    CreatedAt = w.Workspace.CreatedAt,
    IsActive = w.Workspace.IsActive,
    Tier = w.Tier,
    Roles = w.Roles,
    OrganizationId = w.Workspace.OrganizationId,
    OrganizationName = w.Workspace.Organization?.Name ?? ""
};
```

`w.Workspace.Organization` is already loaded by `GetUserWorkspacesAsync`/`GetWorkspaceByIdAsync`'s existing `.Include(w => w.Organization).ThenInclude(o => o.Subscription)` — no service-layer change needed for this task.

- [ ] **Step 4: Run the workspace test**

Run: `dotnet test --filter WorkspaceServiceTests` (from `api/`)
Expected: PASS

- [ ] **Step 5: Write the failing job test**

Add to `AccessibleJobsTests.cs`, following the exact setup pattern already used by `Admin_SeesWorkspaceJobs_TaggedWorkspaceLevel` in the same file (`_jobService`/`_access` fields, `IJobService.CreateAsync`):

```csharp
[Fact]
public async Task GetAccessibleJobsAsync_IncludesOrganizationId()
{
    _jobService = GetService<IJobService>();
    _access = GetService<IScopedAccessService>();
    var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });

    var jobs = await _access.GetAccessibleJobsAsync(AdminId);

    var result = Assert.Single(jobs);
    Assert.Equal(job.Id, result.JobId);
    Assert.NotEqual(Guid.Empty, result.OrganizationId);
}
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test --filter AccessibleJobsTests` (from `api/`)
Expected: FAIL — compile error, `AccessibleJob` record has no `OrganizationId` member yet.

- [ ] **Step 7: Add organizationId to the AccessibleJob record and its computation**

In `ScopedAccessService.cs`, update the record:

```csharp
public record AccessibleJob(
    Guid JobId, string JobNumber, string Title, string Status,
    Guid WorkspaceId, string WorkspaceName, Guid OrganizationId, string AccessScopeType);
```

Update `GetAccessibleJobsAsync` to also look up and pass organization ids — change the `workspaceNames` dictionary build and the final `Select` to also carry `OrganizationId`:

```csharp
var workspaceIds = tagged.Select(t => t.Job.WorkspaceId).Distinct().ToList();
var workspaceInfo = await _context.Workspaces
    .Where(w => workspaceIds.Contains(w.Id))
    .ToDictionaryAsync(w => w.Id, w => new { w.Name, w.OrganizationId });

return tagged
    .Select(t => new AccessibleJob(
        t.Job.Id, t.Job.JobNumber, t.Job.Title, t.Job.Status,
        t.Job.WorkspaceId, workspaceInfo.GetValueOrDefault(t.Job.WorkspaceId)?.Name ?? "Unknown workspace",
        workspaceInfo.GetValueOrDefault(t.Job.WorkspaceId)?.OrganizationId ?? Guid.Empty,
        t.Scope))
    .ToList();
```

- [ ] **Step 8: Add organizationId to AccessibleJobResponse and its controller mapping**

In `AccessibleJobResponse.cs`:

```csharp
public class AccessibleJobResponse
{
    public Guid JobId { get; set; }
    public required string JobNumber { get; set; }
    public required string Title { get; set; }
    public required string Status { get; set; }
    public Guid WorkspaceId { get; set; }
    public required string WorkspaceName { get; set; }
    public Guid OrganizationId { get; set; }

    /// <summary>The real scope-type value (Constants.ScopeTypes) the access was found at - "Workspace" or "Job" today, "Organization" later.</summary>
    public required string AccessScopeType { get; set; }
}
```

In `JobsController.cs`'s `GetMine()`:

```csharp
var response = jobs.Select(j => new AccessibleJobResponse
{
    JobId = j.JobId,
    JobNumber = j.JobNumber,
    Title = j.Title,
    Status = j.Status,
    WorkspaceId = j.WorkspaceId,
    WorkspaceName = j.WorkspaceName,
    OrganizationId = j.OrganizationId,
    AccessScopeType = j.AccessScopeType
}).ToList();
```

- [ ] **Step 9: Run test to verify it passes**

Run: `dotnet test --filter AccessibleJobsTests` (from `api/`)
Expected: PASS

- [ ] **Step 10: Run the full backend suite (pre-frontend checkpoint)**

Run: `dotnet test` (from `api/`)
Expected: PASS. This is the natural checkpoint between the backend and frontend halves of this plan — a good place for the one mid-plan full-suite run, not because every task needs it, but because everything from here on is Angular and won't touch the API again.

- [ ] **Step 11: Commit**

```bash
git add api/src/SurveyorLedger.API/Models/Workspace/WorkspaceResponse.cs api/src/SurveyorLedger.API/Controllers/WorkspaceController.cs api/src/SurveyorLedger.API/Models/Job/AccessibleJobResponse.cs api/src/SurveyorLedger.API/Controllers/JobsController.cs api/src/SurveyorLedger.API/Services/ScopedAccessService.cs api/tests/SurveyorLedger.API.Tests/Services/WorkspaceServiceTests.cs api/tests/SurveyorLedger.API.Tests/Services/AccessibleJobsTests.cs
git commit -m "feat: expose organizationId on workspace and job DTOs"
```

---

### Task 7: Angular OrganizationService

**Files:**
- Create: `ui/src/app/core/organization.service.ts`
- Test: `ui/src/app/core/organization.service.spec.ts`

**Interfaces:**
- Produces:
  ```typescript
  export interface Organization {
    id: string; name: string; tier: string;
    workspaceCount: number; maxWorkspaces: number; callerRoles: string[];
  }
  export interface OrganizationMember {
    userId: string; email: string; firstName: string; lastName: string;
    roles: string[]; isOwner: boolean;
  }
  class OrganizationService {
    list(): Observable<Organization[]>;
    create(name: string): Observable<Organization>;
    getById(id: string): Observable<Organization>;
    getMembers(id: string): Observable<OrganizationMember[]>;
    addMember(id: string, targetUserId: string): Observable<void>;
    removeMember(id: string, targetUserId: string): Observable<void>;
    updateSubscription(id: string, tier: string): Observable<Organization>;
  }
  ```

- [ ] **Step 1: Write the failing test**

`ui/src/app/core/organization.service.spec.ts` (mirrors `land.service.spec.ts`'s exact `TestBed`/`HttpTestingController` pattern):

```typescript
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { OrganizationService, Organization } from './organization.service';
import { environment } from '../../environments/environment';

describe('OrganizationService', () => {
  let service: OrganizationService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/organization`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [OrganizationService]
    });
    service = TestBed.inject(OrganizationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list() gets every organization the caller belongs to', () => {
    const orgs: Organization[] = [{ id: 'o1', name: 'Acme', tier: 'Free', workspaceCount: 1, maxWorkspaces: 1, callerRoles: ['OrgOwner'] }];
    service.list().subscribe(result => expect(result).toEqual(orgs));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: orgs });
  });

  it('create() posts the org name', () => {
    const org: Organization = { id: 'o2', name: 'New Co', tier: 'Free', workspaceCount: 0, maxWorkspaces: 1, callerRoles: ['OrgOwner'] };
    service.create('New Co').subscribe(result => expect(result).toEqual(org));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'New Co' });
    req.flush({ success: true, data: org });
  });

  it('getById() gets a single organization', () => {
    const org: Organization = { id: 'o1', name: 'Acme', tier: 'Free', workspaceCount: 1, maxWorkspaces: 1, callerRoles: ['OrgOwner'] };
    service.getById('o1').subscribe(result => expect(result).toEqual(org));
    const req = httpMock.expectOne(`${base}/o1`);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: org });
  });

  it('getMembers() gets the member roster', () => {
    service.getMembers('o1').subscribe();
    const req = httpMock.expectOne(`${base}/o1/members`);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: [] });
  });

  it('addMember() posts to the member endpoint', () => {
    service.addMember('o1', 'u1').subscribe();
    const req = httpMock.expectOne(`${base}/o1/members/u1`);
    expect(req.request.method).toBe('POST');
    req.flush({ success: true, data: null });
  });

  it('removeMember() deletes the member', () => {
    service.removeMember('o1', 'u1').subscribe();
    const req = httpMock.expectOne(`${base}/o1/members/u1`);
    expect(req.request.method).toBe('DELETE');
    req.flush({ success: true, data: null });
  });

  it('updateSubscription() puts the tier', () => {
    const org: Organization = { id: 'o1', name: 'Acme', tier: 'Pro', workspaceCount: 1, maxWorkspaces: 5, callerRoles: ['OrgOwner'] };
    service.updateSubscription('o1', 'Pro').subscribe(result => expect(result).toEqual(org));
    const req = httpMock.expectOne(`${base}/o1/subscription`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ tier: 'Pro' });
    req.flush({ success: true, data: org });
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `ng test --include organization.service.spec.ts` (from `ui/`)
Expected: FAIL — `organization.service.ts` doesn't exist.

- [ ] **Step 3: Implement OrganizationService**

`ui/src/app/core/organization.service.ts`:

```typescript
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface Organization {
  id: string;
  name: string;
  tier: string;
  workspaceCount: number;
  maxWorkspaces: number;
  callerRoles: string[];
}

export interface OrganizationMember {
  userId: string;
  email: string;
  firstName: string;
  lastName: string;
  roles: string[];
  isOwner: boolean;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

@Injectable({ providedIn: 'root' })
export class OrganizationService {
  private apiUrl = `${environment.apiBaseUrl}/organization`;

  constructor(private http: HttpClient) {}

  list(): Observable<Organization[]> {
    return this.http.get<ApiResponse<Organization[]>>(this.apiUrl).pipe(map(res => res.data));
  }

  create(name: string): Observable<Organization> {
    return this.http.post<ApiResponse<Organization>>(this.apiUrl, { name }).pipe(map(res => res.data));
  }

  getById(id: string): Observable<Organization> {
    return this.http.get<ApiResponse<Organization>>(`${this.apiUrl}/${id}`).pipe(map(res => res.data));
  }

  getMembers(id: string): Observable<OrganizationMember[]> {
    return this.http.get<ApiResponse<OrganizationMember[]>>(`${this.apiUrl}/${id}/members`).pipe(map(res => res.data));
  }

  addMember(id: string, targetUserId: string): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/${id}/members/${targetUserId}`, {});
  }

  removeMember(id: string, targetUserId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}/members/${targetUserId}`);
  }

  updateSubscription(id: string, tier: string): Observable<Organization> {
    return this.http.put<ApiResponse<Organization>>(`${this.apiUrl}/${id}/subscription`, { tier }).pipe(map(res => res.data));
  }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `ng test --include organization.service.spec.ts` (from `ui/`)
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add ui/src/app/core/organization.service.ts ui/src/app/core/organization.service.spec.ts
git commit -m "feat: add Angular OrganizationService"
```

---

### Task 8: CurrentOrganizationService with persistence, and the resolve guard

**Files:**
- Create: `ui/src/app/core/current-organization.service.ts`
- Create: `ui/src/app/core/organization-resolve.guard.ts`
- Modify: `ui/src/app/app.routes.ts`
- Test: `ui/src/app/core/organization-resolve.guard.spec.ts`

**Interfaces:**
- Consumes: `OrganizationService.list()` (Task 7).
- Produces: `CurrentOrganizationService.current: Signal<Organization | null>`, `.set(org: Organization): void`, `.clear(): void`; `organizationResolveGuard: CanActivateFn`.

- [ ] **Step 1: Implement CurrentOrganizationService**

`ui/src/app/core/current-organization.service.ts`:

```typescript
import { Injectable, signal } from '@angular/core';
import { Organization } from './organization.service';

const STORAGE_KEY = 'selectedOrganizationId';

@Injectable({ providedIn: 'root' })
export class CurrentOrganizationService {
  private state = signal<Organization | null>(null);
  current = this.state.asReadonly();

  set(organization: Organization): void {
    this.state.set(organization);
    localStorage.setItem(STORAGE_KEY, organization.id);
  }

  clear(): void {
    this.state.set(null);
    localStorage.removeItem(STORAGE_KEY);
  }

  getPersistedId(): string | null {
    return localStorage.getItem(STORAGE_KEY);
  }
}
```

- [ ] **Step 2: Write the failing guard test**

`ui/src/app/core/organization-resolve.guard.spec.ts`:

```typescript
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { Router } from '@angular/router';
import { organizationResolveGuard } from './organization-resolve.guard';
import { CurrentOrganizationService } from './current-organization.service';
import { environment } from '../../environments/environment';
import { Organization } from './organization.service';

describe('organizationResolveGuard', () => {
  let httpMock: HttpTestingController;
  let currentOrg: CurrentOrganizationService;
  const base = `${environment.apiBaseUrl}/organization`;
  const orgs: Organization[] = [
    { id: 'o1', name: 'First', tier: 'Free', workspaceCount: 1, maxWorkspaces: 1, callerRoles: ['OrgOwner'] },
    { id: 'o2', name: 'Second', tier: 'Free', workspaceCount: 1, maxWorkspaces: 1, callerRoles: ['OrgMember'] }
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule]
    });
    httpMock = TestBed.inject(HttpTestingController);
    currentOrg = TestBed.inject(CurrentOrganizationService);
    localStorage.clear();
  });

  afterEach(() => httpMock.verify());

  it('restores a persisted org id that is still in the list', (done) => {
    localStorage.setItem('selectedOrganizationId', 'o2');

    TestBed.runInInjectionContext(() => {
      const result = organizationResolveGuard({} as any, {} as any);
      (result as any).subscribe((allowed: boolean) => {
        expect(allowed).toBe(true);
        expect(currentOrg.current()?.id).toBe('o2');
        done();
      });
    });

    httpMock.expectOne(base).flush({ success: true, data: orgs });
  });

  it('falls back to the first org when the persisted id is stale', (done) => {
    localStorage.setItem('selectedOrganizationId', 'does-not-exist');

    TestBed.runInInjectionContext(() => {
      const result = organizationResolveGuard({} as any, {} as any);
      (result as any).subscribe((allowed: boolean) => {
        expect(allowed).toBe(true);
        expect(currentOrg.current()?.id).toBe('o1');
        done();
      });
    });

    httpMock.expectOne(base).flush({ success: true, data: orgs });
  });
});
```

- [ ] **Step 3: Run test to verify it fails**

Run: `ng test --include organization-resolve.guard.spec.ts` (from `ui/`)
Expected: FAIL — `organization-resolve.guard.ts` doesn't exist.

- [ ] **Step 4: Implement the guard**

`ui/src/app/core/organization-resolve.guard.ts`:

```typescript
import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { OrganizationService } from './organization.service';
import { CurrentOrganizationService } from './current-organization.service';

/**
 * Runs once on entering /app. Every user is guaranteed at least one organization (see the
 * invite-accept and startup-backfill changes elsewhere in this plan), so this never blocks
 * navigation - it only restores the persisted selection (or defaults to the first org) so the
 * user doesn't have to re-pick an organization every time they come back.
 */
export const organizationResolveGuard: CanActivateFn = () => {
  const orgService = inject(OrganizationService);
  const currentOrg = inject(CurrentOrganizationService);

  return orgService.list().pipe(
    map(orgs => {
      if (orgs.length === 0)
        return true;

      const persistedId = currentOrg.getPersistedId();
      const match = orgs.find(o => o.id === persistedId) ?? orgs[0];
      currentOrg.set(match);
      return true;
    }),
    catchError(() => of(true))
  );
};
```

- [ ] **Step 5: Run test to verify it passes**

Run: `ng test --include organization-resolve.guard.spec.ts` (from `ui/`)
Expected: PASS

- [ ] **Step 6: Wire the guard into the app shell route**

In `app.routes.ts`, add the import and add the guard to the existing `app` route's `canActivate` array:

```typescript
import { organizationResolveGuard } from './core/organization-resolve.guard';
```

```typescript
{
  path: 'app',
  component: AppShellComponent,
  canActivate: [authGuard, organizationResolveGuard],
  children: [
    // ... unchanged
  ]
},
```

- [ ] **Step 7: Commit**

```bash
git add ui/src/app/core/current-organization.service.ts ui/src/app/core/organization-resolve.guard.ts ui/src/app/core/organization-resolve.guard.spec.ts ui/src/app/app.routes.ts
git commit -m "feat: add CurrentOrganizationService with persisted selection"
```

---

### Task 9: Topbar quick-switch dropdown

**Files:**
- Modify: `ui/src/app/shell/topbar.component.ts`

**Interfaces:**
- Consumes: `OrganizationService.list()`, `CurrentOrganizationService.current`/`.set()` (Tasks 7-8).

- [ ] **Step 1: Update the component**

Replace `topbar.component.ts` in full:

```typescript
import { Component, OnInit, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { OrganizationService, Organization } from '../core/organization.service';
import { CurrentOrganizationService } from '../core/current-organization.service';

@Component({
  selector: 'app-topbar',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <header class="h-14 border-b border-neutral-200 bg-white flex items-center justify-between px-lg gap-md">
      <button
        type="button"
        class="md:hidden text-neutral-600 hover:text-neutral-900"
        (click)="menuToggle.emit()"
        aria-label="Toggle menu"
      >
        ☰
      </button>

      @if (currentOrg.current(); as org) {
        <div class="relative">
          <button
            type="button"
            class="flex items-center gap-xs px-md py-xs rounded text-sm text-neutral-700 hover:bg-neutral-100"
            (click)="orgMenuOpen.set(!orgMenuOpen())"
          >
            <span class="font-medium">{{ org.name }}</span>
            <span class="text-xs px-xs py-[1px] rounded bg-neutral-100 text-neutral-600">{{ org.tier }}</span>
            <span class="text-neutral-400">▾</span>
          </button>

          @if (orgMenuOpen()) {
            <div class="absolute left-0 mt-xs w-64 card p-xs shadow-lg z-10" (mouseleave)="orgMenuOpen.set(false)">
              @for (o of organizations(); track o.id) {
                <button
                  type="button"
                  class="w-full text-left px-md py-sm text-sm rounded hover:bg-neutral-100 flex items-center justify-between"
                  [class.bg-primary-50]="o.id === org.id"
                  (click)="switchTo(o)"
                >
                  <span>{{ o.name }}</span>
                  @if (o.id === org.id) {
                    <span class="text-primary-500">✓</span>
                  }
                </button>
              }
              <div class="border-t border-neutral-100 mt-xs pt-xs">
                <a routerLink="/app/organizations" class="block px-md py-sm text-sm text-neutral-700 hover:bg-neutral-100 rounded" (click)="orgMenuOpen.set(false)">
                  Manage organizations
                </a>
              </div>
            </div>
          }
        </div>
      }

      <button
        type="button"
        class="flex-1 max-w-sm flex items-center gap-sm px-md py-xs bg-neutral-100 rounded text-sm text-neutral-500 hover:bg-neutral-200 text-left"
        (click)="paletteOpen.emit()"
      >
        <span>Search…</span>
        <span class="ml-auto text-xs border border-neutral-300 rounded px-xs bg-white">⌘K</span>
      </button>

      <div class="relative">
        <button
          type="button"
          class="w-8 h-8 rounded-full bg-primary-500 text-white text-xs font-semibold flex items-center justify-center"
          (click)="menuOpen.set(!menuOpen())"
        >
          {{ initials() }}
        </button>

        @if (menuOpen()) {
          <div class="absolute right-0 mt-xs w-40 card p-xs shadow-lg" (mouseleave)="menuOpen.set(false)">
            <a routerLink="/app/profile" class="block px-md py-sm text-sm text-neutral-700 hover:bg-neutral-100 rounded" (click)="menuOpen.set(false)">Profile</a>
            <a routerLink="/app/invitations" class="block px-md py-sm text-sm text-neutral-700 hover:bg-neutral-100 rounded" (click)="menuOpen.set(false)">Invitations</a>
            <button type="button" class="w-full text-left px-md py-sm text-sm text-neutral-700 hover:bg-neutral-100 rounded" (click)="logout()">Logout</button>
          </div>
        }
      </div>
    </header>
  `
})
export class TopbarComponent implements OnInit {
  paletteOpen = output<void>();
  menuToggle = output<void>();
  menuOpen = signal(false);
  orgMenuOpen = signal(false);
  organizations = signal<Organization[]>([]);

  constructor(
    private authService: AuthService,
    private organizationService: OrganizationService,
    protected currentOrg: CurrentOrganizationService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.organizationService.list().subscribe(orgs => this.organizations.set(orgs));
  }

  initials(): string {
    return 'U';
  }

  switchTo(org: Organization): void {
    this.currentOrg.set(org);
    this.orgMenuOpen.set(false);
    this.router.navigate(['/app/dashboard']);
  }

  logout(): void {
    this.authService.logout();
    window.location.href = '/';
  }
}
```

- [ ] **Step 2: Manually verify**

Run `ng serve` (from `ui/`, requires the API running too), log in with a user who has ≥1 org (the seeded/backfilled dev account from earlier work qualifies), confirm the org name + tier badge renders in the topbar, the dropdown lists every org with a checkmark on the active one, and "Manage organizations" is present (its target route doesn't exist yet — Task 10 adds it, a 404/redirect here is expected until then).

- [ ] **Step 3: Commit**

```bash
git add ui/src/app/shell/topbar.component.ts
git commit -m "feat: add organization quick-switch dropdown to the topbar"
```

---

### Task 10: `/app/organizations` list + create page

**Files:**
- Create: `ui/src/app/pages/organization/organizations-list.component.ts`
- Create: `ui/src/app/pages/organization/create-modal/create-organization-modal.component.ts`
- Modify: `ui/src/app/app.routes.ts`

**Interfaces:**
- Consumes: `OrganizationService.list()`/`.create()`, `CurrentOrganizationService.set()` (Tasks 7-8).

- [ ] **Step 1: Create the modal**

`ui/src/app/pages/organization/create-modal/create-organization-modal.component.ts`:

```typescript
import { Component, EventEmitter, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Organization, OrganizationService } from '../../../core/organization.service';

@Component({
  selector: 'app-create-organization-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-md" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">New organization</h2>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Name</label>
            <input class="input-field" type="text" name="name" [(ngModel)]="name" required />
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading()">
              {{ loading() ? 'Creating…' : 'Create' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class CreateOrganizationModalComponent {
  @Output() cancel = new EventEmitter<void>();
  @Output() created = new EventEmitter<Organization>();

  name = '';
  loading = signal(false);
  error = signal('');

  constructor(private organizationService: OrganizationService) {}

  submit(): void {
    this.error.set('');
    this.loading.set(true);
    this.organizationService.create(this.name).subscribe({
      next: (org) => {
        this.loading.set(false);
        this.created.emit(org);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not create organization.');
      }
    });
  }
}
```

- [ ] **Step 2: Create the list page**

`ui/src/app/pages/organization/organizations-list.component.ts`:

```typescript
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { Organization, OrganizationService } from '../../core/organization.service';
import { CurrentOrganizationService } from '../../core/current-organization.service';
import { CreateOrganizationModalComponent } from './create-modal/create-organization-modal.component';

@Component({
  selector: 'app-organizations-list',
  standalone: true,
  imports: [CommonModule, RouterLink, CreateOrganizationModalComponent],
  template: `
    <div class="p-lg max-w-4xl mx-auto">
      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Your organizations</h1>
        <button class="btn-primary" (click)="modalOpen.set(true)">New organization</button>
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (organizations().length === 0) {
        <div class="card text-center text-sm text-neutral-500">No organizations yet. Create one to get started.</div>
      } @else {
        <div class="grid gap-md sm:grid-cols-2">
          @for (org of organizations(); track org.id) {
            <div class="card">
              <button type="button" class="text-left w-full hover:opacity-80" (click)="switchTo(org)">
                <div class="flex items-center justify-between">
                  <span class="font-medium text-neutral-900">{{ org.name }}</span>
                  <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ org.tier }}</span>
                </div>
                <p class="text-xs text-neutral-500 mt-sm">{{ org.workspaceCount }} / {{ maxWorkspacesLabel(org) }} workspaces</p>
                <p class="text-xs text-neutral-500 mt-xs">Role: {{ org.callerRoles.join(', ') }}</p>
              </button>
              <a [routerLink]="['/app/organizations', org.id]" class="mt-sm inline-block text-xs text-primary-500 hover:text-primary-600">Manage</a>
            </div>
          }
        </div>
      }
    </div>

    @if (modalOpen()) {
      <app-create-organization-modal (cancel)="modalOpen.set(false)" (created)="onCreated($event)" />
    }
  `
})
export class OrganizationsListComponent implements OnInit {
  organizations = signal<Organization[]>([]);
  loading = signal(true);
  modalOpen = signal(false);

  constructor(
    private organizationService: OrganizationService,
    private currentOrg: CurrentOrganizationService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.organizationService.list().subscribe({
      next: (orgs) => { this.organizations.set(orgs); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  maxWorkspacesLabel(org: Organization): string {
    return org.maxWorkspaces >= 2147483647 ? '∞' : String(org.maxWorkspaces);
  }

  switchTo(org: Organization): void {
    this.currentOrg.set(org);
    this.router.navigate(['/app/dashboard']);
  }

  onCreated(org: Organization): void {
    this.modalOpen.set(false);
    this.organizations.update(list => [...list, org]);
    this.switchTo(org);
  }
}
```

(`2147483647` is `int.MaxValue`, the backend's "unlimited" sentinel for the Business tier — matches `Constants.OrganizationTiers.MaxWorkspaces[Business]` on the API side.)

- [ ] **Step 3: Add the route**

In `app.routes.ts`, import and add a sibling route to `workspace/:id` inside the `app` children array:

```typescript
import { OrganizationsListComponent } from './pages/organization/organizations-list.component';
```

```typescript
{ path: 'organizations', component: OrganizationsListComponent },
```

- [ ] **Step 4: Manually verify**

Run `ng serve` (from `ui/`), navigate to `/app/organizations`, confirm the grid renders, "New organization" opens the modal and creating one switches to it and lands on the dashboard, and the topbar's "Manage organizations" link (Task 9) now resolves correctly.

- [ ] **Step 5: Commit**

```bash
git add ui/src/app/pages/organization/organizations-list.component.ts ui/src/app/pages/organization/create-modal/create-organization-modal.component.ts ui/src/app/app.routes.ts
git commit -m "feat: add /app/organizations list and create page"
```

---

### Task 11: Organization settings page (rename, members, subscription)

**Files:**
- Create: `ui/src/app/pages/organization/organization-settings.component.ts`
- Modify: `ui/src/app/app.routes.ts`

**Interfaces:**
- Consumes: `OrganizationService.getById()`/`getMembers()`/`addMember()`/`removeMember()`/`updateSubscription()` (Task 7).

- [ ] **Step 1: Create the component**

`ui/src/app/pages/organization/organization-settings.component.ts`:

```typescript
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Organization, OrganizationMember, OrganizationService } from '../../core/organization.service';

@Component({
  selector: 'app-organization-settings',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="p-lg max-w-2xl mx-auto space-y-lg">
      <h1 class="text-lg font-semibold text-neutral-900">Organization settings</h1>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (organization(); as org) {
        <div class="card">
          <h2 class="text-sm font-semibold text-neutral-900 mb-xs">Subscription</h2>
          <p class="text-xs text-neutral-500 mb-md">
            {{ org.workspaceCount }} of {{ org.maxWorkspaces >= 2147483647 ? '∞' : org.maxWorkspaces }} workspaces used.
          </p>
          <div class="flex items-center gap-sm">
            <select class="input-field w-40" [(ngModel)]="selectedTier">
              <option value="Free">Free</option>
              <option value="Pro">Pro</option>
              <option value="Business">Business</option>
            </select>
            <button type="button" class="btn-primary" [disabled]="savingTier()" (click)="saveTier()">
              {{ savingTier() ? 'Saving…' : 'Update tier' }}
            </button>
          </div>
          @if (tierError()) {
            <p class="text-sm text-primary-500 mt-xs">{{ tierError() }}</p>
          }
        </div>

        <div class="card">
          <h2 class="text-sm font-semibold text-neutral-900 mb-md">Members</h2>
          @if (members().length === 0) {
            <p class="text-sm text-neutral-500">No members yet.</p>
          } @else {
            <div class="space-y-sm">
              @for (member of members(); track member.userId) {
                <div class="flex items-center justify-between text-sm">
                  <div>
                    <span class="text-neutral-900">{{ member.firstName }} {{ member.lastName }}</span>
                    <span class="text-neutral-500 ml-xs">{{ member.email }}</span>
                    @if (member.isOwner) {
                      <span class="text-xs px-xs py-[1px] rounded bg-neutral-100 text-neutral-600 ml-xs">Owner</span>
                    }
                  </div>
                  @if (!member.isOwner) {
                    <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="removeMember(member)">Remove</button>
                  }
                </div>
              }
            </div>
          }
        </div>
      }
    </div>
  `
})
export class OrganizationSettingsComponent implements OnInit {
  organizationId = '';
  organization = signal<Organization | null>(null);
  members = signal<OrganizationMember[]>([]);
  loading = signal(true);
  savingTier = signal(false);
  tierError = signal('');
  selectedTier = 'Free';

  constructor(
    private route: ActivatedRoute,
    private organizationService: OrganizationService
  ) {}

  ngOnInit(): void {
    this.organizationId = this.route.snapshot.paramMap.get('id') ?? '';
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.organizationService.getById(this.organizationId).subscribe({
      next: (org) => {
        this.organization.set(org);
        this.selectedTier = org.tier;
        this.organizationService.getMembers(this.organizationId).subscribe({
          next: (members) => { this.members.set(members); this.loading.set(false); },
          error: () => this.loading.set(false)
        });
      },
      error: () => this.loading.set(false)
    });
  }

  saveTier(): void {
    this.tierError.set('');
    this.savingTier.set(true);
    this.organizationService.updateSubscription(this.organizationId, this.selectedTier).subscribe({
      next: (org) => { this.organization.set(org); this.savingTier.set(false); },
      error: (err) => {
        this.savingTier.set(false);
        this.tierError.set(err.error?.message ?? 'Could not update subscription.');
      }
    });
  }

  removeMember(member: OrganizationMember): void {
    this.organizationService.removeMember(this.organizationId, member.userId).subscribe({
      next: () => this.members.update(list => list.filter(m => m.userId !== member.userId))
    });
  }
}
```

- [ ] **Step 2: Add the route**

In `app.routes.ts`:

```typescript
import { OrganizationSettingsComponent } from './pages/organization/organization-settings.component';
```

```typescript
{ path: 'organizations/:id', component: OrganizationSettingsComponent },
```

(Place this after the `organizations` route added in Task 10, same array.)

- [ ] **Step 3: Manually verify**

Run `ng serve`, navigate to `/app/organizations`, click "Manage" on an org card, confirm the settings page loads members and the tier selector, changing tier persists (reflected in the workspace-count/max display), removing a non-owner member updates the list.

- [ ] **Step 4: Commit**

```bash
git add ui/src/app/pages/organization/organization-settings.component.ts ui/src/app/app.routes.ts
git commit -m "feat: add organization settings page (subscription, members)"
```

---

### Task 12: Scope the dashboard to the active organization

**Files:**
- Modify: `ui/src/app/pages/dashboard/dashboard.component.ts`
- Modify: `ui/src/app/core/workspace.service.ts`
- Modify: `ui/src/app/core/job.service.ts`

**Interfaces:**
- Consumes: `organizationId`/`organizationName` on `Workspace` (Task 6's DTO), `organizationId` on `AccessibleJob` (Task 6's DTO), `CurrentOrganizationService.current` (Task 8).

- [ ] **Step 1: Add organizationId/organizationName to the Angular Workspace interface**

In `workspace.service.ts`, update the `Workspace` interface:

```typescript
export interface Workspace {
  workspaceId: string;
  name: string;
  description: string;
  createdAt: string;
  isActive: boolean;
  tier: string;
  roles: string[];
  organizationId: string;
  organizationName: string;
}
```

- [ ] **Step 2: Add organizationId to the Angular AccessibleJob interface**

In `job.service.ts`, update the `AccessibleJob` interface:

```typescript
export interface AccessibleJob {
  jobId: string;
  jobNumber: string;
  title: string;
  status: string;
  workspaceId: string;
  workspaceName: string;
  organizationId: string;
  accessScopeType: string;
}
```

- [ ] **Step 3: Scope the dashboard component**

In `dashboard.component.ts`, add the `CurrentOrganizationService` dependency and change `workspaces`/`directAccessJobs` from plain signals holding the raw fetch result into computed values filtered by the active org. Replace the relevant parts:

```typescript
import { CurrentOrganizationService } from '../../core/current-organization.service';
```

```typescript
export class DashboardComponent implements OnInit {
  allWorkspaces = signal<Workspace[]>([]);
  allJobs = signal<AccessibleJob[]>([]);
  loading = signal(true);
  modalOpen = signal(false);
  notFoundError = signal(false);
  viewMode = signal<ViewMode>('both');

  workspaceFilter = '';
  statusFilter = '';
  accessTypeFilter = '';

  workspaces = computed(() => {
    const orgId = this.currentOrg.current()?.id;
    return orgId ? this.allWorkspaces().filter(w => w.organizationId === orgId) : this.allWorkspaces();
  });

  jobs = computed(() => {
    const orgId = this.currentOrg.current()?.id;
    return orgId ? this.allJobs().filter(j => j.organizationId === orgId) : this.allJobs();
  });

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
    protected currentOrg: CurrentOrganizationService,
    private route: ActivatedRoute,
    private router: Router
  ) {}
```

Update `fetch()` to write into `allWorkspaces`/`allJobs` instead of `workspaces`/`jobs` (both are now `computed`, read-only):

```typescript
fetch(): void {
  this.loading.set(true);
  let remaining = 2;
  const done = () => { if (--remaining === 0) this.loading.set(false); };

  this.workspaceService.list().subscribe({
    next: (workspaces) => { this.allWorkspaces.set(workspaces); done(); },
    error: () => done()
  });
  this.jobService.getMine().subscribe({
    next: (jobs) => { this.allJobs.set(jobs); done(); },
    error: () => done()
  });
}
```

Update the "New workspace" flow to pass the active org into the modal — change the template's trigger and the modal's own inputs in Task 13, and change `onCreated` to remain as-is (it already navigates to the new workspace, which is correct regardless of org scoping).

- [ ] **Step 4: Manually verify**

Run `ng serve`, log in as a multi-org user, confirm the dashboard's workspace grid and jobs list only show entries under the active org, and switching org via the topbar (Task 9) changes what's shown without a page reload (the guard doesn't refetch dashboard data on org switch by itself — `switchTo()` in the topbar/organizations pages already navigates to `/app/dashboard`, which re-triggers `DashboardComponent.ngOnInit`'s `fetch()`).

- [ ] **Step 5: Commit**

```bash
git add ui/src/app/pages/dashboard/dashboard.component.ts ui/src/app/core/workspace.service.ts ui/src/app/core/job.service.ts
git commit -m "feat: scope dashboard workspace and job lists to the active organization"
```

---

### Task 13: Extend the create-workspace modal with an organization picker

**Files:**
- Modify: `ui/src/app/pages/workspace/create-modal/create-modal.component.ts`
- Modify: `ui/src/app/core/workspace.service.ts`

**Interfaces:**
- Consumes: `OrganizationService.list()`/`.create()` (Task 7), `CurrentOrganizationService.current` (Task 8).
- Produces: `WorkspaceService.create(name, description, organizationId)` — signature changes (drops the `tier` parameter, which the backend no longer accepts).

- [ ] **Step 1: Update WorkspaceService.create's signature**

In `workspace.service.ts`:

```typescript
create(name: string, description: string, organizationId: string): Observable<Workspace> {
  return this.http.post<ApiResponse<Workspace>>(this.apiUrl, { name, description, organizationId }).pipe(map(res => res.data));
}
```

- [ ] **Step 2: Rewrite the create-workspace modal**

Replace `create-modal.component.ts` in full:

```typescript
import { Component, EventEmitter, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Workspace, WorkspaceService } from '../../../core/workspace.service';
import { Organization, OrganizationService } from '../../../core/organization.service';
import { CurrentOrganizationService } from '../../../core/current-organization.service';

const NEW_ORG_VALUE = '__new__';

@Component({
  selector: 'app-create-workspace-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-md" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">New workspace</h2>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Organization</label>
            <select class="input-field" name="organization" [(ngModel)]="selectedOrgId">
              @for (org of organizations(); track org.id) {
                <option [value]="org.id">{{ org.name }}</option>
              }
              <option [value]="newOrgSentinel">+ Create new organization</option>
            </select>
          </div>

          @if (selectedOrgId === newOrgSentinel) {
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">New organization name</label>
              <input class="input-field" type="text" name="newOrgName" [(ngModel)]="newOrgName" required />
            </div>
          }

          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Name</label>
            <input class="input-field" type="text" name="name" [(ngModel)]="name" required />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Description</label>
            <textarea class="input-field" name="description" rows="3" [(ngModel)]="description"></textarea>
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading()">
              {{ loading() ? 'Creating…' : 'Create' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class CreateWorkspaceModalComponent implements OnInit {
  @Output() cancel = new EventEmitter<void>();
  @Output() created = new EventEmitter<Workspace>();

  newOrgSentinel = NEW_ORG_VALUE;
  organizations = signal<Organization[]>([]);
  selectedOrgId = '';
  newOrgName = '';

  name = '';
  description = '';
  loading = signal(false);
  error = signal('');

  constructor(
    private workspaceService: WorkspaceService,
    private organizationService: OrganizationService,
    private currentOrg: CurrentOrganizationService
  ) {}

  ngOnInit(): void {
    this.organizationService.list().subscribe(orgs => {
      this.organizations.set(orgs);
      const active = this.currentOrg.current();
      this.selectedOrgId = active && orgs.some(o => o.id === active.id)
        ? active.id
        : (orgs[0]?.id ?? this.newOrgSentinel);
    });
  }

  submit(): void {
    this.error.set('');
    this.loading.set(true);

    if (this.selectedOrgId === this.newOrgSentinel) {
      this.organizationService.create(this.newOrgName).subscribe({
        next: (org) => this.createWorkspace(org.id),
        error: (err) => {
          this.loading.set(false);
          this.error.set(err.error?.message ?? 'Could not create organization.');
        }
      });
      return;
    }

    this.createWorkspace(this.selectedOrgId);
  }

  private createWorkspace(organizationId: string): void {
    this.workspaceService.create(this.name, this.description, organizationId).subscribe({
      next: (workspace) => {
        this.loading.set(false);
        this.created.emit(workspace);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not create workspace.');
      }
    });
  }
}
```

- [ ] **Step 3: Manually verify**

Run `ng serve`, open "New workspace" from the dashboard: confirm the organization dropdown defaults to the active org, selecting "+ Create new organization" reveals the name field, and submitting either path creates the workspace and lands on it (existing `onCreated` behavior in `dashboard.component.ts` is unchanged).

- [ ] **Step 4: Commit**

```bash
git add ui/src/app/pages/workspace/create-modal/create-modal.component.ts ui/src/app/core/workspace.service.ts
git commit -m "feat: add organization picker to the create-workspace modal"
```

---

## Post-plan notes

- No `OrgAdmin` role — matches the already-shipped backend (`OrgOwner`/`OrgMember` only).
- No payment gateway — `updateSubscription` stays a bare tier field-flip, gated by `organization.manage_subscription` (already enforced server-side).
- The full test suite (`dotnet test` from `api/`, `ng test` from `ui/`) is the pre-merge gate for `finishing-a-development-branch` — not run again mid-plan beyond the two checkpoints already called out (Task 6 Step 10 for backend, and this note for the final gate).
