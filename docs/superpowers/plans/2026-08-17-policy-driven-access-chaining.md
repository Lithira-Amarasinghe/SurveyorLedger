# Policy-Driven Access Chaining Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make role assignment able to require presence of a least-privilege role at ancestor scopes (e.g. Surveyor at Job → auto-grants WorkspaceMember at Workspace), driven entirely by data so the rule set — or a future Organization scope level — can change without touching engine code.

**Architecture:** Extend the existing single choke point for grants/revokes, `UserAccessGrantService` (`api/src/SurveyorLedger.API/Services/UserAccessGrantService.cs`), with a policy-driven ancestor-chain step. Two small DI-registered resolver registries translate a scope's actual ID to its parent's/children's actual IDs. No existing call site changes.

**Tech Stack:** .NET 9, EF Core 9 (Code-First migrations), xUnit.

## Global Constraints

- Every tenant-scoped query filters by WorkspaceId — no exceptions.
- Migrations generated via `dotnet ef migrations add`, never hand-edited.
- `GrantAsync`/`RevokeAsync` signatures on `IUserAccessGrantService` do not change — existing callers (`JobService.AddParticipantAsync`, `InvitationService`, `WorkspaceController.RemoveMember`) must compile and behave identically except for the new chaining/cascade side effects.
- Client and Finance roles keep SingleScope (Job-only, no Workspace membership) — this is the just-shipped job-scoped-billing behavior (`docs/superpowers/specs/2026-08-16-job-scoped-billing-design.md`) and must not regress.
- Policy/PolicyId changes affect future `GrantAsync` calls only — no retroactive migration of existing `UserAccess` rows.
- No commits during execution — user commits at the end themselves.

---

### Task 1: Entities, EF configuration, DbContext wiring

**Files:**
- Create: `api/src/SurveyorLedger.Data/Entities/ScopeParentType.cs`
- Create: `api/src/SurveyorLedger.Data/Entities/AssignmentPolicy.cs`
- Modify: `api/src/SurveyorLedger.Data/Entities/Role.cs`
- Create: `api/src/SurveyorLedger.Data/Configurations/ScopeParentTypeConfiguration.cs`
- Create: `api/src/SurveyorLedger.Data/Configurations/AssignmentPolicyConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/RoleConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/ApplicationDbContext.cs`

**Interfaces:**
- Produces: `ScopeParentType { ScopeType (PK, string), ParentScopeType (string?) }`, `AssignmentPolicy { Id (Guid), Name (string), RulesJson (string) }`, `Role.PolicyId (Guid, required FK)`.

- [ ] **Step 1: Create `ScopeParentType` entity**

```csharp
namespace SurveyorLedger.Data.Entities;

/// <summary>Maps a scope type to its parent scope type - the one place the hierarchy shape
/// is declared for access-chaining purposes. Adding Organization above Workspace later is one
/// new row here, nothing else changes.</summary>
public class ScopeParentType
{
    public string ScopeType { get; set; }
    public string? ParentScopeType { get; set; }
}
```

- [ ] **Step 2: Create `AssignmentPolicy` entity**

```csharp
namespace SurveyorLedger.Data.Entities;

/// <summary>Data-driven rule for what happens at ancestor scopes when a role is granted.
/// RulesJson shape: { "ancestors": [ { "scopeType": "Workspace", "grantRoleId": "<guid>" } ] }
/// Ordered nearest-ancestor-first. Empty array = no chaining (single scope only).</summary>
public class AssignmentPolicy
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string RulesJson { get; set; }

    public ICollection<Role> Roles { get; set; } = new List<Role>();
}
```

- [ ] **Step 3: Add `PolicyId` to `Role`**

Edit `api/src/SurveyorLedger.Data/Entities/Role.cs` — add after `UpdatedAt`:

```csharp
    public Guid PolicyId { get; set; }
    public AssignmentPolicy Policy { get; set; }
```

- [ ] **Step 4: `ScopeParentTypeConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Core;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class ScopeParentTypeConfiguration : IEntityTypeConfiguration<ScopeParentType>
{
    public void Configure(EntityTypeBuilder<ScopeParentType> builder)
    {
        builder.HasKey(x => x.ScopeType);
        builder.Property(x => x.ScopeType).HasMaxLength(50);
        builder.Property(x => x.ParentScopeType).HasMaxLength(50);

        builder.HasData(
            new ScopeParentType { ScopeType = Constants.ScopeTypes.Job, ParentScopeType = Constants.ScopeTypes.Workspace }
        );
    }
}
```

