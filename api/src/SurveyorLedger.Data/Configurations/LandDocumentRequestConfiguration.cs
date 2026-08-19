using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class LandDocumentRequestConfiguration : IEntityTypeConfiguration<LandDocumentRequest>
{
    public void Configure(EntityTypeBuilder<LandDocumentRequest> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Category).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.Property(x => x.OwnerType).HasMaxLength(20).IsRequired();

        builder.HasIndex(x => x.LandId);
        builder.HasIndex(x => new { x.OwnerType, x.OwnerId });

        builder.HasOne(x => x.Land)
            .WithMany()
            .HasForeignKey(x => x.LandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RequestedByUser)
            .WithMany()
            .HasForeignKey(x => x.RequestedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.FulfilledByUser)
            .WithMany()
            .HasForeignKey(x => x.FulfilledBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.TargetRole).HasMaxLength(20);

        builder.Property(x => x.ShareToken).HasMaxLength(64);
        builder.HasIndex(x => x.ShareToken).IsUnique().HasFilter("[ShareToken] IS NOT NULL");
    }
}
