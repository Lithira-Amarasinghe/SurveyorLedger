using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Core;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class ScopeParentTypeConfiguration : IEntityTypeConfiguration<ScopeParentType>
{
    public void Configure(EntityTypeBuilder<ScopeParentType> builder)
    {
        builder.HasKey(x => x.ScopeType);
        builder.Property(x => x.ScopeType).HasMaxLength(50);
        builder.Property(x => x.ParentScopeType).HasMaxLength(50);

        builder.HasData(
            new ScopeParentType { ScopeType = Constants.ScopeTypes.Job, ParentScopeType = Constants.ScopeTypes.Workspace },
            new ScopeParentType { ScopeType = Constants.ScopeTypes.Workspace, ParentScopeType = Constants.ScopeTypes.Organization }
        );
    }
}