- [ ] **Step 5: `AssignmentPolicyConfiguration`** (fixed GUIDs, following `RoleConfiguration`'s stable-migration pattern)

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class AssignmentPolicyConfiguration : IEntityTypeConfiguration<AssignmentPolicy>
{
    public static readonly Guid SingleScopeId = new("00000000-0000-0000-0000-000000000701");
    public static readonly Guid FullChainId = new("00000000-0000-0000-0000-000000000702");

    public void Configure(EntityTypeBuilder<AssignmentPolicy> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RulesJson).IsRequired();

        // WorkspaceMemberRoleId is defined in RoleConfiguration below (added same task) -
        // referenced here as a literal GUID string since HasData can't forward-reference
        // a static field defined later in the same task's edits without a build first;
        // both are fixed at "...0801" per Step 6.
        builder.HasData(
            new AssignmentPolicy
            {
                Id = SingleScopeId,
                Name = "SingleScope",
                RulesJson = "{\"ancestors\":[]}"
            },
            new AssignmentPolicy
            {
                Id = FullChainId,
                Name = "FullChain",
                RulesJson = "{\"ancestors\":[{\"scopeType\":\"Workspace\",\"grantRoleId\":\"00000000-0000-0000-0000-000000000801\"}]}"
            }
        );
    }
}
```

- [ ] **Step 6: Add `WorkspaceMember` role + `PolicyId` backfill for existing roles in `RoleConfiguration.cs`**

Add a new fixed GUID alongside the existing ones (`api/src/SurveyorLedger.Data/Configurations/RoleConfiguration.cs`):

```csharp
    public static readonly Guid WorkspaceMemberRoleId = new("00000000-0000-0000-0000-000000000801");
```

In `Configure(...)`, find the existing `builder.HasData(...)` call that seeds Admin/Surveyor/Client/Member/Finance. Add `PolicyId` to every existing seeded row (Client, Finance → `AssignmentPolicyConfiguration.SingleScopeId`; Admin, Surveyor, Member → `AssignmentPolicyConfiguration.FullChainId`), and add one new row:

```csharp
            new Role
            {
                Id = WorkspaceMemberRoleId,
                Name = "WorkspaceMember",
                Description = "Least-privilege membership granted automatically when a role requires workspace-level presence",
                IsSystem = true,
                PolicyId = AssignmentPolicyConfiguration.SingleScopeId,
                CreatedAt = seededAt,
                UpdatedAt = seededAt
            }
```

(Match the exact property set/order the existing `HasData` rows use — read the current file before editing, this plan doesn't have its literal text.)

- [ ] **Step 7: Seed `WorkspaceMember`'s permission + scope**

In `api/src/SurveyorLedger.Data/Configurations/RolePermissionConfiguration.cs`, add inside the existing `builder.HasData(...)`:

```csharp
            Grant(new Guid("00000000-0000-0000-0000-000000000802"), RoleConfiguration.WorkspaceMemberRoleId, PermissionConfiguration.ViewWorkspaceId),
```

In `api/src/SurveyorLedger.Data/Configurations/RoleScopeConfiguration.cs`, add inside the existing `builder.HasData(...)`:

```csharp
            new RoleScope { RoleId = RoleConfiguration.WorkspaceMemberRoleId, ScopeType = Constants.ScopeTypes.Workspace }
```

- [ ] **Step 8: Wire DbSets**

In `api/src/SurveyorLedger.Data/ApplicationDbContext.cs`, add after the `RoleScopes` DbSet line:

```csharp
    public DbSet<ScopeParentType> ScopeParentTypes { get; set; }
    public DbSet<AssignmentPolicy> AssignmentPolicies { get; set; }
```

- [ ] **Step 9: Add unique index on `UserAccess`**

In `api/src/SurveyorLedger.Data/Configurations/UserAccessConfiguration.cs`, add (read the file first to place it alongside existing index calls, matching style):

```csharp
        builder.HasIndex(x => new { x.UserId, x.RoleId, x.ScopeType, x.ScopeId }).IsUnique();
