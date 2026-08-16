# Person / UserAccount Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `User` into `Person` (identity: name/email/phone/address) and `UserAccount` (login credential), retire `Client` into `Person`, and repoint every FK per the approved spec, with a clean-slate EF migration (no backfill).

**Architecture:** `Person` is a global, workspace-agnostic identity table; `UserAccount` holds exactly one optional login per `Person` via a unique `PersonId` FK. Every FK that means "this real person is associated with a record" (creator, uploader, payee, invitee, client) now points at `Person.Id`; every FK that means "this authenticated session/permission grant" (`UserAccess`, `AuthToken`, `AuditLog`, `Workspace.Owner`, `Invitation.InvitedBy`) points at `UserAccount.Id`. Casbin/JWT subject stays a `Guid.ToString()` â€” its meaning shifts from `User.Id` to `UserAccount.Id`, no code change needed in Casbin itself.

**Tech Stack:** .NET 9, EF Core 9, SQL Server (LocalDB), Casbin.NET 2.0, Angular 21, xUnit + Testcontainers-style LocalDB integration tests.

## Global Constraints
- Clean layers: Controllers â†’ Services â†’ Data. Entities in `SurveyorLedger.Data`, DTOs in `SurveyorLedger.API`.
- Multi-tenant query filters / soft-delete (`IsActive`) patterns must be preserved on every entity that has them today.
- Migrations are always `dotnet ef migrations add` output â€” never hand-edited after generation (per `.claude/rules.md` and the migration-check skill).
- Dev-only DB: this migration drops `Users`/`Clients` and regenerates â€” no backfill logic, no data preservation.
- Auth/RBAC changes get extra scrutiny â€” Casbin subject semantics change (now `UserAccount.Id`), so every `CallerId()`/`ClaimTypes.NameIdentifier` call site must be audited, not assumed.
- No behavior change to the invitation flow â€” `CreateScopedInvitationAsync`'s eager-create-without-password semantics must be preserved 1:1, just retargeted at `Person`.
- `Invoice.ClientId`/`Quotation.ClientId` â†’ `Person.Id` is transitional (per spec) â€” do not attempt to build proper billing access control here, that's a follow-up spec.
- Per-change verification only: `dotnet build`, `dotnet test --filter <ClassName>`, `npx tsc --noEmit -p tsconfig.app.json` after each task â€” not the full suite every time.
- Don't commit until the user explicitly asks.

---

### Task 1: `Person` + `UserAccount` entities, configurations, DbSets, migration

**Files:**
- Create: `api/src/SurveyorLedger.Data/Entities/Person.cs`
- Create: `api/src/SurveyorLedger.Data/Entities/UserAccount.cs`
- Create: `api/src/SurveyorLedger.Data/Configurations/PersonConfiguration.cs`
- Create: `api/src/SurveyorLedger.Data/Configurations/UserAccountConfiguration.cs`
- Delete: `api/src/SurveyorLedger.Data/Entities/User.cs`
- Delete: `api/src/SurveyorLedger.Data/Entities/Client.cs`
- Delete: `api/src/SurveyorLedger.Data/Configurations/UserConfiguration.cs`
- Delete: `api/src/SurveyorLedger.Data/Configurations/ClientConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/ApplicationDbContext.cs`
- Create (generated): `api/src/SurveyorLedger.Data/Migrations/*_SplitUserIntoPersonAndUserAccount.cs`

This task only creates the new shape and repoints the FK-owning entities enough to compile+migrate; the *meaning* of `UserId` params inside services (Task 2+) comes later. To keep this task independently buildable, every entity FK in the sweep table is retargeted here in one pass (types only â€” property names on the entities stay the same, e.g. `Job.CreatedBy` stays `Guid CreatedBy` but its navigation `CreatedByUser` becomes type `Person`).

**Interfaces:**
- Produces: `Person { Guid Id; string FirstName; string LastName; string? Email; string? Phone; Address Address; bool IsActive; DateTime CreatedAt; DateTime UpdatedAt; }` plus inverse nav collections.
- Produces: `UserAccount { Guid Id; Guid PersonId; Person Person; string? PasswordHash; bool EmailVerified; DateTime? EmailVerifiedAt; bool HasCompletedSignup; int FailedLoginAttempts; DateTime? LockoutEndsAt; bool IsActive; DateTime CreatedAt; DateTime UpdatedAt; }`
- Consumes (by all later tasks): `ApplicationDbContext.People`, `ApplicationDbContext.UserAccounts`.

- [ ] **Step 1: Write the failing test (compile-level smoke test)**

```csharp
// api/tests/SurveyorLedger.API.Tests/Data/PersonUserAccountShapeTests.cs
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Data;

public class PersonUserAccountShapeTests
{
    [Fact]
    public void Person_HasIdentityFields_NoCredentialFields()
    {
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Ann",
            LastName = "Silva",
            Email = "ann@example.com",
            Phone = "0771234567",
            Address = new Address { City = "Colombo" },
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        Assert.Equal("Ann", person.FirstName);
        Assert.Null(person.GetType().GetProperty("PasswordHash"));
    }

    [Fact]
    public void UserAccount_RequiresPersonId_HasCredentialFields()
    {
        var personId = Guid.NewGuid();
        var account = new UserAccount
        {
            Id = Guid.NewGuid(),
            PersonId = personId,
            PasswordHash = "hash",
            EmailVerified = true,
            HasCompletedSignup = true,
            FailedLoginAttempts = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        Assert.Equal(personId, account.PersonId);
        Assert.Null(account.GetType().GetProperty("FirstName"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```
cd api && dotnet test --filter PersonUserAccountShapeTests
```
Expected: compile error â€” `Person`/`UserAccount` types don't exist yet.

- [ ] **Step 3: Write minimal implementation**

`api/src/SurveyorLedger.Data/Entities/Person.cs`:
```csharp
namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A real-world identity: name, contact info, address. No workspace scoping - the same
/// person can be a billing client of one workspace and a job participant of another
/// without duplicating their name/address. May or may not have a UserAccount (login).
/// </summary>
public class Person
{
    public Guid Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public Address Address { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public UserAccount? UserAccount { get; set; }
    public ICollection<Workspace> OwnedWorkspaces { get; set; } = new List<Workspace>();
}
```

`api/src/SurveyorLedger.Data/Entities/UserAccount.cs`:
```csharp
namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A login credential for a Person - "how they sign in". A Person may have zero or one
/// UserAccount. Email lives on Person only; login lookups join through Person.Email.
/// Casbin subject id and JWT NameIdentifier are this entity's Id, never Person.Id.
/// </summary>
public class UserAccount
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public string? PasswordHash { get; set; }
    public bool EmailVerified { get; set; }
    public DateTime? EmailVerifiedAt { get; set; }

    /// <summary>
    /// Whether this account has completed signup via any method (password set, or a future
    /// OAuth login linked) - distinct from PasswordHash != null, which only means "has a
    /// password" and would be permanently false for an OAuth-only account.
    /// </summary>
    public bool HasCompletedSignup { get; set; }

    /// <summary>Consecutive failed login attempts, reset on any successful login.</summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>Set while the account is temporarily locked after too many failures. Null when not locked.</summary>
    public DateTime? LockoutEndsAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Person Person { get; set; }
    public ICollection<UserAccess> UserAccesses { get; set; } = new List<UserAccess>();
    public ICollection<AuthToken> AuthTokens { get; set; } = new List<AuthToken>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
```

`api/src/SurveyorLedger.Data/Configurations/PersonConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Email).HasMaxLength(255);
        builder.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(30);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.OwnsOne(x => x.Address, a =>
        {
            a.Property(p => p.Street).HasMaxLength(255).HasColumnName("Street");
            a.Property(p => p.City).HasMaxLength(100).HasColumnName("City");
            a.Property(p => p.District).HasMaxLength(100).HasColumnName("District");
            a.Property(p => p.PostalCode).HasMaxLength(20).HasColumnName("PostalCode");
            a.Property(p => p.Country).HasMaxLength(100).HasColumnName("Country");
        });

        // rule: filtered unique index - SQL Server treats multiple NULLs as duplicates in a
        // plain unique index, so a Person without an email yet would collide on the 2nd null row.
        builder.HasIndex(x => x.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.IsActive);

        builder.HasMany(x => x.OwnedWorkspaces).WithOne(x => x.Owner).HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}
```
Note: `Workspace.Owner`/`OwnerId` becomes `UserAccount`, not `Person` (per the FK table). Remove `OwnedWorkspaces` from `Person` and put it on `UserAccount` instead â€” see the corrected `UserAccountConfiguration` below, which is authoritative.

`api/src/SurveyorLedger.Data/Configurations/UserAccountConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.PersonId).IsUnique();
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.IsActive);

        builder.HasOne(x => x.Person).WithOne(x => x.UserAccount)
            .HasForeignKey<UserAccount>(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.OwnedWorkspaces).WithOne(x => x.Owner).HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.UserAccesses).WithOne(x => x.User).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.AuthTokens).WithOne(x => x.User).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
```
(This supersedes the `OwnedWorkspaces` nav on `Person.cs` above â€” remove that line from `Person.cs`; `Workspace.Owner` is `UserAccount`.)

`Person.cs` corrected (final, no `OwnedWorkspaces`):
```csharp
namespace SurveyorLedger.Data.Entities;

public class Person
{
    public Guid Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public Address Address { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public UserAccount? UserAccount { get; set; }
}
```

Now retarget every entity in the FK sweep (property/nav types only â€” this is the mechanical repoint from the spec table). Existing property names (`UserId`, `CreatedBy`, `RecordedBy`, etc.) are unchanged; only the navigation's target type and the entity's `WithMany`/`HasForeignKey` config change.

`UserAccess.cs` â€” `public User User` â†’ `public UserAccount User { get; set; }` (property name `User` kept per spec "no signature changes, just what the Guid refers to" â€” but the type is `UserAccount`).

`AuthToken.cs` â€” `public User User` â†’ `public UserAccount User { get; set; }`.

`AuditLog.cs` â€” `public User? User` (nullable, matches nullable `UserId`) â†’ `public UserAccount? User { get; set; }`.

`Workspace.cs` â€” `public User Owner` â†’ `public UserAccount Owner { get; set; }`.

`Invitation.cs`:
```csharp
public Guid UserId { get; set; }        // invitee -> now means PersonId; keep name UserId per no-signature-change note, OR rename â€” see decision below
...
public Person User { get; set; }        // invitee: Person
public Role Role { get; set; }
public UserAccount InvitedByUser { get; set; }  // inviter: UserAccount
```
Decision: keep the C# property names `UserId`/`User`/`InvitedBy`/`InvitedByUser` unchanged (only their target type changes) â€” this matches "no signature changes" from the spec and avoids a second mechanical rename pass across `InvitationService`/`InvitationController`/DTOs that don't need to change otherwise.

`Job.cs` â€” `public User CreatedByUser` â†’ `public Person CreatedByUser { get; set; }`.
`Milestone.cs` â€” `CreatedByUser`, `CompletedByUser` â†’ both `Person`.
`Expense.cs` â€” `RecordedByUser` â†’ `Person`.
`Document.cs` â€” `UploadedByUser` â†’ `Person`.
`LandPhoto.cs` â€” `UploadedByUser` â†’ `Person`.
`DocumentRequest.cs` â€” `RequestedByUser`, `FulfilledByUser`, `TargetUser` â†’ all `Person`.
`StaffPayment.cs` â€” `User` (payee) and `RecordedByUser` â†’ both `Person`.
`Invoice.cs` â€” replace `public Client Client { get; set; }` with `public Person Client { get; set; }` (property name `Client` kept, type becomes `Person`, per spec's "transitional" note â€” a rename to `ClientPerson` is not requested and would ripple needlessly).
`Quotation.cs` â€” same: `public Person Client { get; set; }`.

Update every configuration accordingly (mechanical `.HasOne(x => x.XxxUser).WithMany().HasForeignKey(...)` â€” target generic changes from `User`/`Client` to `Person`/`UserAccount`, structure unchanged):

`UserAccessConfiguration.cs`, `AuthTokenConfiguration.cs`, `AuditLogConfiguration.cs`: change `IEntityTypeConfiguration<...>` unaffected (still keyed on `UserAccess`/`AuthToken`/`AuditLog`), only `.HasOne(x => x.User)` now resolves to `UserAccount` automatically via the nav type â€” no config line text changes needed beyond compiling against the new nav type.

`WorkspaceConfiguration.cs`: no explicit `HasOne(x => x.Owner)` exists today (the relationship is configured from `UserConfiguration`/now `UserAccountConfiguration` via `HasMany(x => x.OwnedWorkspaces)`) â€” no change needed here.

`InvitationConfiguration.cs`: `.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)` now targets `Person`; `.HasOne(x => x.InvitedByUser).WithMany().HasForeignKey(x => x.InvitedBy)` now targets `UserAccount`. No text change needed (nav types resolve it), just delete `using` if any specific `User` type reference existed (none).

`JobConfiguration.cs`, `MilestoneConfiguration.cs`, `ExpenseConfiguration.cs`, `DocumentConfiguration.cs`, `DocumentRequestConfiguration.cs`, `LandPhotoConfiguration.cs`, `StaffPaymentConfiguration.cs`: no line changes â€” every `.HasOne(x => x.XxxUser)` already resolves generically through the nav property, whose declared type changed in the entity file. Confirm by building (Step 4).

`InvoiceConfiguration.cs` / `QuotationConfiguration.cs`: `builder.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId)` â€” no line change (nav type on entity now `Person`), delete the `Client` entity's own config file.

`ApplicationDbContext.cs`:
```csharp
public DbSet<Person> People { get; set; }
public DbSet<UserAccount> UserAccounts { get; set; }
// remove: DbSet<User> Users, DbSet<Client> Clients
...
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

    modelBuilder.Entity<Person>().HasQueryFilter(x => x.IsActive);
    modelBuilder.Entity<UserAccount>().HasQueryFilter(x => x.IsActive);
    modelBuilder.Entity<Workspace>().HasQueryFilter(x => x.IsActive);
    modelBuilder.Entity<UserAccess>().HasQueryFilter(x => x.IsActive);
    modelBuilder.Entity<Land>().HasQueryFilter(x => x.IsActive);
    modelBuilder.Entity<Job>().HasQueryFilter(x => x.IsActive);
    modelBuilder.Entity<Milestone>().HasQueryFilter(x => x.IsActive);
    modelBuilder.Entity<Quotation>().HasQueryFilter(x => x.IsActive);
    modelBuilder.Entity<Invoice>().HasQueryFilter(x => x.IsActive);
    // Client filter removed - Client entity deleted
}
```

- [ ] **Step 4: Run test to verify it passes + generate migration**
```
cd api && dotnet build
dotnet test --filter PersonUserAccountShapeTests
dotnet ef migrations add SplitUserIntoPersonAndUserAccount --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```
Expected: build succeeds (once Tasks 2-9's compile-only renames of `User`â†’`Person`/`UserAccount` at call sites are also done â€” see note below), test passes, migration generated with `DropTable("Users")`, `DropTable("Clients")`, `CreateTable("People")`, `CreateTable("UserAccounts")`, and every FK column repointed. **Do not hand-edit the generated migration file** â€” if the generated migration looks wrong, fix the entity/config and regenerate.

Note: because `User`/`Client` are referenced from every service/controller/DTO in the repo, this task's `dotnet build` will not go green until Tasks 2-9 land their compile-fixing renames too. Treat Step 4's build check as "the Data project builds standalone" (`dotnet build src/SurveyorLedger.Data`) for this task; the full-solution build gate moves to the end of Task 9.

- [ ] **Step 5: Commit**
```
git add api/src/SurveyorLedger.Data
git commit -m "feat: add Person/UserAccount entities, retire User/Client shape, generate split migration"
```

---

### Task 2: `AuthService` rewrite (register/login/refresh/OTP/password-reset through the split)

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/AuthService.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/AuthServiceTests.cs` (create if absent, else extend)

