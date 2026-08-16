using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Core;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class RoleScopeConfiguration : IEntityTypeConfiguration<RoleScope>
{
    public void Configure(EntityTypeBuilder<RoleScope> builder)
    {
        builder.HasKey(x => new { x.RoleId, x.ScopeType });
        builder.Property(x => x.ScopeType).HasMaxLength(50).IsRequired();

        builder.HasOne(x => x.Role).WithMany(x => x.RoleScopes).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);

        builder.HasData(
            new RoleScope { RoleId = RoleConfiguration.AdminRoleId, ScopeType = Constants.ScopeTypes.Workspace },
            new RoleScope { RoleId = RoleConfiguration.SurveyorRoleId, ScopeType = Constants.ScopeTypes.Workspace },
            new RoleScope { RoleId = RoleConfiguration.SurveyorRoleId, ScopeType = Constants.ScopeTypes.Job },
            new RoleScope { RoleId = RoleConfiguration.ClientRoleId, ScopeType = Constants.ScopeTypes.Job },
            new RoleScope { RoleId = RoleConfiguration.MemberRoleId, ScopeType = Constants.ScopeTypes.Workspace }
        );
    }
}
