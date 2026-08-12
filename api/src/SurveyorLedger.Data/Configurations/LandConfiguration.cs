using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class LandConfiguration : IEntityTypeConfiguration<Land>
{
    public void Configure(EntityTypeBuilder<Land> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Size).HasColumnType("decimal(18,2)");
        builder.Property(x => x.SizeUnit).HasMaxLength(20);
        builder.Property(x => x.GpsCoordinates).HasMaxLength(100);
        builder.Property(x => x.Latitude).HasColumnType("decimal(9,6)");
        builder.Property(x => x.Longitude).HasColumnType("decimal(9,6)");
        builder.Property(x => x.LocationShareToken).HasMaxLength(64);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.OwnerName).HasMaxLength(200);
        builder.Property(x => x.OwnerPhone).HasMaxLength(30);
        builder.Property(x => x.OwnerEmail).HasMaxLength(256);
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
        builder.HasIndex(x => x.LocationShareToken).IsUnique().HasFilter("[LocationShareToken] IS NOT NULL");

        builder.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Surveys).WithOne(x => x.Land).HasForeignKey(x => x.LandId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Deeds).WithOne(x => x.Land).HasForeignKey(x => x.LandId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Boundaries).WithOne(x => x.Land).HasForeignKey(x => x.LandId).OnDelete(DeleteBehavior.Cascade);
    }
}