**Interfaces:**
- Consumes: `ApplicationDbContext.People`, `ApplicationDbContext.UserAccounts` (Task 1).
- Produces: `IAuthService` â€” same method signatures as today but `User` â†’ tuple of `(Person, UserAccount)`:
```csharp
Task RegisterAsync(RegisterRequest request);
Task<(Person person, UserAccount account, string accessToken, string refreshToken, int expiresIn)> LoginAsync(LoginRequest request);
Task<(Person person, UserAccount account, string accessToken, string refreshToken, int expiresIn)> RefreshTokenAsync(string refreshToken);
Task LogoutAsync(string refreshToken);
Task VerifyOtpAsync(string email, string otpCode);
Task ResendOtpAsync(string email);
Task RequestPasswordResetAsync(string email);
Task ResetPasswordAsync(string email, string otpCode, string newPassword);
Task<Person?> GetPersonByEmailAsync(string email);
Task<(Person person, UserAccount account)?> GetAccountByIdAsync(Guid userAccountId);
Task<List<Person>> SearchPeopleAsync(string query);
Task<Person> UpdateProfileAsync(Guid userAccountId, Models.User.UpdateProfileRequest request);
```
This is a genuine signature change (tuple return replaces `User`), which downstream callers (`UserController`, `WorkspaceController`) must be updated for in Task 8 â€” noted there.

- [ ] **Step 1: Write the failing test**

```csharp
// api/tests/SurveyorLedger.API.Tests/Services/AuthServiceTests.cs
using SurveyorLedger.API.Models.Auth;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class AuthServiceTests : WorkspaceIntegrationTestBase
{
    protected override void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        services.AddScoped<IPasswordService, PasswordService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IEmailService, NoopEmailService>();
        services.AddScoped<IAuthService, AuthService>();
    }

    [Fact]
    public async Task Login_CreatesAccessTokenAndReturnsBothPersonAndAccount()
    {
        var authService = GetService<IAuthService>();
        var person = new SurveyorLedger.Data.Entities.Person
        {
            Id = Guid.NewGuid(), FirstName = "Nimal", LastName = "Perera", Email = "nimal@test.local",
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        var passwordService = GetService<IPasswordService>();
        var account = new SurveyorLedger.Data.Entities.UserAccount
        {
            Id = Guid.NewGuid(), PersonId = person.Id, PasswordHash = passwordService.HashPassword("Passw0rd!"),
            EmailVerified = true, HasCompletedSignup = true, IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        Context.People.Add(person);
        Context.UserAccounts.Add(account);
        await Context.SaveChangesAsync();

        var (loggedInPerson, loggedInAccount, accessToken, refreshToken, expiresIn) =
            await authService.LoginAsync(new LoginRequest { Email = "nimal@test.local", Password = "Passw0rd!" });

        Assert.Equal(person.Id, loggedInPerson.Id);
        Assert.Equal(account.Id, loggedInAccount.Id);
        Assert.NotEmpty(accessToken);
    }

    [Fact]
    public async Task Login_WithNoUserAccount_ThrowsInvalidCredentials()
    {
        var authService = GetService<IAuthService>();
        var person = new SurveyorLedger.Data.Entities.Person
        {
            Id = Guid.NewGuid(), FirstName = "Kamal", LastName = "Silva", Email = "kamal@test.local",
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        Context.People.Add(person);
        await Context.SaveChangesAsync();

        await Assert.ThrowsAsync<AppException>(() =>
            authService.LoginAsync(new LoginRequest { Email = "kamal@test.local", Password = "whatever" }));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```
cd api && dotnet test --filter AuthServiceTests
```
Expected: compile error against the still-`User`-shaped `IAuthService`.

- [ ] **Step 3: Write minimal implementation**

Full rewrite of `AuthService.cs`. Key changes vs. today's file (read in full above):

```csharp
public interface IAuthService
{
    Task RegisterAsync(RegisterRequest request);
    Task<(Person person, UserAccount account, string accessToken, string refreshToken, int expiresIn)> LoginAsync(LoginRequest request);
    Task<(Person person, UserAccount account, string accessToken, string refreshToken, int expiresIn)> RefreshTokenAsync(string refreshToken);
    Task LogoutAsync(string refreshToken);
    Task VerifyOtpAsync(string email, string otpCode);
    Task ResendOtpAsync(string email);
    Task RequestPasswordResetAsync(string email);
    Task ResetPasswordAsync(string email, string otpCode, string newPassword);
    Task<Person?> GetPersonByEmailAsync(string email);
    Task<(Person person, UserAccount account)?> GetAccountByIdAsync(Guid userAccountId);
    Task<List<Person>> SearchPeopleAsync(string query);
    Task<Person> UpdateProfileAsync(Guid userAccountId, Models.User.UpdateProfileRequest request);
}

public class AuthService : IAuthService
{
    private const string RegistrationTokenType = "Registration";
    private const string PasswordResetTokenType = "PasswordReset";
    private const string RefreshTokenType = "Refresh";

    private readonly ApplicationDbContext _context;
    private readonly IPasswordService _passwordService;
    private readonly ITokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthService> _logger;

    public AuthService(ApplicationDbContext context, IPasswordService passwordService, ITokenService tokenService,
        IEmailService emailService, IConfiguration config, ILogger<AuthService> logger)
    {
        _context = context; _passwordService = passwordService; _tokenService = tokenService;
        _emailService = emailService; _config = config; _logger = logger;
    }

    public async Task RegisterAsync(RegisterRequest request)
    {
        var email = request.Email.Trim();

        var existingPerson = await _context.People.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Email == email);
        if (existingPerson != null)
        {
            _logger.LogWarning("Registration attempted for existing email: {Email}", email);
            throw new AppException(Constants.ErrorCodes.UserAlreadyExists, "Email already registered");
        }

        var otpExpiryMinutes = GetOtpExpiryMinutes();
        var passwordHash = _passwordService.HashPassword(request.Password);
        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var pending = await _context.PendingRegistrations.FirstOrDefaultAsync(p => p.Email == email);
            if (pending == null)
            {
                pending = new PendingRegistration { Id = Guid.NewGuid(), Email = email, CreatedAt = DateTime.UtcNow };
                await _context.PendingRegistrations.AddAsync(pending);
            }
            pending.PasswordHash = passwordHash;
            pending.FirstName = firstName;
            pending.LastName = lastName;
            pending.ExpiresAt = DateTime.UtcNow.AddMinutes(otpExpiryMinutes);

            var otp = await IssueOtpAsync(email, RegistrationTokenType, otpExpiryMinutes);
            await _context.SaveChangesAsync();

