using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class AssignmentPolicyConfiguration : IEntityTypeConfiguration<AssignmentPolicy>
{
    public static readonly Guid SingleScopeId = new("00000000-0000-0000-0000-000000000701");
    public static readonly Guid FullChainId = new("00000000-0000-0000-0000-000000000702");

    public void Configure(EntityTypeBuilder<AssignmentPolicy> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RulesJson).IsRequired();

        builder.HasMany(x => x.Roles).WithOne(x => x.Policy).HasForeignKey(x => x.PolicyId);

        builder.HasData(
            new AssignmentPolicy
            {
                Id = SingleScopeId,
                Name = "SingleScope",
                RulesJson = "{\"ancestors\":[]}"
            },
            new AssignmentPolicy
            {
                Id = FullChainId,
                Name = "FullChain",
                RulesJson = "{\"ancestors\":[{\"scopeType\":\"Workspace\",\"grantRoleId\":\"00000000-0000-0000-0000-000000000801\"}]}"
            }
        );
    }
}
