using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class StaffPaymentConfiguration : IEntityTypeConfiguration<StaffPayment>
{
    public void Configure(EntityTypeBuilder<StaffPayment> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.WorkspaceId);
        builder.HasIndex(x => x.JobId);
        builder.HasIndex(x => x.UserId);

        builder.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RecordedByUser).WithMany().HasForeignKey(x => x.RecordedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
