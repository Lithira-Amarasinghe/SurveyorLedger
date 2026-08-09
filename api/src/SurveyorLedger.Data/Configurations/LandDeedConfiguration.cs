using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class LandDeedConfiguration : IEntityTypeConfiguration<LandDeed>
{
    public void Configure(EntityTypeBuilder<LandDeed> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DeedNumber).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.IsCurrent).HasDefaultValue(false);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.LandId);
    }
}
