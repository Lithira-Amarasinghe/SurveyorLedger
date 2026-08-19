using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.StoredPath).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Category).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.Property(x => x.OwnerType).HasMaxLength(30);

        builder.HasIndex(x => x.JobId);
        builder.HasIndex(x => new { x.OwnerType, x.OwnerId });

        builder.HasOne(x => x.Job)
            .WithMany()
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.UploadedByUser)
            .WithMany()
            .HasForeignKey(x => x.UploadedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
