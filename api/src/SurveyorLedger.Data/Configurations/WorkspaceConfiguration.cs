using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SubscriptionTier).HasMaxLength(50).HasDefaultValue("Free");
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.Property(x => x.LetterheadCompanyName).HasMaxLength(255);
        builder.Property(x => x.LetterheadAddress).HasMaxLength(1000);
        builder.Property(x => x.LetterheadPhone).HasMaxLength(50);
        builder.Property(x => x.LetterheadEmail).HasMaxLength(255);
        builder.Property(x => x.LetterheadRegistrationNumber).HasMaxLength(100);
        builder.Property(x => x.LetterheadLogoPath).HasMaxLength(500);

        builder.HasIndex(x => x.OwnerId);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.IsActive);

        builder.HasMany(x => x.Subscriptions).WithOne(x => x.Workspace).HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
