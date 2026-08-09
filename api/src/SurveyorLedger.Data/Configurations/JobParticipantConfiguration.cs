using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class JobParticipantConfiguration : IEntityTypeConfiguration<JobParticipant>
{
    public void Configure(EntityTypeBuilder<JobParticipant> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ParticipantType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.AddedAt).HasDefaultValueSql("GETUTCDATE()");

        // A given user can't be added twice with the same role on the same job - re-adding
        // (e.g. after removal) should reactivate the existing row, not create a duplicate.
        builder.HasIndex(x => new { x.JobId, x.UserId, x.ParticipantType }).IsUnique();
        builder.HasIndex(x => x.UserId);

        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
