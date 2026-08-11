using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public static readonly Guid ViewWorkspaceId = new("00000000-0000-0000-0000-000000000101");
    public static readonly Guid EditWorkspaceId = new("00000000-0000-0000-0000-000000000102");
    public static readonly Guid DeleteWorkspaceId = new("00000000-0000-0000-0000-000000000103");
    public static readonly Guid ManageMembersId = new("00000000-0000-0000-0000-000000000104");
    public static readonly Guid ViewLandId = new("00000000-0000-0000-0000-000000000105");
    public static readonly Guid CreateLandId = new("00000000-0000-0000-0000-000000000106");
    public static readonly Guid EditLandId = new("00000000-0000-0000-0000-000000000107");
    public static readonly Guid DeleteLandId = new("00000000-0000-0000-0000-000000000108");
    public static readonly Guid ViewJobId = new("00000000-0000-0000-0000-000000000109");
    public static readonly Guid CreateJobId = new("00000000-0000-0000-0000-000000000110");
    public static readonly Guid EditJobId = new("00000000-0000-0000-0000-000000000111");
    public static readonly Guid DeleteJobId = new("00000000-0000-0000-0000-000000000112");
    public static readonly Guid ViewClientId = new("00000000-0000-0000-0000-000000000113");
    public static readonly Guid CreateClientId = new("00000000-0000-0000-0000-000000000114");
    public static readonly Guid ViewAllJobId = new("00000000-0000-0000-0000-000000000115");

    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Resource).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.Name).IsUnique();
        builder.HasIndex(x => new { x.Resource, x.Action, x.Scope }).IsUnique();

        builder.HasMany(x => x.RolePermissions).WithOne(x => x.Permission).HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);

        var seededAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        builder.HasData(
            new Permission { Id = ViewWorkspaceId, Name = "workspace.view", Description = "View workspace details.", Resource = "workspace", Action = "view", Scope = null, CreatedAt = seededAt },
            new Permission { Id = EditWorkspaceId, Name = "workspace.edit", Description = "Edit workspace settings.", Resource = "workspace", Action = "edit", Scope = null, CreatedAt = seededAt },
            new Permission { Id = DeleteWorkspaceId, Name = "workspace.delete", Description = "Delete a workspace.", Resource = "workspace", Action = "delete", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ManageMembersId, Name = "workspace.manage_members", Description = "Invite, remove, and change roles of workspace members.", Resource = "workspace", Action = "manage_members", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ViewLandId, Name = "land.view", Description = "View land records.", Resource = "land", Action = "view", Scope = null, CreatedAt = seededAt },
            new Permission { Id = CreateLandId, Name = "land.create", Description = "Create land records.", Resource = "land", Action = "create", Scope = null, CreatedAt = seededAt },
            new Permission { Id = EditLandId, Name = "land.edit", Description = "Edit land records, surveys, deeds, and boundaries.", Resource = "land", Action = "edit", Scope = null, CreatedAt = seededAt },
            new Permission { Id = DeleteLandId, Name = "land.delete", Description = "Delete land records.", Resource = "land", Action = "delete", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ViewJobId, Name = "job.view", Description = "View jobs.", Resource = "job", Action = "view", Scope = null, CreatedAt = seededAt },
            new Permission { Id = CreateJobId, Name = "job.create", Description = "Create jobs.", Resource = "job", Action = "create", Scope = null, CreatedAt = seededAt },
            new Permission { Id = EditJobId, Name = "job.edit", Description = "Edit jobs, participants, and land links.", Resource = "job", Action = "edit", Scope = null, CreatedAt = seededAt },
            new Permission { Id = DeleteJobId, Name = "job.delete", Description = "Delete jobs.", Resource = "job", Action = "delete", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ViewClientId, Name = "client.view", Description = "Search/view client contact records.", Resource = "client", Action = "view", Scope = null, CreatedAt = seededAt },
            new Permission { Id = CreateClientId, Name = "client.create", Description = "Create a bare client contact record.", Resource = "client", Action = "create", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ViewAllJobId, Name = "job.view_all", Description = "View every job in the workspace, not just assigned ones.", Resource = "job", Action = "view_all", Scope = null, CreatedAt = seededAt }
        );
    }
}
