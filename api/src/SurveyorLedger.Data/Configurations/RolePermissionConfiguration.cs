using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => new { x.RoleId, x.PermissionId }).IsUnique();

        var seededAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        RolePermission Grant(Guid id, Guid roleId, Guid permissionId) =>
            new() { Id = id, RoleId = roleId, PermissionId = permissionId, CreatedAt = seededAt };

        builder.HasData(
            // Admin: full workspace control
            Grant(new Guid("00000000-0000-0000-0000-000000000201"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewWorkspaceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000202"), RoleConfiguration.AdminRoleId, PermissionConfiguration.EditWorkspaceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000203"), RoleConfiguration.AdminRoleId, PermissionConfiguration.DeleteWorkspaceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000204"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ManageMembersId),
            // Manager, Surveyor, Client: view only
            Grant(new Guid("00000000-0000-0000-0000-000000000205"), RoleConfiguration.ManagerRoleId, PermissionConfiguration.ViewWorkspaceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000206"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewWorkspaceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000207"), RoleConfiguration.ClientRoleId, PermissionConfiguration.ViewWorkspaceId)
        );
    }
}
