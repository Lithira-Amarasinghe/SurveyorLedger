using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class AssignmentPolicyConfiguration : IEntityTypeConfiguration<AssignmentPolicy>
{
    public static readonly Guid SingleScopeId = new("00000000-0000-0000-0000-000000000701");
    public static readonly Guid FullChainId = new("00000000-0000-0000-0000-000000000702");
    public static readonly Guid OrgOnlyId = new("00000000-0000-0000-0000-000000000703");

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
                RulesJson = "{\"grants\":{}}"
            },
            new AssignmentPolicy
            {
                Id = FullChainId,
                Name = "FullChain",
                RulesJson = "{\"grants\":{\"Workspace\":\"00000000-0000-0000-0000-000000000801\",\"Organization\":\"00000000-0000-0000-0000-000000000010\"}}"
            },
            new AssignmentPolicy
            {
                Id = OrgOnlyId,
                Name = "OrgOnly",
                RulesJson = "{\"grants\":{\"Organization\":\"00000000-0000-0000-0000-000000000010\"}}"
            }
        );
    }
}
