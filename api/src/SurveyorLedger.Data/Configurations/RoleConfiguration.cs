using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Core;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    // Fixed GUIDs required for EF HasData to produce stable migrations.
    public static readonly Guid AdminRoleId = new("00000000-0000-0000-0000-000000000001");
    public static readonly Guid SurveyorRoleId = new("00000000-0000-0000-0000-000000000003");
    public static readonly Guid ClientRoleId = new("00000000-0000-0000-0000-000000000004");
    public static readonly Guid MemberRoleId = new("00000000-0000-0000-0000-000000000005");
    public static readonly Guid FinanceRoleId = new("00000000-0000-0000-0000-000000000006");
    public static readonly Guid WorkspaceMemberRoleId = new("00000000-0000-0000-0000-000000000801");

    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.Name);

        builder.HasOne(x => x.Policy).WithMany(x => x.Roles).HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.RolePermissions).WithOne(x => x.Role).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.UserAccesses).WithOne(x => x.Role).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);

        var seededAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        builder.HasData(
            new Role { Id = AdminRoleId, Name = Constants.SystemRoles.Admin, Description = "Full access to workspace settings, members, and data.", IsSystem = true, PolicyId = AssignmentPolicyConfiguration.FullChainId, CreatedAt = seededAt, UpdatedAt = seededAt },
            new Role { Id = SurveyorRoleId, Name = Constants.SystemRoles.Surveyor, Description = "Performs assigned survey jobs.", IsSystem = true, PolicyId = AssignmentPolicyConfiguration.FullChainId, CreatedAt = seededAt, UpdatedAt = seededAt },
            new Role { Id = ClientRoleId, Name = Constants.SystemRoles.Client, Description = "Views job status and results for their organization.", IsSystem = true, PolicyId = AssignmentPolicyConfiguration.SingleScopeId, CreatedAt = seededAt, UpdatedAt = seededAt },
            new Role { Id = MemberRoleId, Name = Constants.SystemRoles.Member, Description = "Workspace membership only. No access to jobs or land until assigned.", IsSystem = true, PolicyId = AssignmentPolicyConfiguration.FullChainId, CreatedAt = seededAt, UpdatedAt = seededAt },
            new Role { Id = FinanceRoleId, Name = Constants.SystemRoles.Finance, Description = "Job-scoped view of invoices and quotations for that job only.", IsSystem = true, PolicyId = AssignmentPolicyConfiguration.SingleScopeId, CreatedAt = seededAt, UpdatedAt = seededAt },
            new Role { Id = WorkspaceMemberRoleId, Name = "WorkspaceMember", Description = "Least-privilege membership granted automatically when a role requires workspace-level presence.", IsSystem = true, PolicyId = AssignmentPolicyConfiguration.SingleScopeId, CreatedAt = seededAt, UpdatedAt = seededAt }
        );
    }
}
