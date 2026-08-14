using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(30);
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.OwnsOne(x => x.Address, a =>
        {
            a.Property(p => p.Street).HasMaxLength(255).HasColumnName("Street");
            a.Property(p => p.City).HasMaxLength(100).HasColumnName("City");
            a.Property(p => p.District).HasMaxLength(100).HasColumnName("District");
            a.Property(p => p.PostalCode).HasMaxLength(20).HasColumnName("PostalCode");
            a.Property(p => p.Country).HasMaxLength(100).HasColumnName("Country");
        });

        builder.HasIndex(x => x.WorkspaceId);
        builder.HasIndex(x => x.IsActive);

        builder.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
    }
}
