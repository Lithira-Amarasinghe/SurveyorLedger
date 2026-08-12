using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Configurations;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// Shared bootstrap for integration tests that exercise real services against a real
/// (throwaway) LocalDB - the RBAC/scope logic under test spans EF query behavior and
/// Casbin's in-memory enforcer together, which mocks can't represent faithfully. Each
/// test class gets its own database (name derived from the class), created fresh and
/// dropped afterward. Seeds one workspace with an Admin, a Surveyor, and a Client member.
/// </summary>
public abstract class WorkspaceIntegrationTestBase : IAsyncLifetime
{
    private readonly string _databaseName = $"SurveyorLedgerTest_{Guid.NewGuid():N}";
    private ServiceProvider _provider = null!;

    protected ApplicationDbContext Context { get; private set; } = null!;
    protected IUserAccessGrantService GrantService { get; private set; } = null!;
    protected ICasbinService CasbinService { get; private set; } = null!;

    protected Guid WorkspaceId { get; private set; }
    protected Guid AdminId { get; private set; }
    protected Guid SurveyorId { get; private set; }
    protected Guid ClientId { get; private set; }

    /// <summary>Register additional services needed by the concrete test class.</summary>
    protected virtual void ConfigureServices(IServiceCollection services) { }

    protected T GetService<T>() where T : notnull => _provider.GetRequiredService<T>();

    public async Task InitializeAsync()
    {
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={_databaseName};Integrated Security=true;";

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connectionString));
        services.AddSingleton<ICasbinService, CasbinService>();
        services.AddScoped<IUserAccessGrantService, UserAccessGrantService>();
        services.AddScoped<IScopedAccessService, ScopedAccessService>();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        ConfigureServices(services);

        _provider = services.BuildServiceProvider();
        Context = _provider.GetRequiredService<ApplicationDbContext>();
        await Context.Database.EnsureCreatedAsync(); // schema + HasData seed (Roles/Permissions/RolePermissions)

        CasbinService = _provider.GetRequiredService<ICasbinService>();
        await CasbinService.InitializeAsync();

        GrantService = _provider.GetRequiredService<IUserAccessGrantService>();

        await SeedWorkspaceAndMembersAsync();
    }

    public async Task DisposeAsync()
    {
        await Context.Database.EnsureDeletedAsync();
        await _provider.DisposeAsync();
    }

    private async Task SeedWorkspaceAndMembersAsync()
    {
        WorkspaceId = Guid.NewGuid();
        AdminId = Guid.NewGuid();
        SurveyorId = Guid.NewGuid();
        ClientId = Guid.NewGuid();

        await Context.Workspaces.AddAsync(new Workspace
        {
            Id = WorkspaceId,
            Name = "Test Workspace",
            OwnerId = AdminId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        foreach (var (id, first) in new[] { (AdminId, "Admin"), (SurveyorId, "Surveyor"), (ClientId, "Client") })
        {
            await Context.Users.AddAsync(new User
            {
                Id = id,
                FirstName = first,
                LastName = "Person",
                Email = $"{first.ToLower()}@test.local",
                EmailVerified = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        await Context.SaveChangesAsync();

        await GrantService.GrantAsync(AdminId, RoleConfiguration.AdminRoleId, Constants.ScopeTypes.Workspace, WorkspaceId, AdminId);
        await GrantService.GrantAsync(SurveyorId, RoleConfiguration.SurveyorRoleId, Constants.ScopeTypes.Workspace, WorkspaceId, AdminId);
        // Client is job-scope only now - ClientId is a plain workspace Member here; tests
        // needing job-level Client access grant it explicitly via AddParticipantAsync.
        await GrantService.GrantAsync(ClientId, RoleConfiguration.MemberRoleId, Constants.ScopeTypes.Workspace, WorkspaceId, AdminId);
    }
}
