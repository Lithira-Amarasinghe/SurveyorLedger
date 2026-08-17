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
            RulesJson = "{\"ancestors\":[{\"scopeType\":\"Workspace\",\"grantRoleId\":\"" + _workspaceMemberRoleId + "\"}]}"
        };
        var singleScopePolicy = new AssignmentPolicy
        {
            Id = _singleScopePolicyId,
            Name = "SingleScope",
            RulesJson = "{\"ancestors\":[]}"
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

    [Fact(Skip = "Cascade direction TBD: child→parent vs parent→children")]
    public async Task RevokeAsync_FullRemoval_CascadesRevokeChainedRoles()
    {
        // Arrange: Grant Surveyor at Job (grants WorkspaceMember at Workspace via chain)
        await _service.GrantAsync(_userId, _surveyorRoleId, Constants.ScopeTypes.Job, _jobId, _assignedBy);

        // Act: Fully revoke all roles at Job scope
        await _service.RevokeAsync(_userId, Constants.ScopeTypes.Job, _jobId);

        // Assert: Surveyor revoked at Job
        var jobAccess = await _context.UserAccesses
            .FirstOrDefaultAsync(ua => ua.UserId == _userId && ua.ScopeType == Constants.ScopeTypes.Job &&
                                       ua.ScopeId == _jobId && ua.RoleId == _surveyorRoleId);
        Assert.NotNull(jobAccess);
        Assert.False(jobAccess.IsActive);

        // Assert: WorkspaceMember cascade-revoked at Workspace (chained role auto-revoked)
        var workspaceAccess = await _context.UserAccesses
            .FirstOrDefaultAsync(ua => ua.UserId == _userId && ua.ScopeType == Constants.ScopeTypes.Workspace &&
                                       ua.ScopeId == _workspaceId && ua.RoleId == _workspaceMemberRoleId);
        Assert.NotNull(workspaceAccess);
        Assert.False(workspaceAccess.IsActive);
    }

    [Fact(Skip = "Cascade direction TBD")]
    public async Task RevokeAsync_SingleRoleRevoke_NoAncestorCascade()
    {
        // Arrange: Grant Surveyor at Job
        await _service.GrantAsync(_userId, _surveyorRoleId, Constants.ScopeTypes.Job, _jobId, _assignedBy);

        // Act: Revoke only Surveyor (not full removal)
        await _service.RevokeAsync(_userId, Constants.ScopeTypes.Job, _jobId, _surveyorRoleId);

        // Assert: Surveyor revoked
        var jobAccess = await _context.UserAccesses
            .FirstOrDefaultAsync(ua => ua.UserId == _userId && ua.ScopeType == Constants.ScopeTypes.Job &&
                                       ua.ScopeId == _jobId && ua.RoleId == _surveyorRoleId);
        Assert.NotNull(jobAccess);
        Assert.False(jobAccess.IsActive);

        // Assert: WorkspaceMember NOT revoked (cascade only on full removal)
        var workspaceAccess = await _context.UserAccesses
            .FirstOrDefaultAsync(ua => ua.UserId == _userId && ua.ScopeType == Constants.ScopeTypes.Workspace &&
                                       ua.ScopeId == _workspaceId && ua.RoleId == _workspaceMemberRoleId);
        Assert.NotNull(workspaceAccess);
        Assert.True(workspaceAccess.IsActive);
    }

    [Fact(Skip = "Cascade direction TBD")]
    public async Task GrantAsync_Reactivate_ChainsAncestorAgain()
    {
        // Arrange: Grant Surveyor, then revoke, leaving chained WorkspaceMember inactive
        await _service.GrantAsync(_userId, _surveyorRoleId, Constants.ScopeTypes.Job, _jobId, _assignedBy);
        await _service.RevokeAsync(_userId, Constants.ScopeTypes.Job, _jobId, _surveyorRoleId);

        var workspaceAccess = await _context.UserAccesses
            .FirstOrDefaultAsync(ua => ua.UserId == _userId && ua.ScopeType == Constants.ScopeTypes.Workspace &&
                                       ua.ScopeId == _workspaceId && ua.RoleId == _workspaceMemberRoleId);
        Assert.False(workspaceAccess.IsActive);

        // Act: Re-grant Surveyor
        await _service.GrantAsync(_userId, _surveyorRoleId, Constants.ScopeTypes.Job, _jobId, _assignedBy);

        // Assert: WorkspaceMember reactivated (ancestor chain re-established)
        workspaceAccess = await _context.UserAccesses
            .FirstOrDefaultAsync(ua => ua.UserId == _userId && ua.ScopeType == Constants.ScopeTypes.Workspace &&
                                       ua.ScopeId == _workspaceId && ua.RoleId == _workspaceMemberRoleId);
        Assert.NotNull(workspaceAccess);
        Assert.True(workspaceAccess.IsActive);
    }
}