            try
            {
                await _emailService.SendVerificationOtpAsync(email, otp, otpExpiryMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send OTP email to {Email} during registration - rolling back", email);
                throw new AppException(Constants.ErrorCodes.EmailSendFailed,
                    "Could not send the verification email. Please check the address and try again.", 502);
            }

            await transaction.CommitAsync();
        });

        _logger.LogInformation("Registration pending, awaiting OTP verification: {Email}", email);
    }

    public async Task<(Person person, UserAccount account, string accessToken, string refreshToken, int expiresIn)> LoginAsync(LoginRequest request)
    {
        // Login lookup joins through Person.Email -> UserAccount, per spec.
        var account = await _context.UserAccounts
            .Include(a => a.Person)
            .FirstOrDefaultAsync(a => a.IsActive && a.Person.Email == request.Email && a.Person.IsActive);

        if (account == null)
        {
            _logger.LogWarning("Login failed for email: {Email} - no such account", request.Email);
            throw new AppException(Constants.ErrorCodes.InvalidCredentials, "Invalid email or password");
        }

        if (account.LockoutEndsAt is DateTime lockedUntil && lockedUntil > DateTime.UtcNow)
        {
            var minutesLeft = (int)Math.Ceiling((lockedUntil - DateTime.UtcNow).TotalMinutes);
            _logger.LogWarning("Login blocked for {Email} - account locked for another {Minutes} minute(s)", request.Email, minutesLeft);
            throw new AppException(Constants.ErrorCodes.AccountLocked,
                $"Too many failed attempts. Try again in {minutesLeft} minute{(minutesLeft == 1 ? "" : "s")}.", 423);
        }

        if (account.PasswordHash == null || !_passwordService.VerifyPassword(request.Password, account.PasswordHash))
        {
            var maxAttempts = int.Parse(_config["Lockout:MaxFailedAttempts"] ?? "5");
            var lockoutMinutes = int.Parse(_config["Lockout:DurationMinutes"] ?? "15");

            account.FailedLoginAttempts++;
            if (account.FailedLoginAttempts >= maxAttempts)
            {
                account.LockoutEndsAt = DateTime.UtcNow.AddMinutes(lockoutMinutes);
                account.FailedLoginAttempts = 0;
                _logger.LogWarning("Account locked for {Email} after {MaxAttempts} failed attempts", request.Email, maxAttempts);
            }
            await _context.SaveChangesAsync();

            _logger.LogWarning("Login failed for email: {Email} - invalid credentials", request.Email);
            throw new AppException(Constants.ErrorCodes.InvalidCredentials, "Invalid email or password");
        }

        if (account.FailedLoginAttempts != 0 || account.LockoutEndsAt != null)
        {
            account.FailedLoginAttempts = 0;
            account.LockoutEndsAt = null;
        }

        if (!account.EmailVerified)
        {
            _logger.LogWarning("Login attempted with unverified email: {Email}", request.Email);
            throw new AppException(Constants.ErrorCodes.EmailNotVerified, "Email not verified. Please verify your email first.");
        }

        var (accessToken, refreshToken, expiresIn) = _tokenService.GenerateTokens(account.Id, account.Person.Email);
        await PersistRefreshTokenAsync(account.Id, refreshToken);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User logged in: {Email}", account.Person.Email);
        return (account.Person, account, accessToken, refreshToken, expiresIn);
    }

    public async Task<(Person person, UserAccount account, string accessToken, string refreshToken, int expiresIn)> RefreshTokenAsync(string refreshToken)
    {
        var hash = HashToken(refreshToken);

        var stored = await _context.AuthTokens
            .Include(t => t.User).ThenInclude(a => a.Person)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.TokenType == RefreshTokenType);

        if (stored == null)
        {
            _logger.LogWarning("Refresh attempted with an unknown token");
            throw new AppException(Constants.ErrorCodes.InvalidToken, "Invalid refresh token", 401);
        }

        if (stored.RevokedAt != null)
        {
            _logger.LogWarning("Refresh attempted with a revoked token for account {UserId} - revoking all sessions", stored.UserId);
            await RevokeAllRefreshTokensAsync(stored.UserId);
            await _context.SaveChangesAsync();
            throw new AppException(Constants.ErrorCodes.InvalidToken, "Invalid refresh token", 401);
        }

        if (stored.ExpiresAt <= DateTime.UtcNow)
        {
            _logger.LogInformation("Refresh attempted with an expired token for account {UserId}", stored.UserId);
            throw new AppException(Constants.ErrorCodes.TokenExpired, "Refresh token expired", 401);
        }

        if (!stored.User.IsActive)
            throw new AppException(Constants.ErrorCodes.InvalidToken, "Invalid refresh token", 401);

        var (accessToken, newRefreshToken, expiresIn) = _tokenService.GenerateTokens(stored.User.Id, stored.User.Person.Email);

        stored.RevokedAt = DateTime.UtcNow;
        await PersistRefreshTokenAsync(stored.User.Id, newRefreshToken);
        await _context.SaveChangesAsync();

        return (stored.User.Person, stored.User, accessToken, newRefreshToken, expiresIn);
    }

    public async Task LogoutAsync(string refreshToken)
    {
        var hash = HashToken(refreshToken);
        var stored = await _context.AuthTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.TokenType == RefreshTokenType && t.RevokedAt == null);
        if (stored == null) return;
        stored.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    private async Task PersistRefreshTokenAsync(Guid userAccountId, string refreshToken)
    {
        var refreshExpiryDays = int.Parse(_config["JwtSettings:RefreshTokenExpirationDays"] ?? "7");
        await _context.AuthTokens.AddAsync(new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = userAccountId,
            TokenType = RefreshTokenType,
            TokenHash = HashToken(refreshToken),
            ExpiresAt = DateTime.UtcNow.AddDays(refreshExpiryDays),
            CreatedAt = DateTime.UtcNow
        });
    }

    private async Task RevokeAllRefreshTokensAsync(Guid userAccountId)
    {
        var active = await _context.AuthTokens
            .Where(t => t.UserId == userAccountId && t.TokenType == RefreshTokenType && t.RevokedAt == null)
            .ToListAsync();
        foreach (var token in active) token.RevokedAt = DateTime.UtcNow;
    }

    private static string HashToken(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Verify OTP and complete registration by creating BOTH the Person and the UserAccount
    /// in one transaction (single new registration -> one identity, one login).
    /// </summary>
    public async Task VerifyOtpAsync(string email, string otpCode)
    {
        email = email.Trim();

        var verification = await _context.EmailVerifications
            .Where(e => e.Email == email && e.TokenType == RegistrationTokenType && e.VerifiedAt == null)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync();

        if (verification == null)
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "No pending OTP verification for this email");
        if (verification.ExpiresAt < DateTime.UtcNow)
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "OTP is expired");

        var maxAttempts = int.Parse(_config["OTP:MaxAttempts"] ?? "3");
        if (verification.Attempts >= maxAttempts)
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "Maximum OTP attempts exceeded");

        if (!_passwordService.VerifyPassword(otpCode, verification.OTPCodeHash))
        {
            verification.Attempts++;
            await _context.SaveChangesAsync();
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "OTP is invalid");
        }

        var pending = await _context.PendingRegistrations.FirstOrDefaultAsync(p => p.Email == email)
            ?? throw new AppException(Constants.ErrorCodes.RegistrationExpired,
                "Your registration session has expired. Please sign up again.", 410);

        var alreadyExists = await _context.People.IgnoreQueryFilters().AnyAsync(p => p.Email == email);
        if (alreadyExists)
        {
            _context.PendingRegistrations.Remove(pending);
            verification.VerifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            throw new AppException(Constants.ErrorCodes.UserAlreadyExists, "Email already registered", 409);
        }

        var person = new Person
        {
            Id = Guid.NewGuid(),
            Email = pending.Email,
            FirstName = pending.FirstName,
            LastName = pending.LastName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var account = new UserAccount
        {
            Id = Guid.NewGuid(),
            PersonId = person.Id,
            PasswordHash = pending.PasswordHash,
            EmailVerified = true,
            EmailVerifiedAt = DateTime.UtcNow,
            IsActive = true,
            HasCompletedSignup = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.People.AddAsync(person);
        await _context.UserAccounts.AddAsync(account);
        _context.PendingRegistrations.Remove(pending);
        verification.VerifiedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Registration completed for: {Email}", email);
    }

    public async Task ResendOtpAsync(string email)
    {
        email = email.Trim();
        var pending = await _context.PendingRegistrations.FirstOrDefaultAsync(p => p.Email == email);
        if (pending == null || pending.ExpiresAt < DateTime.UtcNow) return;

        var otpExpiryMinutes = GetOtpExpiryMinutes();
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var otp = await IssueOtpAsync(email, RegistrationTokenType, otpExpiryMinutes);

            var pendingInTx = await _context.PendingRegistrations.FirstOrDefaultAsync(p => p.Email == email)
                ?? throw new AppException(Constants.ErrorCodes.RegistrationExpired,
                    "Your registration session has expired. Please sign up again.", 410);
            pendingInTx.ExpiresAt = DateTime.UtcNow.AddMinutes(otpExpiryMinutes);
            await _context.SaveChangesAsync();

            try { await _emailService.SendVerificationOtpAsync(email, otp, otpExpiryMinutes); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send OTP resend email to {Email} - rolling back", email);
                throw new AppException(Constants.ErrorCodes.EmailSendFailed, "Could not send the verification email. Please try again.", 502);
            }
            await transaction.CommitAsync();
        });
    }

    public async Task<Person?> GetPersonByEmailAsync(string email) =>
        await _context.People.FirstOrDefaultAsync(p => p.Email == email && p.IsActive);

    public async Task<(Person person, UserAccount account)?> GetAccountByIdAsync(Guid userAccountId)
    {
        var account = await _context.UserAccounts.Include(a => a.Person)
            .FirstOrDefaultAsync(a => a.Id == userAccountId && a.IsActive);
        return account == null ? null : (account.Person, account);
    }

    public async Task<List<Person>> SearchPeopleAsync(string query)
    {
        var term = query.Trim();
        if (term.Length < 2) return new List<Person>();

        return await _context.People
            .Where(p => p.IsActive && (
                EF.Functions.Like(p.FirstName, $"%{term}%") ||
                EF.Functions.Like(p.LastName, $"%{term}%") ||
                (p.Email != null && EF.Functions.Like(p.Email, $"%{term}%"))))
            .OrderBy(p => p.FirstName).ThenBy(p => p.LastName)
            .Take(20)
            .ToListAsync();
    }

    public async Task<Person> UpdateProfileAsync(Guid userAccountId, Models.User.UpdateProfileRequest request)
    {
        var account = await _context.UserAccounts.Include(a => a.Person)
            .FirstOrDefaultAsync(a => a.Id == userAccountId && a.IsActive)
            ?? throw new AppException(Constants.ErrorCodes.UserNotFound, "User not found", 404);

        var person = account.Person;
        person.FirstName = request.FirstName.Trim();
        person.LastName = request.LastName.Trim();
        person.Phone = request.Phone?.Trim();
        person.Address = new Address
        {
            Street = request.Address?.Street, City = request.Address?.City, District = request.Address?.District,
            PostalCode = request.Address?.PostalCode, Country = request.Address?.Country
        };
        person.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return person;
    }

    public async Task RequestPasswordResetAsync(string email)
    {
        email = email.Trim();
        var account = await _context.UserAccounts.Include(a => a.Person)
            .FirstOrDefaultAsync(a => a.Person.Email == email && a.IsActive && a.Person.IsActive);
        if (account == null || account.PasswordHash == null) return;

        var otpExpiryMinutes = GetOtpExpiryMinutes();
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var otp = await IssueOtpAsync(email, PasswordResetTokenType, otpExpiryMinutes);
            await _context.SaveChangesAsync();
            try { await _emailService.SendPasswordResetOtpAsync(email, otp, otpExpiryMinutes); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email to {Email} - rolling back", email);
                throw new AppException(Constants.ErrorCodes.EmailSendFailed, "Could not send the reset email. Please try again.", 502);
            }
            await transaction.CommitAsync();
        });
    }

    public async Task ResetPasswordAsync(string email, string otpCode, string newPassword)
    {
        email = email.Trim();
        var verification = await _context.EmailVerifications
            .Where(e => e.Email == email && e.TokenType == PasswordResetTokenType && e.VerifiedAt == null)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync()
            ?? throw new AppException(Constants.ErrorCodes.InvalidOtp, "No pending password reset for this email");

        if (verification.ExpiresAt < DateTime.UtcNow)
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "OTP is expired");

        var maxAttempts = int.Parse(_config["OTP:MaxAttempts"] ?? "3");
        if (verification.Attempts >= maxAttempts)
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "Maximum OTP attempts exceeded");

        if (!_passwordService.VerifyPassword(otpCode, verification.OTPCodeHash))
        {
            verification.Attempts++;
            await _context.SaveChangesAsync();
            throw new AppException(Constants.ErrorCodes.InvalidOtp, "OTP is invalid");
        }

        var account = await _context.UserAccounts
            .FirstOrDefaultAsync(a => a.Person.Email == email && a.IsActive && a.Person.IsActive)
            ?? throw new AppException(Constants.ErrorCodes.InvalidOtp, "No pending password reset for this email");

        account.PasswordHash = _passwordService.HashPassword(newPassword);
        account.UpdatedAt = DateTime.UtcNow;
        verification.VerifiedAt = DateTime.UtcNow;
        account.FailedLoginAttempts = 0;
        account.LockoutEndsAt = null;

        await RevokeAllRefreshTokensAsync(account.Id);
        await _context.SaveChangesAsync();
    }

    private async Task<string> IssueOtpAsync(string email, string tokenType, int otpExpiryMinutes)
    {
        var cooldownSeconds = int.Parse(_config["OTP:ResendCooldownSeconds"] ?? "60");
        var existing = await _context.EmailVerifications
            .Where(e => e.Email == email && e.TokenType == tokenType && e.VerifiedAt == null)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync();

        if (existing?.LastSentAt is DateTime lastSent)
        {
            var cooldownEndsAt = lastSent.AddSeconds(cooldownSeconds);
            if (cooldownEndsAt > DateTime.UtcNow)
            {
                var waitSeconds = (int)Math.Ceiling((cooldownEndsAt - DateTime.UtcNow).TotalSeconds);
                throw new AppException(Constants.ErrorCodes.ResendCooldown,
                    $"Please wait {waitSeconds} second{(waitSeconds == 1 ? "" : "s")} before requesting another code.", 429);
            }
        }
        if (existing != null) _context.EmailVerifications.Remove(existing);

        var otp = GenerateOtp();
        var emailVerification = new EmailVerification
        {
            Id = Guid.NewGuid(), Email = email, OTPCodeHash = _passwordService.HashPassword(otp),
            TokenType = tokenType, ExpiresAt = DateTime.UtcNow.AddMinutes(otpExpiryMinutes),
            Attempts = 0, LastSentAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
        };
        await _context.EmailVerifications.AddAsync(emailVerification);
        return otp;
    }

    private int GetOtpExpiryMinutes() => int.Parse(_config["OTP:ExpirationMinutes"] ?? "3");
    private static string GenerateOtp() => new Random().Next(100000, 999999).ToString();
}
```

- [ ] **Step 4: Run test to verify it passes**
```
cd api && dotnet test --filter AuthServiceTests
```

- [ ] **Step 5: Commit**
```
git add api/src/SurveyorLedger.API/Services/AuthService.cs api/tests/SurveyorLedger.API.Tests/Services/AuthServiceTests.cs
git commit -m "feat: rewrite AuthService against Person/UserAccount split"
```

---

### Task 3: `InvitationService` rewrite (eager `Person`, `UserAccount` created at `CompleteInvitationAsync`)

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/InvitationService.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/InvitationServiceTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext.People`, `.UserAccounts`, `IUserAccessGrantService.GrantAsync(Guid userId, Guid roleId, string scopeType, Guid scopeId, Guid assignedBy)` (unchanged signature â€” `userId` now means `UserAccount.Id`).
- Produces: `IInvitationService` â€” same signatures as today (`Invitation.UserId` still names the invitee column, now targets `Person.Id`; `Invitation.InvitedBy` targets `UserAccount.Id`).

- [ ] **Step 1: Write the failing test**

