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
            Grant(new Guid("00000000-0000-0000-0000-000000000207"), RoleConfiguration.ClientRoleId, PermissionConfiguration.ViewWorkspaceId),
            // Land - Admin: full access
            Grant(new Guid("00000000-0000-0000-0000-000000000208"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000209"), RoleConfiguration.AdminRoleId, PermissionConfiguration.CreateLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000210"), RoleConfiguration.AdminRoleId, PermissionConfiguration.EditLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000211"), RoleConfiguration.AdminRoleId, PermissionConfiguration.DeleteLandId),
            // Land - Manager: full access
            Grant(new Guid("00000000-0000-0000-0000-000000000212"), RoleConfiguration.ManagerRoleId, PermissionConfiguration.ViewLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000213"), RoleConfiguration.ManagerRoleId, PermissionConfiguration.CreateLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000214"), RoleConfiguration.ManagerRoleId, PermissionConfiguration.EditLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000215"), RoleConfiguration.ManagerRoleId, PermissionConfiguration.DeleteLandId),
            // Land - Surveyor: view/create/edit, not delete (captures/updates land data in the field)
            Grant(new Guid("00000000-0000-0000-0000-000000000216"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000217"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.CreateLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000218"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.EditLandId),
            // Land - Client: view only
            Grant(new Guid("00000000-0000-0000-0000-000000000219"), RoleConfiguration.ClientRoleId, PermissionConfiguration.ViewLandId),
            // Job - Admin: full access
            Grant(new Guid("00000000-0000-0000-0000-000000000220"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000221"), RoleConfiguration.AdminRoleId, PermissionConfiguration.CreateJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000222"), RoleConfiguration.AdminRoleId, PermissionConfiguration.EditJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000223"), RoleConfiguration.AdminRoleId, PermissionConfiguration.DeleteJobId),
            // Job - Manager: full access
            Grant(new Guid("00000000-0000-0000-0000-000000000224"), RoleConfiguration.ManagerRoleId, PermissionConfiguration.ViewJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000225"), RoleConfiguration.ManagerRoleId, PermissionConfiguration.CreateJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000226"), RoleConfiguration.ManagerRoleId, PermissionConfiguration.EditJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000227"), RoleConfiguration.ManagerRoleId, PermissionConfiguration.DeleteJobId),
            // Job - Surveyor: view/create/edit, not delete
            Grant(new Guid("00000000-0000-0000-0000-000000000228"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000229"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.CreateJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000230"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.EditJobId),
            // Job - Client: view only (further scoped to their own jobs in JobService, not Casbin)
            Grant(new Guid("00000000-0000-0000-0000-000000000231"), RoleConfiguration.ClientRoleId, PermissionConfiguration.ViewJobId),
            // Client contacts - Admin/Manager/Surveyor: view+create (whoever can field the
            // call and capture a client). The Client role gets nothing here - a client
            // doesn't manage other clients.
            Grant(new Guid("00000000-0000-0000-0000-000000000232"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewClientId),
            Grant(new Guid("00000000-0000-0000-0000-000000000233"), RoleConfiguration.AdminRoleId, PermissionConfiguration.CreateClientId),
            Grant(new Guid("00000000-0000-0000-0000-000000000234"), RoleConfiguration.ManagerRoleId, PermissionConfiguration.ViewClientId),
            Grant(new Guid("00000000-0000-0000-0000-000000000235"), RoleConfiguration.ManagerRoleId, PermissionConfiguration.CreateClientId),
            Grant(new Guid("00000000-0000-0000-0000-000000000236"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewClientId),
            Grant(new Guid("00000000-0000-0000-0000-000000000237"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.CreateClientId),
            // Job view-all - Admin/Manager see every job in the workspace; Surveyor/Client
            // are scoped to jobs they've been explicitly assigned (job-scoped UserAccess).
            Grant(new Guid("00000000-0000-0000-0000-000000000238"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewAllJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000239"), RoleConfiguration.ManagerRoleId, PermissionConfiguration.ViewAllJobId)
        );
    }
}
