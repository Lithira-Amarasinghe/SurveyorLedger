using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class OrganizationSubscriptionConfiguration : IEntityTypeConfiguration<OrganizationSubscription>
{
    public void Configure(EntityTypeBuilder<OrganizationSubscription> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Tier).HasMaxLength(50).HasDefaultValue("Free");
        builder.Property(x => x.Status).HasMaxLength(50).HasDefaultValue("Active");
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.OrganizationId).IsUnique();
        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.Organization).WithOne(x => x.Subscription).HasForeignKey<OrganizationSubscription>(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}