```csharp
// api/tests/SurveyorLedger.API.Tests/Services/InvitationServiceTests.cs
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class InvitationServiceTests : WorkspaceIntegrationTestBase
{
    protected override void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        services.AddScoped<IEmailService, NoopEmailService>();
        services.AddScoped<IPasswordService, PasswordService>();
        services.AddScoped<IInvitationService, InvitationService>();
    }

    [Fact]
    public async Task CreateScopedInvitationAsync_ForNewEmail_CreatesPersonOnly_NoUserAccount()
    {
        var svc = GetService<IInvitationService>();
        var invitation = await svc.CreateScopedInvitationAsync(
            Constants.ScopeTypes.Workspace, WorkspaceId, RoleConfigurationRoleIds.MemberRoleId,
            "Test Workspace", AdminId, "newperson@test.local", "New", "Person", null, null);

        var person = await Context.People.FirstAsync(p => p.Id == invitation.UserId);
        Assert.Equal("newperson@test.local", person.Email);

        var hasAccount = await Context.UserAccounts.AnyAsync(a => a.PersonId == person.Id);
        Assert.False(hasAccount);
    }

    [Fact]
    public async Task CompleteInvitationAsync_CreatesUserAccountForExistingPerson()
    {
        var svc = GetService<IInvitationService>();
        var invitation = await svc.CreateScopedInvitationAsync(
            Constants.ScopeTypes.Workspace, WorkspaceId, RoleConfigurationRoleIds.MemberRoleId,
            "Test Workspace", AdminId, "complete@test.local", "Complete", "Me", null, null);

        await svc.CompleteInvitationAsync(invitation.Token, new SurveyorLedger.API.Models.Invitation.CompleteInvitationRequest
        {
            FirstName = "Complete", LastName = "Me", Password = "Passw0rd!"
        });

        var account = await Context.UserAccounts.FirstOrDefaultAsync(a => a.PersonId == invitation.UserId);
        Assert.NotNull(account);
        Assert.True(account!.HasCompletedSignup);
    }
}
```
(Uses `RoleConfigurationRoleIds` alias for `RoleConfiguration.MemberRoleId` used elsewhere in the test suite â€” reuse whatever the existing test files reference, e.g. `RoleConfiguration.MemberRoleId` directly, matching `WorkspaceIntegrationTestBase`'s own usage.)

- [ ] **Step 2: Run test to verify it fails**
```
cd api && dotnet test --filter InvitationServiceTests
```

- [ ] **Step 3: Write minimal implementation**

Rewrite `InvitationService.cs`. The interface is unchanged in shape; only the entity types inside change:

```csharp
public async Task<Invitation> CreateScopedInvitationAsync(
    string scopeType, Guid scopeId, Guid roleId, string displayName, Guid invitedByUserId,
    string email, string? firstName, string? lastName, string? phone, AddressDto? address)
{
    var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == roleId)
        ?? throw new AppException(Constants.ErrorCodes.ValidationFailed, "Role not found", 400);

    email = email.Trim();

    var targetPerson = await _context.People
        .FirstOrDefaultAsync(p => p.Email != null && p.Email.ToUpper() == email.ToUpper() && p.IsActive);

    if (targetPerson != null)
    {
        var account = await _context.UserAccounts.FirstOrDefaultAsync(a => a.PersonId == targetPerson.Id && a.IsActive);
        if (account != null)
        {
            var alreadyHasAccess = await _context.UserAccesses.AnyAsync(ua =>
                ua.UserId == account.Id && ua.IsActive && ua.ScopeType == scopeType && ua.ScopeId == scopeId);
            if (alreadyHasAccess)
                throw new AppException(Constants.ErrorCodes.AlreadyMember, "This person already has access at this scope.", 409);
        }
        // A Person with no UserAccount yet (e.g. an existing billing client, or a still-
        // pending invitee from a different scope) is a valid invite target - falls through
        // to reuse the existing Person row, same as before under the old User model.
    }
    else
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            throw new AppException(Constants.ErrorCodes.ValidationFailed, "FirstName and LastName are required for a new person.", 400);

        targetPerson = new Person
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Phone = phone?.Trim(),
            Address = new Address
            {
                Street = address?.Street, City = address?.City, District = address?.District,
                PostalCode = address?.PostalCode, Country = address?.Country
            },
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.People.AddAsync(targetPerson);
    }

    var existingPending = await _context.Invitations
        .Where(i => i.UserId == targetPerson.Id && i.ScopeType == scopeType &&
            i.ScopeId == scopeId && i.Status == "Pending")
        .ToListAsync();
    foreach (var stale in existingPending) stale.Status = "Revoked";

    var invitation = new Invitation
    {
        Id = Guid.NewGuid(),
        UserId = targetPerson.Id,
        Email = email,
        ScopeType = scopeType,
        ScopeId = scopeId,
        RoleId = role.Id,
        Token = Guid.NewGuid().ToString("N"),
        InvitedBy = invitedByUserId,
        ExpiresAt = DateTime.UtcNow.AddDays(7),
        Status = "Pending",
        CreatedAt = DateTime.UtcNow
    };

    invitation.EmailFailed = !await TrySendInviteEmailAsync(invitation, displayName);

    await _context.Invitations.AddAsync(invitation);
    AddAudit("InvitationCreated", "Invitation", invitation.Id,
        scopeType == Constants.ScopeTypes.Workspace ? scopeId : null, invitedByUserId, null, $"{email}:{role.Name}");
    await _context.SaveChangesAsync();

    return invitation;
}
```

`CompleteInvitationAsync` â€” the one genuinely new codepath (per spec: "today it updated the eagerly-created User in place; now it inserts a new UserAccount row"):

```csharp
public async Task CompleteInvitationAsync(string token, CompleteInvitationRequest request)
{
    var invitation = await LoadAcceptableInvitationAsync(i => i.Token == token);

    var person = await _context.People.FirstOrDefaultAsync(p => p.Id == invitation.UserId && p.IsActive)
        ?? throw new NotFoundException("Person not found");

    var existingAccount = await _context.UserAccounts.FirstOrDefaultAsync(a => a.PersonId == person.Id);
    if (existingAccount is { HasCompletedSignup: true })
        throw new AppException(Constants.ErrorCodes.UserAlreadyExists,
            "This account already has a password - log in and accept the invitation from there.", 409);

    person.FirstName = request.FirstName.Trim();
    person.LastName = request.LastName.Trim();
    if (request.Phone != null) person.Phone = request.Phone.Trim();
    if (request.Address != null)
    {
        person.Address = new Address
        {
            Street = request.Address.Street, City = request.Address.City, District = request.Address.District,
            PostalCode = request.Address.PostalCode, Country = request.Address.Country
        };
    }
    person.UpdatedAt = DateTime.UtcNow;

    if (existingAccount == null)
    {
        var account = new UserAccount
        {
            Id = Guid.NewGuid(),
            PersonId = person.Id,
            PasswordHash = _passwordService.HashPassword(request.Password),
            HasCompletedSignup = true,
            EmailVerified = true,
            EmailVerifiedAt = DateTime.UtcNow,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.UserAccounts.AddAsync(account);
    }
    else
    {
        existingAccount.PasswordHash = _passwordService.HashPassword(request.Password);
        existingAccount.HasCompletedSignup = true;
        existingAccount.EmailVerified = true;
        existingAccount.EmailVerifiedAt = DateTime.UtcNow;
        existingAccount.UpdatedAt = DateTime.UtcNow;
    }

    // Deliberately does NOT accept the invitation - same as before.
    await _context.SaveChangesAsync();
    _logger.LogInformation("Invitation {InvitationId} account set up for {Email}", invitation.Id, invitation.Email);
}
```

Everything else in `InvitationService.cs` (`CreateInvitationAsync`, `GetPendingInvitationsAsync`, `GetMyInvitationsAsync`, `RevokeInvitationAsync`, `ResendInvitationAsync`, `GetByTokenAsync`, `AcceptInvitationAsync`, `DeclineInvitationAsync`, `DeclineByTokenAsync`, `MarkDeclinedAsync`, `LoadAcceptableInvitationAsync`, `ExpireStaleAsync`, `TrySendInviteEmailAsync`, `AddAudit`) is copied unchanged â€” none of it touches `User`/`Person` types directly except through `invitation.UserId`/`invitation.InvitedBy` (still `Guid`s, unaffected). `GrantAndMarkAcceptedAsync` calls `_grantService.GrantAsync(invitation.UserId, ...)` â€” **this is a behavior-preserving detail to verify carefully**: `invitation.UserId` is the invitee's `Person.Id`, but `UserAccess.UserId` must be a `UserAccount.Id`. Fix:

```csharp
private async Task GrantAndMarkAcceptedAsync(Invitation invitation)
{
    var account = await _context.UserAccounts.FirstOrDefaultAsync(a => a.PersonId == invitation.UserId)
        ?? throw new AppException(Constants.ErrorCodes.ValidationFailed,
            "This person has not completed account setup yet.", 409);

    await _grantService.GrantAsync(account.Id, invitation.RoleId, invitation.ScopeType, invitation.ScopeId, invitation.InvitedBy);

    invitation.Status = "Accepted";
    AddAudit("InvitationAccepted", "Invitation", invitation.Id,
        invitation.ScopeType == Constants.ScopeTypes.Workspace ? invitation.ScopeId : null,
        account.Id, null, invitation.ScopeType);
    await _context.SaveChangesAsync();
}
```
This is the load-bearing fix implied by the spec's "no behavior change" claim: the spec states no path grants access before a password exists, which is exactly the invariant this lookup enforces (throws instead of silently granting to a nonexistent account). `AcceptInvitationAsync`'s `if (invitation.UserId != callerUserId)` check also needs updating â€” `callerUserId` is now a `UserAccount.Id` (the JWT subject), not a `Person.Id`, so it must compare against the `Person.Id` behind that account:

```csharp
public async Task<Invitation> AcceptInvitationAsync(Guid invitationId, Guid callerUserId)
{
    var invitation = await LoadAcceptableInvitationAsync(i => i.Id == invitationId);

    var callerAccount = await _context.UserAccounts.FirstOrDefaultAsync(a => a.Id == callerUserId)
        ?? throw new ForbiddenException("This invitation is for a different account.");
    if (invitation.UserId != callerAccount.PersonId)
        throw new ForbiddenException("This invitation is for a different account.");

    await GrantAndMarkAcceptedAsync(invitation);
    return invitation;
}
```
Same pattern for `DeclineInvitationAsync`'s `invitation.UserId != callerUserId` check, and `GetMyInvitationsAsync`'s `.Where(i => i.UserId == callerUserId)` â€” both must resolve `callerUserId` (a `UserAccount.Id`) to its `PersonId` first:

```csharp
public async Task<List<Invitation>> GetMyInvitationsAsync(Guid callerUserId)
{
    var callerAccount = await _context.UserAccounts.FirstOrDefaultAsync(a => a.Id == callerUserId);
    if (callerAccount == null) return new List<Invitation>();

    var invitations = await _context.Invitations
        .Include(i => i.Role)
        .Where(i => i.UserId == callerAccount.PersonId)
        .OrderByDescending(i => i.CreatedAt)
        .ToListAsync();

    var pending = invitations.Where(i => i.Status == "Pending").ToList();
    await ExpireStaleAsync(pending);
    return invitations;
}

public async Task DeclineInvitationAsync(Guid invitationId, Guid callerUserId)
{
    var invitation = await _context.Invitations.FirstOrDefaultAsync(i => i.Id == invitationId)
        ?? throw new NotFoundException("Invitation not found");

    var callerAccount = await _context.UserAccounts.FirstOrDefaultAsync(a => a.Id == callerUserId)
        ?? throw new ForbiddenException("This invitation is for a different account.");
    if (invitation.UserId != callerAccount.PersonId)
        throw new ForbiddenException("This invitation is for a different account.");

    await MarkDeclinedAsync(invitation);
}
```

- [ ] **Step 4: Run test to verify it passes**
```
cd api && dotnet test --filter InvitationServiceTests
```

- [ ] **Step 5: Commit**
```
git add api/src/SurveyorLedger.API/Services/InvitationService.cs api/tests/SurveyorLedger.API.Tests/Services/InvitationServiceTests.cs
git commit -m "feat: rewrite InvitationService for Person/UserAccount split, preserve invite-flow behavior"
```

---

### Task 4: `ScopedAccessService` / `UserAccessGrantService` â€” confirm `UserAccount.Id` semantics, add display-name join helper

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/UserAccessGrantService.cs`
- Modify: `api/src/SurveyorLedger.API/Services/ScopedAccessService.cs` (type-only; logic is `Guid`-based throughout, no behavior change)
- Test: `api/tests/SurveyorLedger.API.Tests/Services/UserAccessGrantServiceTests.cs` (extend existing if present, else create)

**Interfaces:**
- Consumes: `ApplicationDbContext.UserAccounts` (Task 1).
- Produces: `IUserAccessGrantService.GrantAsync(Guid userId, Guid roleId, string scopeType, Guid scopeId, Guid assignedBy)` â€” signature unchanged, `userId` param now documented as `UserAccount.Id`; `RevokeAsync` unchanged.

`ScopedAccessService.cs` needs **no code changes** â€” it is entirely `Guid`/SQL based (`_context.UserAccesses.Where(ua => ua.UserId == userId ...)`), never touches `.User` navigation directly. Confirm by grep after Task 1's rename lands (no `.User.FirstName`/`.User.Email` references exist in this file per the read above). Only its XML doc comments should be updated to say `UserAccount.Id` instead of "user" where ambiguous â€” a doc-only diff, not logic.

`UserAccessGrantService.cs` â€” the only real change is that `GrantAsync`'s internal lookups (`_context.Users.FirstAsync(u => u.Id == userId)`) must become `_context.UserAccounts.FirstAsync(a => a.Id == userId)`:

- [ ] **Step 1: Write the failing test**

```csharp
// api/tests/SurveyorLedger.API.Tests/Services/UserAccessGrantServiceTests.cs
using SurveyorLedger.Core;
using SurveyorLedger.Data.Configurations;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class UserAccessGrantServiceTests : WorkspaceIntegrationTestBase
{
    [Fact]
    public async Task GrantAsync_ResolvesUserAccountNav_NotPerson()
    {
        var access = await GrantService.GrantAsync(SurveyorId, RoleConfiguration.SurveyorRoleId,
            Constants.ScopeTypes.Job, Guid.NewGuid(), AdminId);

        Assert.Equal(SurveyorId, access.UserId);
        Assert.NotNull(access.User); // UserAccount nav, must be loaded
        Assert.IsType<SurveyorLedger.Data.Entities.UserAccount>(access.User);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```
cd api && dotnet test --filter UserAccessGrantServiceTests
```
Expected: compile error (`access.User` still typed `User` before Task 1's rename fully propagates) or a runtime `InvalidOperationException` from `_context.Users` no longer existing.

- [ ] **Step 3: Write minimal implementation**

```csharp
public async Task<UserAccess> GrantAsync(Guid userId, Guid roleId, string scopeType, Guid scopeId, Guid assignedBy)
{
    var role = await _context.Roles.FirstAsync(r => r.Id == roleId);

    var existing = await _context.UserAccesses
        .Include(ua => ua.User)
        .Include(ua => ua.Role)
        .FirstOrDefaultAsync(ua => ua.UserId == userId && ua.ScopeType == scopeType && ua.ScopeId == scopeId && ua.RoleId == roleId);

    if (existing == null)
    {
        var account = await _context.UserAccounts.FirstAsync(a => a.Id == userId);
        var access = new UserAccess
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = roleId,
            ScopeType = scopeType,
            ScopeId = scopeId,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = assignedBy,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.UserAccesses.AddAsync(access);
        await _context.SaveChangesAsync();
        await SyncCasbinAsync(() => _casbinService.AddRoleForUserAsync(userId.ToString(), role.Name, scopeId.ToString()));

        access.Role = role;
        access.User = account;
        return access;
    }

    var wasInactive = !existing.IsActive;
    existing.IsActive = true;
    existing.AssignedBy = assignedBy;
    existing.AssignedAt = DateTime.UtcNow;
    existing.UpdatedAt = DateTime.UtcNow;
    await _context.SaveChangesAsync();

    if (wasInactive)
        await SyncCasbinAsync(() => _casbinService.AddRoleForUserAsync(userId.ToString(), role.Name, scopeId.ToString()));

    return existing;
}
// RevokeAsync unchanged - never touches _context.Users.
```

- [ ] **Step 4: Run test to verify it passes**
```
cd api && dotnet test --filter UserAccessGrantServiceTests
```

- [ ] **Step 5: Commit**
```
git add api/src/SurveyorLedger.API/Services/UserAccessGrantService.cs
git commit -m "feat: repoint UserAccessGrantService to UserAccount"
```

---

### Task 5: `JobService` / `WorkspaceService` â€” actor fields become `Person`, access checks stay `UserAccount`

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/JobService.cs`
- Modify: `api/src/SurveyorLedger.API/Services/WorkspaceService.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Workspace/MemberResponse.cs`
- Test: `api/tests/SurveyorLedger.API.Tests/Services/WorkspaceServiceTests.cs`, `JobServiceTests.cs` (extend existing)

**Interfaces:**
- Consumes: `ApplicationDbContext.People`, `.UserAccounts`, `IAuthService.SearchPeopleAsync` semantics unaffected (JobService's own `_context.Users` lookup at line 210 becomes `_context.People`).
- Produces: `WorkspaceService.MemberInfo` record (rename from whatever record backs the tuple at line 17 â€” `Guid UserId, string Email, ...`) unchanged in shape, but `Email`/`FirstName`/`LastName` now sourced via `UserAccess.User` (an `UserAccount`) `.Person.Email/.FirstName/.LastName`.

- [ ] **Step 1: Write the failing test**

```csharp
// extend api/tests/SurveyorLedger.API.Tests/Services/WorkspaceServiceTests.cs
[Fact]
public async Task GetMembersAsync_ReturnsNamesSourcedFromPerson()
{
    var svc = GetService<IWorkspaceService>();
    var members = await svc.GetMembersAsync(WorkspaceId, AdminId);

    var admin = members.Single(m => m.UserId == AdminId);
    Assert.Equal("Admin", admin.FirstName);
    Assert.Equal("admin@test.local", admin.Email);
}
```

- [ ] **Step 2: Run test to verify it fails**
```
cd api && dotnet test --filter GetMembersAsync_ReturnsNamesSourcedFromPerson
```

- [ ] **Step 3: Write minimal implementation**

`JobService.cs` line 210-215 (the only direct `_context.Users` hit found):
```csharp
var targetPerson = await _context.People.FirstOrDefaultAsync(p => p.Id == targetPersonId && p.IsActive)
    ?? throw new NotFoundException("Person not found");
...
targetPerson.Email!, targetPerson.FirstName, targetPerson.LastName, targetPerson.Phone, null);
```
The surrounding call is invoking `_invitationService.CreateScopedInvitationAsync(...)` for a job participant with no consent coverage â€” that call already targets a `Person` by email lookup internally (Task 3), so this call site's `targetUserId`/`targetPersonId` param (whichever it's currently named â€” verify against the actual `AddParticipantAsync` signature before editing) must now resolve against `_context.People` instead of `_context.Users`, since the id being looked up here is a job participant identity, which under the split is a `Person.Id` (per the FK table, job assignment target for invite purposes is a person, matching `DocumentRequest.TargetUserId` â†’ `Person`).

Line 258's `.Include(ua => ua.User)` (on a `UserAccesses` query) stays as-is structurally but now loads a `UserAccount`; any subsequent `.User.FirstName`/`.User.Email` access in that method must be rewritten to `.User.Person.FirstName`/`.User.Person.Email` â€” add `.ThenInclude(a => a.Person)`:
```csharp
.Include(ua => ua.User).ThenInclude(a => a.Person)
```

`WorkspaceService.cs`:
```csharp
public record MemberInfo(
    Guid UserId, string Email, string FirstName, string LastName, List<string> Roles, DateTime AssignedAt, bool IsOwner,
    List<MemberScopeGrant> JobScopes);
```
Line 68 `OwnerId = userId` â€” unaffected (still a `Guid`, now semantically a `UserAccount.Id` since `Workspace.Owner` targets `UserAccount`).

Lines 184, 221 `.Include(ua => ua.User)` â†’ `.Include(ua => ua.User).ThenInclude(a => a.Person)`.

Lines 256-278 â€” every `first.User.Email`, `first.User.FirstName`, `first.User.LastName` becomes `first.User.Person.Email`, `first.User.Person.FirstName`, `first.User.Person.LastName`:
```csharp
new MemberInfo(
    first.UserId, first.User.Person.Email!, first.User.Person.FirstName, first.User.Person.LastName,
    g.Select(ua => ua.Role.Name).ToList(), first.AssignedAt, first.UserId == workspace.OwnerId,
    jobScopesByUser.GetValueOrDefault(first.UserId, new List<MemberScopeGrant>()));
```
(both occurrences, lines ~256 and ~275).

Lines 345, 392 `targetUserId == workspace.OwnerId` â€” unaffected (`Guid == Guid`, both now `UserAccount.Id`).

- [ ] **Step 4: Run test to verify it passes**
```
cd api && dotnet test --filter WorkspaceServiceTests
dotnet test --filter JobServiceTests
```

- [ ] **Step 5: Commit**
```
git add api/src/SurveyorLedger.API/Services/JobService.cs api/src/SurveyorLedger.API/Services/WorkspaceService.cs
git commit -m "feat: repoint JobService/WorkspaceService member lookups through Person"
```

---

### Task 6: Remaining business entities' FK repoint sweep (Milestone, Expense, Document, DocumentRequest, StaffPayment, LandPhoto) â€” service-layer joins

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/MilestoneService.cs`
- Modify: `api/src/SurveyorLedger.API/Services/ExpenseService.cs`
- Modify: `api/src/SurveyorLedger.API/Services/DocumentService.cs`
- Modify: `api/src/SurveyorLedger.API/Services/DocumentRequestService.cs`
- Modify: `api/src/SurveyorLedger.API/Services/StaffPaymentService.cs`
- Modify: `api/src/SurveyorLedger.API/Services/LandService.cs`
- Test: existing test files per service (`MilestoneServiceTests.cs`, `ExpenseServiceTests.cs`, `DocumentServiceTests.cs`, `DocumentRequestServiceTests.cs`, `StaffPaymentServiceTests.cs`, `LandServiceTests.cs` under `api/tests/SurveyorLedger.API.Tests/Services/`)

These six entities were mechanically identical in the spec table (all `â†’ Person` for the actor field, entity/config already retargeted in Task 1). The remaining work per service is: any `.Include(x => x.CreatedByUser)`/`.RecordedByUser`/`.UploadedByUser`/`.RequestedByUser`/`.FulfilledByUser`/`.TargetUser` navigation stays syntactically identical (nav now resolves to `Person`, no `.ThenInclude` needed since `Person` has no further nav to chase for display purposes), and any place that passed a `User`-typed variable to a constructor/DTO mapper now passes `Person`. Because none of these services were read in full above, this task's actual diff must be produced by:

1. `grep -n "CreatedByUser\|RecordedByUser\|UploadedByUser\|RequestedByUser\|FulfilledByUser\|TargetUser\b" <each file>` to find every touch point.
2. For each hit, confirm the surrounding code only reads `.FirstName`/`.LastName`/`.Email`/`.Id` off the nav (per the entity's spec-driven retarget, that's all `Person` exposes) â€” if so, **no service-layer code change is needed beyond what the compiler flags**, since the nav's declared type already changed in Task 1's entity edits.
3. Where a service constructs a `new User { ... }` to populate one of these actor fields inline (e.g. seeding, or a rare denormalized write) â€” repoint to `new Person { ... }` with matching field names (`FirstName`, `LastName`, `Email`, `Phone`, `Address`, `IsActive`, `CreatedAt`, `UpdatedAt` â€” drop `PasswordHash`/`EmailVerified`/`HasCompletedSignup` if present, those moved to `UserAccount`).

**Interfaces:**
- Consumes: `Person` navigation properties already established on `Milestone`, `Expense`, `Document`, `DocumentRequest`, `LandPhoto`, `StaffPayment` (Task 1).
- Produces: no new public interface â€” same service method signatures throughout (all take `Guid workspaceId, Guid callerId, ...` where `callerId` is a `UserAccount.Id` used only for permission checks via `ScopedAccessService`/`CasbinService`, never dereferenced as a `Person`).

- [ ] **Step 1: Write the failing test**

```csharp
// api/tests/SurveyorLedger.API.Tests/Services/MilestoneServiceTests.cs (extend existing)
[Fact]
public async Task CreateAsync_SetsCreatedByUser_AsPersonNotUserAccount()
{
    var jobId = await CreateTestJobAsync(); // existing helper in the test class, unmodified
    var svc = GetService<IMilestoneService>();

    var milestone = await svc.CreateAsync(WorkspaceId, AdminId, jobId, new MilestoneRequest
    {
        Title = "Site visit", SortOrder = 1
    });

    var loaded = await Context.Milestones.Include(m => m.CreatedByUser).FirstAsync(m => m.Id == milestone.Id);
    Assert.IsType<SurveyorLedger.Data.Entities.Person>(loaded.CreatedByUser);
    Assert.Equal("Admin", loaded.CreatedByUser.FirstName);
}
```
Apply the equivalent pattern (adjust entity/service names) as a new or extended test in each of `ExpenseServiceTests.cs`, `DocumentServiceTests.cs`, `DocumentRequestServiceTests.cs`, `StaffPaymentServiceTests.cs`, `LandServiceTests.cs` (for `LandPhoto.UploadedByUser`).

- [ ] **Step 2: Run test to verify it fails**
```
cd api && dotnet test --filter "MilestoneServiceTests|ExpenseServiceTests|DocumentServiceTests|DocumentRequestServiceTests|StaffPaymentServiceTests|LandServiceTests"
```
Expected: compile errors wherever these services assign `CreatedBy = callerId` and then separately `.Include(x => x.CreatedByUser)` against the now-`Person`-typed nav but still reference `.EmailVerified`/`.PasswordHash` on it anywhere (none should, per the spec's field split â€” this test exists to catch any such leftover reference).

- [ ] **Step 3: Write minimal implementation**

Because `CreatedBy`/`RecordedBy`/`UploadedBy`/`RequestedBy`/`FulfilledBy`/`TargetUserId`/`UserId` (payee) are all plain `Guid` columns unchanged by the split, and the constructors in each service (`new Milestone { CreatedBy = callerId, ... }` etc.) already just assign the `Guid`, **the only required code change is that `callerId` passed into these `CreatedBy`/`RecordedBy`/`UploadedBy`/`RequestedBy` fields must now be a `Person.Id`, not the `UserAccount.Id` (`CallerId()`) the controller hands the service** â€” per the spec table, "creator/uploader/payee/requester" fields are `Person`, but every controller's `CallerId()` now yields a `UserAccount.Id` (Task 8). Each service method must resolve `Person.Id` from the caller's `UserAccount.Id` before writing:

```csharp
// MilestoneService.cs CreateAsync (representative pattern - apply identically to
// ExpenseService.CreateAsync, DocumentService.UploadAsync, DocumentRequestService.CreateAsync/
// FulfillAsync, StaffPaymentService.CreateAsync, LandService.AddPhotoAsync)
public async Task<Milestone> CreateAsync(Guid workspaceId, Guid callerUserAccountId, Guid jobId, MilestoneRequest request)
{
    await _scopedAccess.EnsureJobAccessAsync(callerUserAccountId, workspaceId, jobId, "create");

    var callerPersonId = await _context.UserAccounts
        .Where(a => a.Id == callerUserAccountId)
        .Select(a => a.PersonId)
        .FirstOrDefaultAsync();

    var milestone = new Milestone
    {
        Id = Guid.NewGuid(),
        JobId = jobId,
        Title = request.Title.Trim(),
        Description = request.Description?.Trim(),
        DueDate = request.DueDate,
        Status = "Pending",
        SortOrder = request.SortOrder,
        CreatedBy = callerPersonId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
    await _context.Milestones.AddAsync(milestone);
    await _context.SaveChangesAsync();
    return milestone;
}
```
Introduce a tiny shared helper to avoid repeating the `UserAccounts.Where(...).Select(a => a.PersonId)` lookup six times â€” add to `ScopedAccessService` (it already owns "resolve identity from an id" concerns):
```csharp
// ScopedAccessService.cs - add to interface and implementation
/// <summary>Resolves a caller's UserAccount.Id (the JWT subject) to the Person.Id behind it - needed
/// wherever an actor field (CreatedBy, RecordedBy, UploadedBy, ...) means Person, not UserAccount.</summary>
Task<Guid> ResolvePersonIdAsync(Guid userAccountId);
...
public async Task<Guid> ResolvePersonIdAsync(Guid userAccountId) =>
    await _context.UserAccounts.Where(a => a.Id == userAccountId).Select(a => a.PersonId).FirstAsync();
```
Every one of the six services' write methods (`MilestoneService.CreateAsync`/`UpdateStatusAsync` for `CompletedBy`, `ExpenseService.CreateAsync`, `DocumentService.UploadAsync`, `DocumentRequestService.CreateAsync`/`FulfillAsync`/`UpdateTargetAsync` for `TargetUserId`, `StaffPaymentService.CreateAsync` for both `UserId` payee param and `RecordedBy`, `LandService`'s photo-upload method for `UploadedBy`) replaces its direct `callerUserAccountId` assignment to the actor field with `await _scopedAccess.ResolvePersonIdAsync(callerUserAccountId)`. Any read-side `.Include(x => x.XxxByUser)` navigation stays unchanged syntactically.

`DocumentRequestService`'s `TargetUserId` (an explicit picker of a specific person to notify, not the caller) â€” the `Guid? targetUserId` parameter on `CreateAsync`/`UpdateTargetAsync` is supplied by the controller from `request.TargetUserId`, which comes from the UI's person picker (backed by `UserSearchResponse.UserId`, Task 8/10) â€” that value is already a `Person.Id` post-migration (the search endpoint returns people), so **no resolve-through-UserAccount step is needed for `TargetUserId`** â€” only for the caller-derived actor fields.

`StaffPaymentService`'s payee `UserId` (`StaffPayment.User`/`UserId`, the person being paid) â€” same as `TargetUserId`: this is picked from a person search/picker in the UI, already a `Person.Id`, no resolve step needed. Only `RecordedBy` (the logged-in staff member recording the payment) needs the `ResolvePersonIdAsync` call.

- [ ] **Step 4: Run test to verify it passes**
```
cd api && dotnet build
dotnet test --filter "MilestoneServiceTests|ExpenseServiceTests|DocumentServiceTests|DocumentRequestServiceTests|StaffPaymentServiceTests|LandServiceTests"
```

- [ ] **Step 5: Commit**
```
git add api/src/SurveyorLedger.API/Services/MilestoneService.cs api/src/SurveyorLedger.API/Services/ExpenseService.cs api/src/SurveyorLedger.API/Services/DocumentService.cs api/src/SurveyorLedger.API/Services/DocumentRequestService.cs api/src/SurveyorLedger.API/Services/StaffPaymentService.cs api/src/SurveyorLedger.API/Services/LandService.cs
git commit -m "feat: resolve caller Person.Id for actor fields across job-scoped entities"
```

---

### Task 7: `Invoice`/`Quotation.ClientId` -> `Person`, `Client` entity/service deletion

Per user decision during plan review: `ClientService.SearchAsync`/`CreateAsync` drop
workspace scoping entirely rather than deriving it from `Invoice.WorkspaceId` -
`SearchAsync` becomes a global `Person` search (same pattern as `AuthService.SearchUsersAsync`),
`CreateAsync` creates a bare `Person` with no workspace association. Real isolation is
Spec 2's job (job-scoped billing).

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/InvoiceService.cs:26,48,101,236-241`
- Modify: `api/src/SurveyorLedger.API/Services/QuotationService.cs:36,89,184-189`
- Modify: `api/src/SurveyorLedger.API/Services/ClientService.cs` (repointed to `Person`, workspace scoping dropped - kept, not deleted, since `InvoiceController`/`QuotationController` still need a client picker)
- Modify: `api/src/SurveyorLedger.API/Controllers/ClientsController.cs:27,34` (`_clientService.SearchAsync(workspaceId, CallerId(), query)` -> `_clientService.SearchAsync(CallerId(), query)`; `_clientService.CreateAsync(workspaceId, CallerId(), request)` -> `_clientService.CreateAsync(CallerId(), request)` - route stays nested under `/workspace/{id}` unchanged, just the two service calls drop the now-unused arg)
- Modify: `api/src/SurveyorLedger.API/Models/Billing/ClientDtos.cs` (`ClientResponse` gains `FirstName`/`LastName` split, matching `Person`)
- Test: `api/tests/SurveyorLedger.API.Tests/Services/InvoiceServiceTests.cs`, `QuotationServiceTests.cs`

**Interfaces:**
- Consumes: `Person` (Task 1).
- Produces: `IClientService.CreateAsync(Guid callerUserId, ClientRequest request): Task<Person>` (drops the `workspaceId` param entirely - global creation), `IClientService.SearchAsync(Guid callerUserId, string? query): Task<List<Person>>` (drops `workspaceId`), `EnsureClientExistsAsync(Guid clientId)` on `InvoiceService`/`QuotationService` drops its `workspaceId` param.

- [ ] **Step 1: Write the failing test**

```csharp
// api/tests/SurveyorLedger.API.Tests/Services/InvoiceServiceTests.cs
[Fact]
public async Task CreateAsync_ValidatesClientIdAgainstPerson_NotClientEntity()
{
    var person = new SurveyorLedger.Data.Entities.Person
    {
        Id = Guid.NewGuid(), FirstName = "Client", LastName = "Person", Email = "client-person@test.local",
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };
    Context.People.Add(person);
    await Context.SaveChangesAsync();

    var svc = GetService<IInvoiceService>();
    var invoice = await svc.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
    {
        ClientId = person.Id,
        LineItems = new() { new LineItemDto { Description = "Survey fee", Quantity = 1, UnitPrice = 5000 } }
    });

    Assert.Equal(person.Id, invoice.ClientId);
}

[Fact]
public async Task ClientService_SearchAsync_IsGlobal_NotWorkspaceFiltered()
{
    var clientService = GetService<IClientService>();
    var created = await clientService.CreateAsync(AdminId, new ClientRequest { Name = "Global Client", Email = "global@test.local" });

    var results = await clientService.SearchAsync(AdminId, "Global");

    Assert.Contains(results, p => p.Id == created.Id);
}
```

- [ ] **Step 2: Run test to verify it fails**
```
cd api && dotnet test tests/SurveyorLedger.API.Tests --filter "InvoiceServiceTests"
```
Expected: FAIL to compile - `Context.People` doesn't exist yet (blocked on Task 1), `IClientService.CreateAsync`/`SearchAsync` still take `workspaceId`.

- [ ] **Step 3: Write minimal implementation**

`ClientService.cs` - drop `workspaceId` from `CreateAsync`/`SearchAsync`, swap `Client` for `Person`:
```csharp
public interface IClientService
{
    Task<Person> CreateAsync(Guid callerUserId, ClientRequest request);
    Task<List<Person>> SearchAsync(Guid callerUserId, string? query);
    Task<Person> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid clientId);
    Task<Person> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid clientId, ClientRequest request);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid clientId);
    Task<decimal> GetBalanceAsync(Guid workspaceId, Guid callerUserId, Guid clientId);
    Task<List<Payment>> GetPaymentHistoryAsync(Guid workspaceId, Guid callerUserId, Guid clientId);
}

