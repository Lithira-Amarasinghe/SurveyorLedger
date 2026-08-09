using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class JobLandConfiguration : IEntityTypeConfiguration<JobLand>
{
    public void Configure(EntityTypeBuilder<JobLand> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => new { x.JobId, x.LandId }).IsUnique();
        builder.HasIndex(x => x.LandId);

        builder.HasOne(x => x.Land).WithMany().HasForeignKey(x => x.LandId).OnDelete(DeleteBehavior.Restrict);
    }
}
