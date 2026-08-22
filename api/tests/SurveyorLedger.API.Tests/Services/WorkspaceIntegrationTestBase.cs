using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

    /// <summary>Person.Id behind the seeded Admin/Surveyor/Client UserAccount - needed wherever a
    /// field means Person (e.g. Land.OwnerId), not UserAccount.</summary>
    protected Guid AdminPersonId { get; private set; }
    protected Guid SurveyorPersonId { get; private set; }
    protected Guid ClientPersonId { get; private set; }

    /// <summary>Register additional services needed by the concrete test class.</summary>
    protected virtual void ConfigureServices(IServiceCollection services) { }

    protected T GetService<T>() where T : notnull => _provider.GetRequiredService<T>();

    /// <summary>Create a Person+UserAccount pair for testing. Returns the UserAccount.Id.</summary>
    protected async Task<Guid> CreateUserAccountAsync(string firstName, string lastName, string email)
    {
        var personId = Guid.NewGuid();
        var userAccountId = Guid.NewGuid();

        await Context.People.AddAsync(new Person
        {
            Id = personId,
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await Context.UserAccounts.AddAsync(new UserAccount
        {
            Id = userAccountId,
            PersonId = personId,
            EmailVerified = true,
            HasCompletedSignup = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await Context.SaveChangesAsync();
        return userAccountId;
    }

    public async Task InitializeAsync()
    {
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={_databaseName};Integrated Security=true;";

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connectionString));
        services.AddSingleton<ICasbinService, CasbinService>();
        services.AddScoped<IScopeLinkProvider, JobWorkspaceScopeLinkProvider>();
        services.AddScoped<IScopeIdResolver, ScopeIdResolver>();
        services.AddScoped<IUserAccessGrantService, UserAccessGrantService>();
        services.AddScoped<IScopedAccessService, ScopedAccessService>();
        // WorkspaceService needs this for the letterhead logo upload - registered here rather
        // than per test file since every concrete test class resolves WorkspaceService through
        // this same provider. A test file that also registers its own IFileStorageService
        // (for document/receipt tests) overrides these two harmlessly - last registration wins.
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-base-test-{Guid.NewGuid():N}")
                })
                .Build());
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

    /// <summary>Grants the seeded Client account job-scoped Client access on jobId (via the
    /// same AddParticipantAsync path real callers use - Client is job-scope only, see
    /// SeedWorkspaceAndMembersAsync), and returns their PersonId - the id
    /// InvoiceRequest.ClientId/QuotationRequest.ClientId expect. Requires the concrete test
    /// class to have registered IJobService in ConfigureServices.</summary>
    protected async Task<Guid> GrantClientBillingRoleAsync(Guid jobId)
    {
        await GetService<IJobService>().AddParticipantAsync(WorkspaceId, AdminId, jobId, ClientId, "Client");
        return ClientPersonId;
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
        var organizationId = Guid.NewGuid();

        await Context.Organizations.AddAsync(new Organization
        {
            Id = organizationId,
            Name = "Test Organization",
            OwnerId = AdminId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await Context.OrganizationSubscriptions.AddAsync(new OrganizationSubscription
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Tier = Constants.OrganizationTiers.Free,
            Status = "Active",
            StartDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await Context.Workspaces.AddAsync(new Workspace
        {
            Id = WorkspaceId,
            Name = "Test Workspace",
            OwnerId = AdminId,
            OrganizationId = organizationId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        foreach (var (userAccountId, first) in new[] { (AdminId, "Admin"), (SurveyorId, "Surveyor"), (ClientId, "Client") })
        {
            var personId = Guid.NewGuid();
            await Context.People.AddAsync(new Person
            {
                Id = personId,
                FirstName = first,
                LastName = "Person",
                Email = $"{first.ToLower()}@test.local",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await Context.UserAccounts.AddAsync(new UserAccount
            {
                Id = userAccountId,
                PersonId = personId,
                EmailVerified = true,
                HasCompletedSignup = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            if (userAccountId == AdminId) AdminPersonId = personId;
            else if (userAccountId == SurveyorId) SurveyorPersonId = personId;
            else ClientPersonId = personId;
        }
        await Context.SaveChangesAsync();

        await GrantService.GrantAsync(AdminId, RoleConfiguration.AdminRoleId, Constants.ScopeTypes.Workspace, WorkspaceId, AdminId);
        await GrantService.GrantAsync(SurveyorId, RoleConfiguration.SurveyorRoleId, Constants.ScopeTypes.Workspace, WorkspaceId, AdminId);
        // Client is job-scope only now - ClientId is a plain workspace Member here; tests
        // needing job-level Client access grant it explicitly via AddParticipantAsync.
        await GrantService.GrantAsync(ClientId, RoleConfiguration.MemberRoleId, Constants.ScopeTypes.Workspace, WorkspaceId, AdminId);
    }
}
