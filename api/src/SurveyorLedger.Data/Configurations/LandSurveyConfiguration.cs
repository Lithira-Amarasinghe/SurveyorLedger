using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class LandSurveyConfiguration : IEntityTypeConfiguration<LandSurvey>
{
    public void Configure(EntityTypeBuilder<LandSurvey> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SurveyPlanNumber).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SurveyedByName).HasMaxLength(200);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.LandId);
    }
}
