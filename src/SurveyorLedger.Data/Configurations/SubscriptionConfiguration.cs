using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Tier).HasMaxLength(50).HasDefaultValue("Free");
        builder.Property(x => x.Status).HasMaxLength(50).HasDefaultValue("Active");
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.WorkspaceId);
        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.Workspace).WithMany(x => x.Subscriptions).HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