```

If a non-unique index already exists on a subset of these columns, replace it rather than adding a second one.

- [ ] **Step 10: Build**

Run: `dotnet build` from `api/`
Expected: PASS (entities/config compile; migration not generated yet, so DB is out of sync — that's fine, next task's tests won't run yet).

- [ ] **Step 11: Commit reminder**

Do NOT commit — user commits at the end. Just verify build passes and move to Task 2.

---

### Task 2: Scope ID resolver registries

**Files:**
- Create: `api/src/SurveyorLedger.API/Services/IScopeIdResolver.cs`
- Modify: `api/src/SurveyorLedger.API/Program.cs`

**Interfaces:**
- Produces: `IScopeIdResolver.GetParentIdAsync(string scopeType, Guid scopeId): Task<(string ParentScopeType, Guid ParentScopeId)?>`, `IScopeIdResolver.GetChildIdsAsync(string scopeType, Guid scopeId): Task<List<(string ChildScopeType, Guid ChildScopeId)>>`.
- Consumes: `ApplicationDbContext.Jobs`, `ApplicationDbContext.ScopeParentTypes` (Task 1).

- [ ] **Step 1: Write `IScopeIdResolver` + implementation**

```csharp
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.Core;
using SurveyorLedger.Data;

namespace SurveyorLedger.API.Services;

/// <summary>
/// Translates a scope's actual ID to its actual parent's ID (and the reverse: a scope's
/// actual child IDs of a given type). ScopeParentType (DB) says WHICH scope type is whose
/// parent; the Func registries here say HOW to fetch the real ID, since that FK lives on
/// each scope's own entity (Job.WorkspaceId) and can't be derived from data alone.
/// Adding a new scope type (Organization) later is one new registry entry each - nothing
/// else in the access-chaining engine changes.
/// </summary>
public interface IScopeIdResolver
{
    Task<Guid?> GetParentIdAsync(string scopeType, Guid scopeId);
    Task<List<Guid>> GetChildIdsAsync(string parentScopeType, string childScopeType, Guid parentScopeId);
}

public class ScopeIdResolver : IScopeIdResolver
{
    private readonly ApplicationDbContext _context;

    private static readonly Dictionary<string, Func<ApplicationDbContext, Guid, Task<Guid?>>> ParentIdResolvers = new()
    {
        [Constants.ScopeTypes.Job] = async (ctx, jobId) =>
            (await ctx.Jobs.Where(j => j.Id == jobId).Select(j => (Guid?)j.WorkspaceId).FirstOrDefaultAsync())
    };

    private static readonly Dictionary<string, Func<ApplicationDbContext, Guid, Task<List<Guid>>>> ChildIdsResolvers = new()
    {
        [Constants.ScopeTypes.Workspace] = async (ctx, workspaceId) =>
            await ctx.Jobs.Where(j => j.WorkspaceId == workspaceId).Select(j => j.Id).ToListAsync()
    };

