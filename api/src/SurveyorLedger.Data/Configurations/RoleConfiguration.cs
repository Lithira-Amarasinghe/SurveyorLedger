using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Core;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    // Fixed GUIDs required for EF HasData to produce stable migrations.
    public static readonly Guid AdminRoleId = new("00000000-0000-0000-0000-000000000001");
    public static readonly Guid ManagerRoleId = new("00000000-0000-0000-0000-000000000002");
    public static readonly Guid SurveyorRoleId = new("00000000-0000-0000-0000-000000000003");
    public static readonly Guid ClientRoleId = new("00000000-0000-0000-0000-000000000004");

    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.Name);

        builder.HasMany(x => x.RolePermissions).WithOne(x => x.Role).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.UserAccesses).WithOne(x => x.Role).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);

        var seededAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        builder.HasData(
            new Role { Id = AdminRoleId, Name = Constants.SystemRoles.Admin, Description = "Full access to workspace settings, members, and data.", WorkspaceId = null, IsSystem = true, CreatedAt = seededAt, UpdatedAt = seededAt },
            new Role { Id = ManagerRoleId, Name = Constants.SystemRoles.Manager, Description = "Manages jobs and surveyors within a workspace.", WorkspaceId = null, IsSystem = true, CreatedAt = seededAt, UpdatedAt = seededAt },
            new Role { Id = SurveyorRoleId, Name = Constants.SystemRoles.Surveyor, Description = "Performs assigned survey jobs.", WorkspaceId = null, IsSystem = true, CreatedAt = seededAt, UpdatedAt = seededAt },
            new Role { Id = ClientRoleId, Name = Constants.SystemRoles.Client, Description = "Views job status and results for their organization.", WorkspaceId = null, IsSystem = true, CreatedAt = seededAt, UpdatedAt = seededAt }
        );
    }
}