public async Task<Person> CreateAsync(Guid callerUserId, ClientRequest request)
{
    var person = new Person
    {
        Id = Guid.NewGuid(),
        FirstName = request.Name.Trim(), // ClientRequest.Name has no first/last split - stays on FirstName, LastName empty
        LastName = "",
        Phone = request.Phone?.Trim(),
        Email = request.Email?.Trim(),
        Address = ToAddress(request.Address),
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
    await _context.People.AddAsync(person);
    await _context.SaveChangesAsync();
    _logger.LogInformation("Client-person {PersonId} created by {UserId}", person.Id, callerUserId);
    return person;
}

public async Task<List<Person>> SearchAsync(Guid callerUserId, string? query)
{
    var people = _context.People.AsQueryable();
    if (!string.IsNullOrWhiteSpace(query))
    {
        var term = query.Trim();
        people = people.Where(p =>
            EF.Functions.Like(p.FirstName, $"%{term}%") ||
            EF.Functions.Like(p.LastName, $"%{term}%") ||
            (p.Phone != null && EF.Functions.Like(p.Phone, $"%{term}%")) ||
            (p.Email != null && EF.Functions.Like(p.Email, $"%{term}%")));
    }
    return await people.OrderByDescending(p => p.CreatedAt).ToListAsync();
}
```
`GetByIdAsync`/`UpdateAsync`/`DeleteAsync`/`GetBalanceAsync`/`GetPaymentHistoryAsync` keep taking `workspaceId` (still gate the caller's *permission* to act via `EnsureAllowedAsync` against that workspace) but `FindClientAsync` drops its `WorkspaceId` filter on the entity itself: `_context.People.FirstOrDefaultAsync(p => p.Id == clientId) ?? throw new NotFoundException("Client not found")`.

`InvoiceService.cs`/`QuotationService.cs` - `EnsureClientExistsAsync` drops `workspaceId`:
```csharp
private async Task EnsureClientExistsAsync(Guid clientId)
{
    var exists = await _context.People.AnyAsync(p => p.Id == clientId && p.IsActive);
    if (!exists)
        throw new NotFoundException("Client not found");
}
```
Every call site (`CreateAsync`/`UpdateAsync` in both services) drops the `workspaceId` argument to this call. No other line in either service references `Client`/`_context.Clients` - `ClientId` fields on `Invoice`/`Quotation` entities and `InvoiceRequest`/`QuotationRequest`/`InvoiceResponse`/`QuotationResponse` DTOs are unchanged (`Guid`, same name), only what they point at changes.

`ClientDtos.cs` - `ClientResponse` gains the `Person` shape:
```csharp
public class ClientResponse
{
    public Guid ClientId { get; set; }
    public string Name { get; set; } // FirstName + " " + LastName, trimmed
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public AddressDto Address { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```
`ClientRequest` unchanged (still takes a single `Name`, split into `FirstName`/empty `LastName` in `CreateAsync` above).

`InvoiceServiceTests.cs`/`QuotationServiceTests.cs` - their existing `SeedInvoiceAsync`/`SeedQuotationAsync`-style helpers call `_clientService.CreateAsync(workspaceId, ...)`; drop the `workspaceId` argument to match the new signature.

- [ ] **Step 4: Run test to verify it passes**
```
cd api && dotnet build src/SurveyorLedger.API
dotnet test tests/SurveyorLedger.API.Tests --filter "InvoiceServiceTests|QuotationServiceTests"
```
Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add api/src/SurveyorLedger.API/Services/InvoiceService.cs api/src/SurveyorLedger.API/Services/QuotationService.cs api/src/SurveyorLedger.API/Services/ClientService.cs api/src/SurveyorLedger.API/Models/Billing/ClientDtos.cs api/tests/SurveyorLedger.API.Tests/Services/InvoiceServiceTests.cs api/tests/SurveyorLedger.API.Tests/Services/QuotationServiceTests.cs
git commit -m "feat: repoint Invoice/Quotation ClientId to Person, drop Client workspace scoping"
```

---

### Task 8: Controllers + DTOs â€” `CallerId()` semantics, response shapes joining through `Person`

**Files:**
- Modify: `api/src/SurveyorLedger.API/Controllers/UserController.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/AuthController.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/WorkspaceController.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/InvitationController.cs`
- Modify: `api/src/SurveyorLedger.API/Models/User/UserProfileResponse.cs`
- Modify: `api/src/SurveyorLedger.API/Models/User/UserSearchResponse.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Auth/AuthResponse.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Workspace/MemberResponse.cs`
- No changes needed: `ClientsController.cs` (deleted, Task 7), `DocumentController.cs`, `DocumentRequestController.cs`, `ExpenseController.cs`, `InvoicesController.cs`, `JobController.cs`, `JobsController.cs`, `LandController.cs`, `MilestoneController.cs`, `QuotationsController.cs`, `StaffPaymentController.cs` â€” every `CallerId()` in these already just forwards the `Guid` straight into the corresponding service call, and the service signatures are unchanged (Tasks 5-7). The `Guid` these `CallerId()` helpers extract from `ClaimTypes.NameIdentifier` now *means* `UserAccount.Id` â€” that meaning shift is fully absorbed inside the services already updated; no controller-side code differs.

**Interfaces:**
- Consumes: `IAuthService.LoginAsync`/`RefreshTokenAsync` returning `(Person, UserAccount, string, string, int)` (Task 2); `IAuthService.GetAccountByIdAsync(Guid) â†’ (Person, UserAccount)?`; `IAuthService.SearchPeopleAsync(string) â†’ List<Person>`; `IAuthService.UpdateProfileAsync(Guid, ...) â†’ Person`.
- Produces: `UserProfileResponse`, `UserSearchResponse`, `AuthResponse`, `MemberResponse` DTOs â€” same field names as today, sourced from `Person` (+ `UserAccount.EmailVerified` for `UserProfileResponse.EmailVerified`).

- [ ] **Step 1: Write the failing test**

```csharp
// api/tests/SurveyorLedger.API.Tests/Controllers/UserControllerTests.cs (or extend nearest equivalent integration test)
[Fact]
public async Task GetProfile_ReturnsPersonFields_AndUserAccountEmailVerified()
{
    // (representative shape - implementer wires this against the real test harness for
    // controller-level tests, likely WebApplicationFactory-based; if no such harness exists
    // yet in this repo, exercise UserController.ToResponse via a lightweight unit test
    // constructing (Person, UserAccount) tuples directly instead.)
}
```
Given the repo's existing test suite is service-level integration tests (`WorkspaceIntegrationTestBase`), prefer a direct unit test on the mapping logic instead:
```csharp
// api/tests/SurveyorLedger.API.Tests/Controllers/UserControllerMappingTests.cs
using SurveyorLedger.API.Controllers;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Controllers;

public class UserControllerMappingTests
{
    [Fact]
    public void ToResponse_MapsPersonAndAccountFieldsCorrectly()
    {
        var person = new Person
        {
            Id = Guid.NewGuid(), FirstName = "Ann", LastName = "Silva", Email = "ann@test.local",
            Phone = "0771234567", Address = new Address { City = "Colombo" },
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, IsActive = true
        };
        var account = new UserAccount
        {
            Id = Guid.NewGuid(), PersonId = person.Id, EmailVerified = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, IsActive = true
        };

        var response = UserController.ToResponse(person, account);

        Assert.Equal(account.Id, response.UserId);
        Assert.Equal("Ann", response.FirstName);
        Assert.True(response.EmailVerified);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```
cd api && dotnet test --filter UserControllerMappingTests
```

- [ ] **Step 3: Write minimal implementation**

`UserController.cs`:
```csharp
[HttpGet("profile")]
public async Task<ActionResult<ApiResponse<UserProfileResponse>>> GetProfile()
{
    var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!Guid.TryParse(userId, out var id))
        return Unauthorized(ApiResponse<object>.Fail("Invalid user ID"));

    var result = await _authService.GetAccountByIdAsync(id);
    if (result == null)
        return NotFound(ApiResponse<object>.Fail("User not found"));

    return Ok(ApiResponse<UserProfileResponse>.Ok(ToResponse(result.Value.person, result.Value.account)));
}

[HttpGet("search")]
public async Task<ActionResult<ApiResponse<List<UserSearchResponse>>>> Search([FromQuery] string q)
{
    var people = await _authService.SearchPeopleAsync(q ?? "");
    var results = people.Select(p => new UserSearchResponse
    {
        UserId = p.Id, FirstName = p.FirstName, LastName = p.LastName, Email = p.Email
    }).ToList();
    return Ok(ApiResponse<List<UserSearchResponse>>.Ok(results));
}

[HttpPut("profile")]
public async Task<ActionResult<ApiResponse<UserProfileResponse>>> UpdateProfile([FromBody] UpdateProfileRequest request)
{
    var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!Guid.TryParse(userId, out var id))
        return Unauthorized(ApiResponse<object>.Fail("Invalid user ID"));

    var person = await _authService.UpdateProfileAsync(id, request);
    var result = await _authService.GetAccountByIdAsync(id);
    return Ok(ApiResponse<UserProfileResponse>.Ok(ToResponse(person, result!.Value.account)));
}

internal static UserProfileResponse ToResponse(Person person, UserAccount account) => new()
{
    UserId = account.Id,
    Email = person.Email,
    FirstName = person.FirstName,
    LastName = person.LastName,
    Phone = person.Phone,
    EmailVerified = account.EmailVerified,
    Address = new AddressDto
    {
        Street = person.Address.Street, City = person.Address.City, District = person.Address.District,
        PostalCode = person.Address.PostalCode, Country = person.Address.Country
    },
    CreatedAt = person.CreatedAt
};
```
Note: `UserProfileResponse.UserId` stays the `UserAccount.Id` (the JWT subject, matches every other id the frontend already treats as "the current user id" via `getUserId()`), not `Person.Id` â€” this preserves frontend behavior without a UI change (per spec's "no UI change beyond what's needed to keep the app compiling").

`AuthController.cs` - two call sites (`Login`, `RefreshToken`) build `AuthResponse` from the tuple `IAuthService.LoginAsync`/`RefreshTokenAsync` return. Both destructure `(user, accessToken, refreshToken, expiresIn)` today; change to `(person, account, accessToken, refreshToken, expiresIn)` per Task 2's new signature:
```csharp
// Login (was: var (user, accessToken, refreshToken, expiresIn) = await _authService.LoginAsync(request);)
var (person, account, accessToken, refreshToken, expiresIn) = await _authService.LoginAsync(request);
SetRefreshTokenCookie(refreshToken);

return Ok(ApiResponse<AuthResponse>.Ok(new AuthResponse
{
    UserId = account.Id,
    // LoginAsync matches on request.Email (non-null), so a user reaching this
    // point always has an Email - the ! here reflects that, not a real risk.
    Email = person.Email!,
    FirstName = person.FirstName,
    LastName = person.LastName,
    AccessToken = accessToken,
    RefreshToken = refreshToken,
    ExpiresIn = expiresIn
}));

// RefreshToken (was: var (user, accessToken, newRefreshToken, expiresIn) = await _authService.RefreshTokenAsync(refreshToken);)
var (person, account, accessToken, newRefreshToken, expiresIn) = await _authService.RefreshTokenAsync(refreshToken);
SetRefreshTokenCookie(newRefreshToken);

return Ok(ApiResponse<AuthResponse>.Ok(new AuthResponse
{
    UserId = account.Id,
    Email = person.Email!,
    FirstName = person.FirstName,
    LastName = person.LastName,
    AccessToken = accessToken,
    RefreshToken = newRefreshToken,
    ExpiresIn = expiresIn
}));
```
`Register`/`VerifyOtp`/`ResendOtp`/`ForgotPassword`/`ResetPassword`/`Logout` call sites are unchanged - none of them destructure a `User`/`Person`/`UserAccount` from the service return.

`WorkspaceController.cs` - its `MemberResponse` mapping (backed by `WorkspaceService.MemberInfo`, Task 5) needs no shape change since `MemberInfo`'s fields (`Email`, `FirstName`, `LastName`) are already resolved through `Person` inside `WorkspaceService`.

`InvitationController.cs` â€” no DTO shape change; `Invitation.UserId`/`InvitedBy` are still plain `Guid`s in `InvitationResponse`.

`UserProfileResponse.cs`, `UserSearchResponse.cs`, `AuthResponse.cs`, `MemberResponse.cs` â€” field lists unchanged (same names, sourced from `Person`/`UserAccount` instead of `User` â€” no DTO file edits actually needed unless a doc comment references "User" by name, in which case update the comment for accuracy, e.g. `UserProfileResponse.Email`'s comment "Null for a client who hasn't been invited/verified yet" stays true verbatim).

- [ ] **Step 4: Run test to verify it passes**
```
cd api && dotnet build
dotnet test --filter UserControllerMappingTests
```

- [ ] **Step 5: Commit**
```
git add api/src/SurveyorLedger.API/Controllers/UserController.cs api/src/SurveyorLedger.API/Controllers/AuthController.cs api/tests/SurveyorLedger.API.Tests/Controllers/UserControllerMappingTests.cs
git commit -m "feat: update UserController/AuthController mapping for Person/UserAccount split"
```

---

### Task 9: `Program.cs` DI/startup cleanup + full-solution build gate

**Files:**
- Modify: `api/src/SurveyorLedger.API/Program.cs`

**Interfaces:**
- Consumes: nothing new â€” this task only removes the now-dead `HasCompletedSignup` backfill SQL (line 176, targets `UPDATE Users SET ...`, which is meaningless post-migration since the migration is a clean drop, not a backfill) and confirms no DI registration still references `ClientService`/`IClientService`.

- [ ] **Step 1: Write the failing test**
No new unit test â€” this is a startup-config cleanup task; the "test" is the full build succeeding and the app starting.
```
cd api && dotnet build 2>&1 | grep -i "error"
```
Expected before fix: build succeeds functionally but a startup `UPDATE Users` SQL statement (Program.cs:176) will throw `Invalid object name 'Users'` at runtime on next app start, since `Users` table no longer exists post-migration.

- [ ] **Step 2: Run test to verify it fails**
```
cd api && dotnet run --project src/SurveyorLedger.API
```
Expected: startup throws a SQL exception referencing `Users` if that backfill block still runs unconditionally at boot.

- [ ] **Step 3: Write minimal implementation**

Remove the dead backfill block entirely (it was a one-time data-migration for `HasCompletedSignup` under the old `User` model â€” irrelevant post clean-slate migration):
```csharp
// DELETE this block from Program.cs (was around line 176):
// await context.Database.ExecuteSqlRawAsync(
//     "UPDATE Users SET HasCompletedSignup = 1 WHERE PasswordHash IS NOT NULL AND HasCompletedSignup = 0");
```
Remove `services.AddScoped<IClientService, ClientService>()` (or equivalent DI line) if present â€” grep confirms before editing:
```
grep -n "IClientService\|ClientService" api/src/SurveyorLedger.API/Program.cs
```
No Casbin policy-loading change needed (`CasbinService.LoadRulesFromDatabaseAsync` is entirely `UserAccess`/`RolePermission` based, unaffected by the split â€” confirmed in Task-prep reading above). No JWT claim-setup change needed in `Program.cs` â€” `TokenService.GenerateTokens(userId, email)` (called from `AuthService` with `account.Id`/`person.Email` post-Task-2) already produces the `NameIdentifier` claim from whatever `Guid` it's handed; Task 2's call sites already pass `account.Id`.

- [ ] **Step 4: Run test to verify it passes**
```
cd api && dotnet build
dotnet run --project src/SurveyorLedger.API &
sleep 5
curl -s http://localhost:5296/health || curl -s http://localhost:5296/api/health
```
Expected: clean startup, no SQL errors, Casbin initializes ("Casbin initialized with N policies and M groups" in logs).

- [ ] **Step 5: Commit**
```
git add api/src/SurveyorLedger.API/Program.cs
git commit -m "chore: remove stale User-table backfill from startup, confirm DI clean of Client"
```

---

### Task 10: Test fixture updates (`WorkspaceIntegrationTestBase` + dependent test files)

**Files:**
- Modify: `api/tests/SurveyorLedger.API.Tests/Services/WorkspaceIntegrationTestBase.cs`
- Modify: every test file under `api/tests/SurveyorLedger.API.Tests/Services/` that directly constructs `new User { ... }` or references `Context.Users` (must be discovered via grep before editing â€” the read files above show at least `WorkspaceIntegrationTestBase.cs` itself; others found in Tasks 2-7 above already assume the fixture change lands here)

**Interfaces:**
- Produces: `WorkspaceIntegrationTestBase.AdminId`, `.SurveyorId`, `.ClientId` â€” **unchanged as public surface** (still `Guid` properties), but now seeded as `UserAccount.Id` values (since every downstream test passes these into `CallerId()`-shaped service params, which now mean `UserAccount.Id`). Adds `protected Guid AdminPersonId { get; private set; }` etc. only if a test genuinely needs the `Person.Id` distinct from the account id â€” added on demand, not speculatively (YAGNI).

- [ ] **Step 1: Write the failing test**

The fixture itself has no direct test â€” its correctness is proven by every test class that inherits it. Use the existing `AuthServiceTests`/`InvitationServiceTests`/`UserAccessGrantServiceTests` written in Tasks 2-4 as the acceptance check:
```
cd api && dotnet test --filter "AuthServiceTests|InvitationServiceTests|UserAccessGrantServiceTests"
```
Expected before fix: compile error (`Context.Users` doesn't exist) or runtime FK violation (`UserAccess.UserId` pointing at a `Person.Id` instead of `UserAccount.Id`).

- [ ] **Step 2: Run test to verify it fails**
```
cd api && dotnet test --filter WorkspaceIntegrationTestBase
```
(Run via any dependent test class, since the base itself isn't directly testable â€” e.g. `dotnet test --filter AuthServiceTests`.)

- [ ] **Step 3: Write minimal implementation**

```csharp
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

    foreach (var (accountId, first) in new[] { (AdminId, "Admin"), (SurveyorId, "Surveyor"), (ClientId, "Client") })
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
            Id = accountId,
            PersonId = personId,
            EmailVerified = true,
            HasCompletedSignup = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }
    await Context.SaveChangesAsync();

    await GrantService.GrantAsync(AdminId, RoleConfiguration.AdminRoleId, Constants.ScopeTypes.Workspace, WorkspaceId, AdminId);
    await GrantService.GrantAsync(SurveyorId, RoleConfiguration.SurveyorRoleId, Constants.ScopeTypes.Workspace, WorkspaceId, AdminId);
    await GrantService.GrantAsync(ClientId, RoleConfiguration.MemberRoleId, Constants.ScopeTypes.Workspace, WorkspaceId, AdminId);
}
```
`AdminId`/`SurveyorId`/`ClientId` keep their existing meaning to every test that already uses them as `CallerId()`-equivalent params (they're now literally `UserAccount.Id`, which is exactly what those params mean post-split) â€” zero downstream test call-site changes required for the ~15 test files that only ever pass these ids into service methods.

Then run a repo-wide grep to find and fix any remaining `new User { ... }` or `Context.Users` references in other test files:
```
grep -rln "new User {\|Context.Users\|_context.Users" api/tests/SurveyorLedger.API.Tests/
```
For each hit found, apply the same `Person` + `UserAccount` pair-construction pattern shown above, matching that test's existing variable names.

- [ ] **Step 4: Run test to verify it passes**
```
cd api && dotnet test
```
(Full suite here specifically because this task is the fixture every other test depends on â€” a fixture bug fails silently in isolated `--filter` runs otherwise.)

- [ ] **Step 5: Commit**
```
git add api/tests/SurveyorLedger.API.Tests/Services/WorkspaceIntegrationTestBase.cs
git commit -m "test: update WorkspaceIntegrationTestBase to seed Person+UserAccount pairs"
```

---

### Task 11: UI updates â€” `auth.service.ts` and `User`-shaped interfaces

**Files:**
- Modify: `ui/src/app/core/auth.service.ts`
- Modify: `ui/src/app/core/person.service.ts` (if it wraps `UserSearchResponse`/`UserProfileResponse` â€” confirm field names match Task 8's DTOs, which are unchanged in shape)
- Modify: `ui/src/app/core/workspace.service.ts` (member list interface, if it locally redeclares fields beyond what `MemberResponse` already provides)
- Modify: `ui/src/app/core/invitation.service.ts` (only if it locally types `Invitation.userId` â€” no shape change per Task 8)

**Interfaces:**
- Consumes: `ApiResponse<UserProfileResponse>`, `ApiResponse<AuthResponse>`, `ApiResponse<List<UserSearchResponse>>`, `ApiResponse<List<MemberResponse>>` â€” **all unchanged wire shapes** per Tasks 7-8 (field names preserved deliberately to avoid a UI ripple, matching spec's "no UI change beyond what's needed to keep the app compiling").
- Produces: no new TS interfaces â€” this task is a no-op confirmation pass unless a TS interface literally redeclares `User` fields inline with a comment referencing the backend `User` entity by name.

- [ ] **Step 1: Write the failing test**
No behavioral test â€” this is a typecheck-level task, since the wire contract is unchanged by design.
```
cd ui && npx tsc --noEmit -p tsconfig.app.json
```
Expected before Task 8 lands: no failure (TS interfaces are structurally typed against unchanged JSON shape) â€” this task exists only to catch any TS-side type that assumed a field which existed on the old `User` DTO but was dropped (there are none per Tasks 7-8's "field names preserved" design), and to update stale comments.

- [ ] **Step 2: Run test to verify it fails**
```
cd ui && npx tsc --noEmit -p tsconfig.app.json
```
Run before any edit â€” establishes the zero-diff baseline (expected: passes clean already, confirming the backend DTO shape truly didn't change).

- [ ] **Step 3: Write minimal implementation**

Read `ui/src/app/core/auth.service.ts` lines 1-20 (already partially read above):
```typescript
export interface User {
  email: string;
  firstName: string;
  lastName: string;
  // ... (other existing fields, unchanged)
}
```
No field rename needed â€” `email`/`firstName`/`lastName` map 1:1 to `AuthResponse.Email`/`.FirstName`/`.LastName` (Task 8, unchanged). If the interface or any inline comment references "the User entity" by name expecting a 1:1 backend match (e.g. a JSDoc comment), update the comment only:
```typescript
/** The authenticated caller's identity + credential summary, as returned by /auth endpoints.
 *  Backed by Person (identity) + UserAccount (credential) on the API side post-split -
 *  the wire shape here is unchanged. */