    public ScopeIdResolver(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Guid?> GetParentIdAsync(string scopeType, Guid scopeId)
    {
        if (!ParentIdResolvers.TryGetValue(scopeType, out var resolver))
            return null;
        return await resolver(_context, scopeId);
    }

    /// <summary>childScopeType isn't used for lookup today (one child type per parent type),
    /// but is kept in the signature so a parent with multiple child scope types later doesn't
    /// need a signature change - only a resolver that branches on it.</summary>
    public async Task<List<Guid>> GetChildIdsAsync(string parentScopeType, string childScopeType, Guid parentScopeId)
    {
        if (!ChildIdsResolvers.TryGetValue(parentScopeType, out var resolver))
            return new List<Guid>();
        return await resolver(_context, parentScopeId);
    }
}
```

- [ ] **Step 2: Register in DI**

In `api/src/SurveyorLedger.API/Program.cs`, add near the existing `builder.Services.AddScoped<IUserAccessGrantService, UserAccessGrantService>();` line:

```csharp
builder.Services.AddScoped<IScopeIdResolver, ScopeIdResolver>();
```

- [ ] **Step 3: Build**

Run: `dotnet build` from `api/`
Expected: PASS

---

### Task 3: Ancestor-chain grant + generate/apply migration

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/UserAccessGrantService.cs`
- Create: `api/src/SurveyorLedger.Data/Migrations/<timestamp>_PolicyDrivenAccessChaining.cs` (generated, not hand-written)

**Interfaces:**
- Consumes: `IScopeIdResolver` (Task 2), `AssignmentPolicy.RulesJson` (Task 1).
- Produces: `GrantAsync` now also grants ancestor access per the role's policy; signature unchanged.

- [ ] **Step 1: Add `System.Text.Json` policy DTO + `EnsureAncestorChainAsync` to `UserAccessGrantService`**

Edit `api/src/SurveyorLedger.API/Services/UserAccessGrantService.cs`. Add near the top of the file (outside the class, or as a private nested record):

```csharp
file record AncestorStep(string ScopeType, Guid GrantRoleId);
file record PolicyRules(List<AncestorStep> Ancestors);
```

Add `IScopeIdResolver` to the constructor:

```csharp
public class UserAccessGrantService : IUserAccessGrantService
{
    private readonly ApplicationDbContext _context;
    private readonly ICasbinService _casbinService;
    private readonly IScopeIdResolver _scopeResolver;

    public UserAccessGrantService(ApplicationDbContext context, ICasbinService casbinService, IScopeIdResolver scopeResolver)
    {
        _context = context;
        _casbinService = casbinService;
        _scopeResolver = scopeResolver;
    }
```

Add the chaining step at the end of `GrantAsync`, before each `return`. Both the "new row" branch and the "existing row" branch must call it — replace:

```csharp
            access.Role = role;
            access.User = account;
            return access;
        }
```

with:

```csharp
            access.Role = role;
            access.User = account;
            await EnsureAncestorChainAsync(userId, role, scopeType, scopeId, assignedBy);
            return access;
        }
```

and replace the final `return existing;` with:

```csharp
        await EnsureAncestorChainAsync(userId, role, scopeType, scopeId, assignedBy);
        return existing;
```

Add the new private method (below `GrantAsync`, above `RevokeAsync` or after it — either is fine):

```csharp
    /// <summary>
    /// Walks the granted role's policy nearest-ancestor-first, granting the policy's
    /// least-privilege role at each ancestor scope the user doesn't already hold ANY active
    /// role at. Recurses through GrantAsync itself for the ancestor grant, so a future
    /// multi-level policy (e.g. WorkspaceMember also requiring Organization presence) chains
    /// further with zero changes here.
    /// </summary>
    private async Task EnsureAncestorChainAsync(Guid userId, Role role, string scopeType, Guid scopeId, Guid assignedBy)
    {
        var policy = await _context.AssignmentPolicies.FirstAsync(p => p.Id == role.PolicyId);
        var rules = System.Text.Json.JsonSerializer.Deserialize<PolicyRules>(policy.RulesJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var curScopeType = scopeType;
        var curScopeId = scopeId;

        foreach (var step in rules?.Ancestors ?? new List<AncestorStep>())
        {
            var parentId = await _scopeResolver.GetParentIdAsync(curScopeType, curScopeId);
            if (parentId == null)
                break;

            var hasAny = await _context.UserAccesses.AnyAsync(ua =>
                ua.UserId == userId && ua.IsActive && ua.ScopeType == step.ScopeType && ua.ScopeId == parentId.Value);

            if (!hasAny)
                await GrantAsync(userId, step.GrantRoleId, step.ScopeType, parentId.Value, assignedBy);

            curScopeType = step.ScopeType;
            curScopeId = parentId.Value;
        }
    }
```

- [ ] **Step 2: Generate migration**

Run (API project may not compile standalone if mid-refactor elsewhere — if it fails, use `SurveyorLedger.Data` as both `--project` and `--startup-project` instead, it has its own `IDesignTimeDbContextFactory`):

```bash
cd api && dotnet ef migrations add PolicyDrivenAccessChaining --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```

- [ ] **Step 3: Inspect the generated migration**

Read the generated `Up()`/`Down()`. Confirm it: creates `ScopeParentTypes`, `AssignmentPolicies` tables; adds `Role.PolicyId` as a required FK column with a default matching one of the seeded policy IDs for the `AddColumn` step (EF will need an initial value since existing Role rows can't have a null FK — if EF's generated migration doesn't supply one, add `defaultValue:` to the `AddColumn<Guid>` call for `PolicyId` pointing at `AssignmentPolicyConfiguration.FullChainId`, then let the `HasData` seed rows correct it per-role); inserts the seed rows (ScopeParentType, AssignmentPolicy, WorkspaceMember role + its RolePermission + RoleScope); replaces/adds the unique index on UserAccess. `Down()` reverses all of it cleanly.

- [ ] **Step 4: Apply migration**

Run: `dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API`
Expected: PASS, no errors.

- [ ] **Step 5: Build**

Run: `dotnet build` from `api/`
Expected: PASS

---

### Task 4: Cascade revoke

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/UserAccessGrantService.cs`

**Interfaces:**
- Consumes: `IScopeIdResolver.GetChildIdsAsync` (Task 2).
- Produces: `RevokeAsync` now cascades to child scopes when `roleId` is omitted; signature unchanged.

- [ ] **Step 1: Extend `RevokeAsync`**

Replace the body of `RevokeAsync` in `api/src/SurveyorLedger.API/Services/UserAccessGrantService.cs`:

```csharp
    public async Task RevokeAsync(Guid userId, string scopeType, Guid scopeId, Guid? roleId = null)
    {
        var accesses = await _context.UserAccesses
            .Include(ua => ua.Role)
            .Where(ua => ua.UserId == userId && ua.IsActive && ua.ScopeType == scopeType && ua.ScopeId == scopeId
                && (roleId == null || ua.RoleId == roleId))
            .ToListAsync();

        foreach (var access in accesses)
        {
            access.IsActive = false;
            access.UpdatedAt = DateTime.UtcNow;
            await SyncCasbinAsync(() => _casbinService.RemoveRoleForUserAsync(userId.ToString(), access.Role.Name, scopeId.ToString()));
        }

        await _context.SaveChangesAsync();

        // Full removal from this scope (not a single-role revoke) cascades to whatever the
        // user holds at any child scope underneath it - a Workspace membership going away
        // must take dependent Job access with it, or the chain the grant path enforces would
        // be silently violated on the way out.
        if (roleId == null)
        {
            var childScopeType = GetChildScopeType(scopeType);
            if (childScopeType != null)
            {
                var childIds = await _scopeResolver.GetChildIdsAsync(scopeType, childScopeType, scopeId);
                foreach (var childId in childIds)
                    await RevokeAsync(userId, childScopeType, childId);
            }
        }
    }

    /// <summary>Only one child scope type exists per parent today (Workspace -> Job). When a
    /// parent has multiple child scope types later, this becomes a lookup instead of a
    /// one-line map - GetChildIdsAsync's childScopeType parameter is already there for that.</summary>
    private static string? GetChildScopeType(string parentScopeType) =>
        parentScopeType == SurveyorLedger.Core.Constants.ScopeTypes.Workspace ? SurveyorLedger.Core.Constants.ScopeTypes.Job : null;
```

- [ ] **Step 2: Build**

Run: `dotnet build` from `api/`
Expected: PASS

---

### Task 5: Tests

**Files:**
- Modify or Create: `api/tests/SurveyorLedger.API.Tests/Services/UserAccessGrantServiceTests.cs` (check if it exists first; if not, create following the pattern of `InvoiceServiceTests.cs`'s `ConfigureServices`/`WorkspaceIntegrationTestBase` setup)

**Interfaces:**
- Consumes: `IUserAccessGrantService`, `IScopeIdResolver`, `ICasbinService` (test double or real, matching how `InvoiceServiceTests.cs` registers `IJobService`/`IPasswordService` etc. — read that file's `ConfigureServices` first for the exact pattern).

- [ ] **Step 1: Write failing test — FullChain grants ancestor**

```csharp
[Fact]
public async Task GrantAsync_FullChainRole_GrantsWorkspaceMemberAtWorkspace()
{
    var workspace = await CreateWorkspaceAsync();
    var job = await CreateJobAsync(workspace.Id);
    var targetAccount = await CreateUserAccountAsync();

    await GrantService.GrantAsync(targetAccount.Id, RoleConfiguration.SurveyorRoleId, Constants.ScopeTypes.Job, job.Id, AdminUserId);

    var workspaceAccess = await Context.UserAccesses.FirstOrDefaultAsync(ua =>
        ua.UserId == targetAccount.Id && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspace.Id && ua.IsActive);

    Assert.NotNull(workspaceAccess);
    Assert.Equal(RoleConfiguration.WorkspaceMemberRoleId, workspaceAccess!.RoleId);
}
```

(Adapt helper method names — `CreateWorkspaceAsync`/`CreateJobAsync`/`CreateUserAccountAsync`/`AdminUserId` — to whatever this test file's base class actually exposes; read it first.)

- [ ] **Step 2: Run, verify it fails** (no such behavior yet if Task 3 wasn't applied — should already pass at this point since Task 3 precedes this task; if it fails, Task 3 has a bug, fix there, not here)

- [ ] **Step 3: Write test — SingleScope role does NOT chain**

```csharp
[Fact]
public async Task GrantAsync_SingleScopeRole_DoesNotGrantWorkspaceAccess()
{
    var workspace = await CreateWorkspaceAsync();
    var job = await CreateJobAsync(workspace.Id);
    var targetAccount = await CreateUserAccountAsync();

    await GrantService.GrantAsync(targetAccount.Id, RoleConfiguration.ClientRoleId, Constants.ScopeTypes.Job, job.Id, AdminUserId);

    var workspaceAccess = await Context.UserAccesses.FirstOrDefaultAsync(ua =>
        ua.UserId == targetAccount.Id && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.IsActive);

    Assert.Null(workspaceAccess);
}
```

- [ ] **Step 4: Write test — user already has any role at ancestor scope, no duplicate stacked**

```csharp
[Fact]
public async Task GrantAsync_UserAlreadyAdminAtWorkspace_DoesNotStackWorkspaceMember()
{
    var workspace = await CreateWorkspaceAsync();
    var job = await CreateJobAsync(workspace.Id);
    var targetAccount = await CreateUserAccountAsync();
    await GrantService.GrantAsync(targetAccount.Id, RoleConfiguration.AdminRoleId, Constants.ScopeTypes.Workspace, workspace.Id, AdminUserId);

    await GrantService.GrantAsync(targetAccount.Id, RoleConfiguration.SurveyorRoleId, Constants.ScopeTypes.Job, job.Id, AdminUserId);

    var workspaceAccesses = await Context.UserAccesses
        .Where(ua => ua.UserId == targetAccount.Id && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.IsActive)
        .ToListAsync();

    Assert.Single(workspaceAccesses);
    Assert.Equal(RoleConfiguration.AdminRoleId, workspaceAccesses[0].RoleId);
}
```

- [ ] **Step 5: Write test — cascade revoke**

```csharp
[Fact]
public async Task RevokeAsync_FullRemovalAtWorkspace_CascadesToJobAccess()
{
    var workspace = await CreateWorkspaceAsync();
    var job = await CreateJobAsync(workspace.Id);
    var targetAccount = await CreateUserAccountAsync();
    await GrantService.GrantAsync(targetAccount.Id, RoleConfiguration.SurveyorRoleId, Constants.ScopeTypes.Job, job.Id, AdminUserId);

    await GrantService.RevokeAsync(targetAccount.Id, Constants.ScopeTypes.Workspace, workspace.Id);

    var jobAccess = await Context.UserAccesses.FirstOrDefaultAsync(ua =>
        ua.UserId == targetAccount.Id && ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == job.Id && ua.IsActive);

    Assert.Null(jobAccess);
}
```

- [ ] **Step 6: Run full test file**

Run: `dotnet test --filter UserAccessGrantServiceTests`
Expected: all PASS (4 new tests + any pre-existing ones in the file).

- [ ] **Step 7: Scoped regression check**

Run: `dotnet test --filter "JobServiceTests|InvitationServiceTests|WorkspaceServiceTests"`
Expected: PASS — confirms existing grant call sites still behave correctly with chaining active underneath.

---

### Task 6: Final verification

**Files:** none (verification only)

- [ ] **Step 1: Full backend build**

Run: `dotnet build` from `api/`
Expected: PASS, 0 errors, 0 warnings introduced.

- [ ] **Step 2: Scoped test run covering everything touched**

Run: `dotnet test --filter "UserAccessGrantServiceTests|JobServiceTests|InvitationServiceTests|WorkspaceServiceTests|InvoiceServiceTests|QuotationServiceTests"`
Expected: PASS — the last two confirm job-scoped billing's Client/Finance SingleScope behavior wasn't regressed.

- [ ] **Step 3: Manual smoke check (optional but recommended given RBAC blast radius)**

Via API or UI: assign a Surveyor to a Job in a workspace they have no prior access to → confirm they can now see that Workspace in their workspace list (WorkspaceMember grant) but only that one Job's detail, not others. Assign a Client to a Job the same way → confirm they do NOT gain workspace visibility.

- [ ] **Step 4: Report to user, do not commit**

Summarize what changed, point at the two doc files (spec + this plan), and stop — user commits when ready per their standing instruction.
