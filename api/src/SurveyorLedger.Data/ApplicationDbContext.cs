using Microsoft.EntityFrameworkCore;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Person> People { get; set; }
    public DbSet<UserAccount> UserAccounts { get; set; }
    public DbSet<Workspace> Workspaces { get; set; }
    public DbSet<Subscription> Subscriptions { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<RoleScope> RoleScopes { get; set; }
    public DbSet<ScopeParentType> ScopeParentTypes { get; set; }
    public DbSet<AssignmentPolicy> AssignmentPolicies { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<RolePermission> RolePermissions { get; set; }
    public DbSet<UserAccess> UserAccesses { get; set; }
    public DbSet<Invitation> Invitations { get; set; }
    public DbSet<AuthToken> AuthTokens { get; set; }
    public DbSet<EmailVerification> EmailVerifications { get; set; }
    public DbSet<PendingRegistration> PendingRegistrations { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<Land> Lands { get; set; }
    public DbSet<LandSurvey> LandSurveys { get; set; }
    public DbSet<LandDeed> LandDeeds { get; set; }
    public DbSet<LandBoundary> LandBoundaries { get; set; }
    public DbSet<LandMapPoint> LandMapPoints { get; set; }
    public DbSet<Job> Jobs { get; set; }
    public DbSet<JobLand> JobLands { get; set; }
    public DbSet<Milestone> Milestones { get; set; }
    public DbSet<Document> Documents { get; set; }
    public DbSet<DocumentRequest> DocumentRequests { get; set; }
    public DbSet<LandDocumentRequest> LandDocumentRequests { get; set; }
    public DbSet<Quotation> Quotations { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<Expense> Expenses { get; set; }
    public DbSet<JobBudget> JobBudgets { get; set; }

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
        modelBuilder.Entity<Payment>().HasQueryFilter(x => !x.IsVoided);
    }
}
