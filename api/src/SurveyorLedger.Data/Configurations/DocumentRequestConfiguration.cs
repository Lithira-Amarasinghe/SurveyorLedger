using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class DocumentRequestConfiguration : IEntityTypeConfiguration<DocumentRequest>
{
    public void Configure(EntityTypeBuilder<DocumentRequest> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Category).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.JobId);

        // Restrict, not Cascade: SQL Server rejects multiple cascade paths to the same
        // table (Job -> DocumentRequest directly, and Job -> Document -> DocumentRequest
        // via FulfilledDocumentId's SetNull). Jobs are soft-deleted in this app, so a hard
        // delete blocking on an active DocumentRequest is not a real-world concern.
        builder.HasOne(x => x.Job)
            .WithMany()
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.FulfilledDocument)
            .WithMany()
            .HasForeignKey(x => x.FulfilledDocumentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.RequestedByUser)
            .WithMany()
            .HasForeignKey(x => x.RequestedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.FulfilledByUser)
            .WithMany()
            .HasForeignKey(x => x.FulfilledBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