export interface User {
  email: string;
  firstName: string;
  lastName: string;
}
```
Same doc-only pass for `person.service.ts`, `workspace.service.ts`, `invitation.service.ts` â€” grep first:
```
grep -rn "interface User\|// User entity\|/\* User \*/" ui/src/app/core/*.ts
```

- [ ] **Step 4: Run test to verify it passes**
```
cd ui && npx tsc --noEmit -p tsconfig.app.json
ng build --configuration development
```

- [ ] **Step 5: Commit**
```
git add ui/src/app/core/auth.service.ts
git commit -m "docs: note Person/UserAccount split in UI auth interface comments"
```

---

### Task 12: End-to-end verification

**Files:** none (verification only)

- [ ] **Step 1: Full backend test suite**
```
cd api && dotnet test
```
Expected: all tests green, including Tasks 2-10's new tests and every pre-existing test that now runs against the seeded `Person`+`UserAccount` fixture (Task 10).

- [ ] **Step 2: Full backend build + migration apply against LocalDB**
```
cd api && dotnet build
dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```
Expected: `Users`/`Clients` tables dropped, `People`/`UserAccounts` tables created, every FK column repointed per the sweep table, no manual SQL needed (dev DB, no data to preserve).

- [ ] **Step 3: Manual smoke test â€” register â†’ verify OTP â†’ login â†’ invite â†’ accept**
```
cd api && dotnet run --project src/SurveyorLedger.API &
cd ui && ng serve &
```
In browser: register a new account (confirms `Person`+`UserAccount` created together, Task 2), verify OTP, log in (confirms JWT `NameIdentifier` = `UserAccount.Id`, Task 2), create a workspace (confirms `Workspace.Owner` = `UserAccount.Id`, Task 1/5), invite a new email to the workspace (confirms `Person`-only eager create, Task 3), open the invite link and complete signup (confirms `UserAccount` created at complete-time, Task 3), log in as the invitee and accept (confirms `GrantAndMarkAcceptedAsync`'s `Personâ†’UserAccount` resolve, Task 3), create a job and assign the invitee (confirms `Job.CreatedBy`/participant flow through `Person`, Task 5-6), create an invoice against a person (confirms `Invoice.ClientId â†’ Person`, Task 7).

- [ ] **Step 4: UI typecheck + build**
```
cd ui && npx tsc --noEmit -p tsconfig.app.json
ng build --configuration production
```

- [ ] **Step 5: Report results to user, do not commit further (per rules.md â€” implement and verify, then wait for explicit commit instruction on any remaining uncommitted work)**

---

## Self-Review Notes

- **Requirement coverage check against the spec:** every row of the FK repointing table (`UserAccess.UserId`, `AuthToken.UserId`, `AuditLog.UserId`, `Workspace.Owner`, `Invitation.UserId`/`InvitedByUser`, `Job.CreatedBy`, `Milestone.CreatedByUser`/`CompletedByUser`, `Expense.RecordedByUser`, `Document.UploadedByUser`, `LandPhoto.UploadedByUser`, `DocumentRequest.RequestedByUser`/`FulfilledByUser`/`TargetUser`, `StaffPayment.UserId`/`RecordedByUser`, `Invoice.ClientId`, `Quotation.ClientId`) is addressed across Tasks 1 (entity/config), 5-7 (service-layer joins and actor-field resolution).
- **Fixed a correctness gap during drafting, not left as a placeholder:** `InvitationService.GrantAndMarkAcceptedAsync` originally (in the current codebase) passes `invitation.UserId` straight into `_grantService.GrantAsync`. Under the split `invitation.UserId` is a `Person.Id`, but `UserAccess.UserId` must be a `UserAccount.Id` â€” Task 3 adds an explicit resolve-and-throw-if-missing step, which is also the concrete mechanism that keeps the spec's "no path grants access before a password exists" invariant true (it now throws instead of silently corrupting `UserAccess.UserId` with a `Person.Id`). Same fix applied to `AcceptInvitationAsync`, `DeclineInvitationAsync`, `GetMyInvitationsAsync`'s caller-id comparisons, which compare a JWT-derived `UserAccount.Id` against `Invitation.UserId` (a `Person.Id`) â€” all three needed a `PersonId` resolve step added.
- **CasbinService confirmed to need zero code changes** â€” it operates only on `.ToString()`'d `Guid`s and DB rows already keyed by `UserAccess.UserId`/`RoleId`, never touches `User`/`Person`/`UserAccount` types directly. Verified by full read.
- **ScopedAccessService confirmed to need zero logic changes** (only doc-comment accuracy) â€” entirely `Guid`/SQL-based, never dereferences `.User` navigation. Verified by full read.
- **Consistency check:** `IUserAccessGrantService.GrantAsync(Guid userId, ...)` signature is identical in Task 4 and every caller in Tasks 3 and 5 â€” no drift.
- **Task 7 and the `AuthController.cs` half of Task 8 were initially drafted without reading the real files** (flagged honestly rather than hidden) - both gaps were closed in a follow-up pass: read `InvoiceService.cs`, `QuotationService.cs`, `ClientService.cs`, `ClientDtos.cs`, `ClientsController.cs`, and `AuthController.cs` in full and rewrote both tasks against the actual signatures (`InvoiceRequest`/`LineItemDto` field names, `EnsureClientExistsAsync`'s exact call sites, `AuthController.Login`/`RefreshToken`'s exact tuple destructuring). No placeholder signatures remain in the plan.
- **User decision folded in during review:** `ClientService.SearchAsync`/`CreateAsync` drop workspace scoping entirely (global `Person` search/create) rather than deriving scope from `Invoice.WorkspaceId` - real isolation is explicitly Spec 2's job, this plan just keeps the build green.
- **Task right-sizing:** each task is independently buildable/testable per the plan's own build-gate steps; Task 1 is the only one whose `dotnet build` check is scoped to the Data project rather than the full solution, because every consumer of `User`/`Client` types across the other ~40 files can't compile until Tasks 2-9 land â€” this is called out explicitly in Task 1 rather than presented as a false "green build" claim.
- **Migration discipline:** exactly one `dotnet ef migrations add SplitUserIntoPersonAndUserAccount` step (Task 1), never hand-edited afterward, consistent with `.claude/rules.md` and the migration-check skill's requirement.

### Critical Files for Implementation
- api/src/SurveyorLedger.Data/Entities/Person.cs
- api/src/SurveyorLedger.Data/Entities/UserAccount.cs
- api/src/SurveyorLedger.API/Services/AuthService.cs
- api/src/SurveyorLedger.API/Services/InvitationService.cs
- api/tests/SurveyorLedger.API.Tests/Services/WorkspaceIntegrationTestBase.cs