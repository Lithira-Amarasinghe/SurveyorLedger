using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TaxRatePercent).HasColumnType("decimal(5,2)");
        builder.Property(x => x.DiscountAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.OwnsMany(x => x.LineItems, li =>
        {
            li.ToTable("InvoiceLineItems");
            li.WithOwner().HasForeignKey("InvoiceId");
            li.HasKey(x => x.Id);
            li.Property(x => x.Description).HasMaxLength(500).IsRequired();
            li.Property(x => x.Quantity).HasColumnType("decimal(18,2)");
            li.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
        });

        builder.HasIndex(x => x.WorkspaceId);
        builder.HasIndex(x => new { x.WorkspaceId, x.Number }).IsUnique();
        builder.HasIndex(x => x.IsActive);

        builder.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Quotation).WithMany().HasForeignKey(x => x.QuotationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Payments).WithOne(x => x.Invoice).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
    }
}
