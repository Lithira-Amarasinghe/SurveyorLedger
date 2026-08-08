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
            new Permission { Id = ManageMembersId, Name = "workspace.manage_members", Description = "Invite, remove, and change roles of workspace members.", Resource = "workspace", Action = "manage_members", Scope = null, CreatedAt = seededAt }
        );
    }
}
