using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class JobBudgetConfiguration : IEntityTypeConfiguration<JobBudget>
{
    public void Configure(EntityTypeBuilder<JobBudget> builder)
    {
        builder.HasKey(x => x.JobId);
        builder.Property(x => x.EstimatedFee).HasColumnType("decimal(18,2)");
        builder.Property(x => x.EstimatedCost).HasColumnType("decimal(18,2)");

        builder.HasOne(x => x.Job).WithOne().HasForeignKey<JobBudget>(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.UpdatedByPerson).WithMany().HasForeignKey(x => x.UpdatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
