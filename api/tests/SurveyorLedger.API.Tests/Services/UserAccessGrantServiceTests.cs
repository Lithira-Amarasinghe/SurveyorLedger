using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using Microsoft.Extensions.Logging;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>Tests for access chaining: when granting a job-scoped role, ancestor roles are auto-granted via policy.</summary>
public class UserAccessGrantServiceTests : IAsyncLifetime
{
    private readonly DbContextOptions<ApplicationDbContext> _dbContextOptions;
    private ApplicationDbContext _context = null!;
    private Mock<ICasbinService> _casbinServiceMock = null!;
    private Mock<IScopeIdResolver> _scopeIdResolverMock = null!;
    private Mock<ILogger<UserAccessGrantService>> _loggerMock = null!;
    private UserAccessGrantService _service = null!;

    private Guid _userId;
    private Guid _workspaceId;
    private Guid _jobId;
    private Guid _surveyorRoleId;
    private Guid _workspaceMemberRoleId;
    private Guid _adminRoleId;
    private Guid _fullChainPolicyId;
    private Guid _singleScopePolicyId;
    private Guid _assignedBy;

    public UserAccessGrantServiceTests()
    {
        _dbContextOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
            .Options;
    }

    public async Task InitializeAsync()
    {
        _context = new ApplicationDbContext(_dbContextOptions);
        await _context.Database.EnsureCreatedAsync();

        _userId = Guid.NewGuid();
        _workspaceId = Guid.NewGuid();
        _jobId = Guid.NewGuid();
        _assignedBy = Guid.NewGuid();
        _surveyorRoleId = Guid.NewGuid();
        _workspaceMemberRoleId = Guid.NewGuid();
        _adminRoleId = Guid.NewGuid();
        _fullChainPolicyId = Guid.NewGuid();
        _singleScopePolicyId = Guid.NewGuid();

        // Seed basic entities
        var person = new Person { Id = _userId, FirstName = "Test", LastName = "User", Email = "test@example.com", IsActive = true };
        var userAccount = new UserAccount { Id = _userId, PersonId = _userId, IsActive = true };
        var workspace = new Workspace { Id = _workspaceId, Name = "Test Workspace", IsActive = true };
        var job = new Job { Id = _jobId, WorkspaceId = _workspaceId, JobNumber = "JOB001", Title = "Test Job", CreatedBy = _assignedBy, IsActive = true };

        // Seed policies
        var fullChainPolicy = new AssignmentPolicy
        {
            Id = _fullChainPolicyId,
            Name = "FullChain",
            RulesJson = "{\"grants\":{\"Workspace\":\"" + _workspaceMemberRoleId + "\"}}"
        };
        var singleScopePolicy = new AssignmentPolicy
        {
            Id = _singleScopePolicyId,
            Name = "SingleScope",
            RulesJson = "{\"grants\":{}}"
        };

        // Seed roles
        var surveyorRole = new Role
        {
            Id = _surveyorRoleId,
            Name = "Surveyor",
            Description = "Surveyor role",
            IsSystem = true,
            PolicyId = _fullChainPolicyId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var workspaceMemberRole = new Role
        {
            Id = _workspaceMemberRoleId,
            Name = "WorkspaceMember",
            Description = "Workspace Member role",
            IsSystem = true,
            PolicyId = _singleScopePolicyId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var adminRole = new Role
        {
            Id = _adminRoleId,
            Name = "Admin",
            Description = "Admin role",
            IsSystem = true,
            PolicyId = _singleScopePolicyId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.People.Add(person);
        _context.UserAccounts.Add(userAccount);
        _context.Workspaces.Add(workspace);
        _context.Jobs.Add(job);
        _context.AssignmentPolicies.AddRange(fullChainPolicy, singleScopePolicy);
        _context.Roles.AddRange(surveyorRole, workspaceMemberRole, adminRole);
        await _context.SaveChangesAsync();

        // Seed scope hierarchy only if it doesn't exist
        if (!await _context.ScopeParentTypes.AnyAsync(x => x.ScopeType == Constants.ScopeTypes.Job))
        {
            var scopeParentType = new ScopeParentType { ScopeType = Constants.ScopeTypes.Job, ParentScopeType = Constants.ScopeTypes.Workspace };
            _context.ScopeParentTypes.Add(scopeParentType);
            await _context.SaveChangesAsync();
        }

        // Setup mocks
        _casbinServiceMock = new Mock<ICasbinService>();
        _casbinServiceMock.Setup(x => x.AddRoleForUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _casbinServiceMock.Setup(x => x.RemoveRoleForUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _scopeIdResolverMock = new Mock<IScopeIdResolver>();
        _scopeIdResolverMock
            .Setup(x => x.GetParentIdAsync(Constants.ScopeTypes.Job, _jobId))
            .ReturnsAsync(_workspaceId);
        _scopeIdResolverMock
            .Setup(x => x.GetParentIdAsync(Constants.ScopeTypes.Workspace, It.IsAny<Guid>()))
            .ReturnsAsync((Guid?)null);
        _scopeIdResolverMock
            .Setup(x => x.GetChildIdsAsync(Constants.ScopeTypes.Workspace, Constants.ScopeTypes.Job, _workspaceId))
            .ReturnsAsync(new List<Guid> { _jobId });
        // The rewritten grants-map walk resolves the real parent TYPE at each hop (not a fixed
        // array position) - by default this file's world has no Organization level, so Workspace
        // is the top. Individual tests below override this to add an Organization hop.
        _scopeIdResolverMock
            .Setup(x => x.GetParentScopeType(Constants.ScopeTypes.Job))
            .Returns(Constants.ScopeTypes.Workspace);
        _scopeIdResolverMock
            .Setup(x => x.GetParentScopeType(Constants.ScopeTypes.Workspace))
            .Returns((string?)null);

        _loggerMock = new Mock<ILogger<UserAccessGrantService>>();

        _service = new UserAccessGrantService(_context, _casbinServiceMock.Object, _scopeIdResolverMock.Object, _loggerMock.Object);
    }

    public async Task DisposeAsync()
    {
        await _context.Database.EnsureDeletedAsync();
        _context.Dispose();
    }

    [Fact]
    public async Task GrantAsync_FullChainPolicy_GrantsAncestorRole()
    {
        // Act: Grant Surveyor at Job scope (which has FullChain policy)
        await _service.GrantAsync(_userId, _surveyorRoleId, Constants.ScopeTypes.Job, _jobId, _assignedBy);

        // Assert: User has Surveyor at Job
        var jobAccess = await _context.UserAccesses
            .FirstOrDefaultAsync(ua => ua.UserId == _userId && ua.ScopeType == Constants.ScopeTypes.Job &&
                                       ua.ScopeId == _jobId && ua.RoleId == _surveyorRoleId);
        Assert.NotNull(jobAccess);
        Assert.True(jobAccess.IsActive);

        // Assert: User auto-granted WorkspaceMember at Workspace (ancestor role from policy)
        var workspaceAccess = await _context.UserAccesses
            .FirstOrDefaultAsync(ua => ua.UserId == _userId && ua.ScopeType == Constants.ScopeTypes.Workspace &&
                                       ua.ScopeId == _workspaceId && ua.RoleId == _workspaceMemberRoleId);
        Assert.NotNull(workspaceAccess);
        Assert.True(workspaceAccess.IsActive);
    }

    [Fact]
    public async Task GrantAsync_NoAncestorGrant_WhenAlreadyHasRoleAtAncestor()
    {
        // Arrange: Pre-assign Admin role at Workspace
        var adminAccess = new UserAccess
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            RoleId = _adminRoleId,
            ScopeType = Constants.ScopeTypes.Workspace,
            ScopeId = _workspaceId,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = _assignedBy,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.UserAccesses.Add(adminAccess);
        await _context.SaveChangesAsync();

        // Act: Grant Surveyor at Job
        await _service.GrantAsync(_userId, _surveyorRoleId, Constants.ScopeTypes.Job, _jobId, _assignedBy);

        // Assert: User still has only Admin at Workspace (no duplicate WorkspaceMember)
        var workspaceAccesses = await _context.UserAccesses
            .Where(ua => ua.UserId == _userId && ua.ScopeType == Constants.ScopeTypes.Workspace &&
                         ua.ScopeId == _workspaceId && ua.IsActive)
            .ToListAsync();
        Assert.Single(workspaceAccesses);
        Assert.Equal(_adminRoleId, workspaceAccesses[0].RoleId);
    }

    [Fact]
    public async Task RevokeAsync_LastJobRemoved_WorkspaceMemberStaysSticky()
    {
        // Arrange: Grant Surveyor at Job (auto-grants WorkspaceMember at Workspace via chain)
        await _service.GrantAsync(_userId, _surveyorRoleId, Constants.ScopeTypes.Job, _jobId, _assignedBy);

        // Act: Revoke the Surveyor role at Job - the only job this user was ever assigned to
        await _service.RevokeAsync(_userId, Constants.ScopeTypes.Job, _jobId, _surveyorRoleId);

        // Assert: Surveyor revoked at Job (IgnoreQueryFilters - UserAccess has a global
        // IsActive filter, so a plain query would silently exclude the very row we're checking)
        var jobAccess = await _context.UserAccesses.IgnoreQueryFilters()
            .FirstOrDefaultAsync(ua => ua.UserId == _userId && ua.ScopeType == Constants.ScopeTypes.Job &&
                                       ua.ScopeId == _jobId && ua.RoleId == _surveyorRoleId);
        Assert.NotNull(jobAccess);
        Assert.False(jobAccess.IsActive);

        // Assert: WorkspaceMember stays active - auto-granted baseline access is sticky once
        // given, not tied to the specific job that first triggered it. An admin who wants it
        // gone removes it explicitly (WorkspaceService.RemoveMemberAsync or AddMemberRoleAsync).
        var workspaceAccess = await _context.UserAccesses
            .FirstOrDefaultAsync(ua => ua.UserId == _userId && ua.ScopeType == Constants.ScopeTypes.Workspace &&
                                       ua.ScopeId == _workspaceId && ua.RoleId == _workspaceMemberRoleId);
        Assert.NotNull(workspaceAccess);
        Assert.True(workspaceAccess.IsActive);
    }

    [Fact]
    public async Task RevokeAsync_DirectlyGrantedAncestorRole_NeverTouched()
    {
        // Arrange: user directly holds WorkspaceMember at Workspace (not chain-granted -
        // e.g. an admin picked it explicitly), then separately gets Surveyor at Job which
        // sees "already has a role at Workspace" and grants nothing new.
        var directAccess = new UserAccess
        {
            Id = Guid.NewGuid(), UserId = _userId, RoleId = _workspaceMemberRoleId,
            ScopeType = Constants.ScopeTypes.Workspace, ScopeId = _workspaceId,
            AssignedAt = DateTime.UtcNow, AssignedBy = _assignedBy, IsActive = true,
            IsChainGranted = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.UserAccesses.Add(directAccess);
        await _context.SaveChangesAsync();

        await _service.GrantAsync(_userId, _surveyorRoleId, Constants.ScopeTypes.Job, _jobId, _assignedBy);

        // Act: revoke Surveyor at Job entirely
        await _service.RevokeAsync(_userId, Constants.ScopeTypes.Job, _jobId, _surveyorRoleId);

        // Assert: WorkspaceMember is untouched - RevokeAsync never touches any scope other
        // than the one it was called on.
        var workspaceAccess = await _context.UserAccesses
            .FirstOrDefaultAsync(ua => ua.UserId == _userId && ua.ScopeType == Constants.ScopeTypes.Workspace &&
                                       ua.ScopeId == _workspaceId && ua.RoleId == _workspaceMemberRoleId);
        Assert.NotNull(workspaceAccess);
        Assert.True(workspaceAccess.IsActive);
        Assert.False(workspaceAccess.IsChainGranted);
    }

    [Fact]
    public async Task GrantAsync_Reactivate_ChainsAncestorAgain()
    {
        // Arrange: WorkspaceMember exists but inactive (e.g. an admin explicitly removed it
        // earlier via WorkspaceService), simulated directly since RevokeAsync no longer
        // touches ancestor scopes.
        var inactiveWorkspaceMember = new UserAccess
        {
            Id = Guid.NewGuid(), UserId = _userId, RoleId = _workspaceMemberRoleId,
            ScopeType = Constants.ScopeTypes.Workspace, ScopeId = _workspaceId,
            AssignedAt = DateTime.UtcNow, AssignedBy = _assignedBy, IsActive = false,
            IsChainGranted = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.UserAccesses.Add(inactiveWorkspaceMember);
        await _context.SaveChangesAsync();

        // Act: Grant Surveyor at Job - must reactivate the existing inactive row, not insert
        // a duplicate (ApplicationDbContext's global IsActive filter would otherwise hide it
        // from the lookup query and cause a second row to be created).
        await _service.GrantAsync(_userId, _surveyorRoleId, Constants.ScopeTypes.Job, _jobId, _assignedBy);

        // Assert: WorkspaceMember reactivated, exactly one row for (user, role, scope)
        var workspaceAccesses = await _context.UserAccesses.IgnoreQueryFilters()
            .Where(ua => ua.UserId == _userId && ua.ScopeType == Constants.ScopeTypes.Workspace &&
                         ua.ScopeId == _workspaceId && ua.RoleId == _workspaceMemberRoleId)
            .ToListAsync();
        Assert.Single(workspaceAccesses);
        Assert.True(workspaceAccesses[0].IsActive);
    }

    /// <summary>Seeds a role using a two-hop grants map (Workspace + Organization) and points
    /// the mocked resolver's Workspace hop at a real Organization, for the three tests below
    /// that verify the walk correctly reaches Organization.</summary>
    private async Task<(Guid RoleId, Guid OrgMemberRoleId, Guid OrgId)> SeedTwoHopRoleAsync()
    {
        var orgId = Guid.NewGuid();
        var orgMemberRoleId = Guid.NewGuid();
        var twoHopPolicyId = Guid.NewGuid();
        var twoHopRoleId = Guid.NewGuid();

        _context.AssignmentPolicies.Add(new AssignmentPolicy
        {
            Id = twoHopPolicyId,
            Name = "TwoHopTestPolicy",
            RulesJson = "{\"grants\":{\"Workspace\":\"" + _workspaceMemberRoleId + "\",\"Organization\":\"" + orgMemberRoleId + "\"}}"
        });
        _context.Roles.Add(new Role
        {
            Id = twoHopRoleId, Name = "TwoHopTestRole", Description = "test", IsSystem = false, PolicyId = twoHopPolicyId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        _scopeIdResolverMock.Setup(x => x.GetParentScopeType(Constants.ScopeTypes.Workspace)).Returns(Constants.ScopeTypes.Organization);
        _scopeIdResolverMock.Setup(x => x.GetParentScopeType(Constants.ScopeTypes.Organization)).Returns((string?)null);
        _scopeIdResolverMock.Setup(x => x.GetParentIdAsync(Constants.ScopeTypes.Workspace, _workspaceId)).ReturnsAsync(orgId);

        return (twoHopRoleId, orgMemberRoleId, orgId);
    }

    [Fact]
    public async Task GrantAsync_JobStart_GrantsMapWalksToWorkspaceThenOrganization()
    {
        var (roleId, orgMemberRoleId, orgId) = await SeedTwoHopRoleAsync();

        await _service.GrantAsync(_userId, roleId, Constants.ScopeTypes.Job, _jobId, _assignedBy);

        var workspaceGrant = await _context.UserAccesses.SingleOrDefaultAsync(ua =>
            ua.UserId == _userId && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == _workspaceId && ua.IsActive);
        Assert.NotNull(workspaceGrant);
        Assert.Equal(_workspaceMemberRoleId, workspaceGrant!.RoleId);

        var orgGrant = await _context.UserAccesses.SingleOrDefaultAsync(ua =>
            ua.UserId == _userId && ua.ScopeType == Constants.ScopeTypes.Organization && ua.ScopeId == orgId && ua.IsActive);
        Assert.NotNull(orgGrant);
        Assert.Equal(orgMemberRoleId, orgGrant!.RoleId);
    }

    [Fact]
    public async Task GrantAsync_WorkspaceStart_SkipsWorkspaceHopGrantsOnlyOrganization()
    {
        // The same two-hop policy, but granted directly AT Workspace scope (mirrors
        // WorkspaceService.AddMemberRoleAsync) - must not create a corrupted UserAccess row at
        // Workspace scope using the Organization's guid as the scope id.
        var (roleId, orgMemberRoleId, orgId) = await SeedTwoHopRoleAsync();

        await _service.GrantAsync(_userId, roleId, Constants.ScopeTypes.Workspace, _workspaceId, _assignedBy);

        var corrupted = await _context.UserAccesses.AnyAsync(ua =>
            ua.UserId == _userId && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == orgId);
        Assert.False(corrupted);

        var orgGrant = await _context.UserAccesses.SingleOrDefaultAsync(ua =>
            ua.UserId == _userId && ua.ScopeType == Constants.ScopeTypes.Organization && ua.ScopeId == orgId && ua.IsActive);
        Assert.NotNull(orgGrant);
        Assert.Equal(orgMemberRoleId, orgGrant!.RoleId);
    }

    [Fact]
    public async Task ResolveTopAncestorAsync_WithStopAtScopeType_CapsWalkAtThatLevel()
    {
        var (roleId, _, _) = await SeedTwoHopRoleAsync();

        var top = await _service.ResolveTopAncestorAsync(
            Constants.ScopeTypes.Job, _jobId, roleId, stopAtScopeType: Constants.ScopeTypes.Workspace);

        Assert.NotNull(top);
        Assert.Equal(Constants.ScopeTypes.Workspace, top!.Value.ScopeType);
        Assert.Equal(_workspaceId, top.Value.ScopeId);
        Assert.Equal(_workspaceMemberRoleId, top.Value.RoleId);
    }
}
