using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class LandConfiguration : IEntityTypeConfiguration<Land>
{
    public void Configure(EntityTypeBuilder<Land> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AreaSquareMeters).HasColumnType("decimal(14,4)");
        builder.Property(x => x.LocationShareToken).HasMaxLength(64);
        builder.Property(x => x.MapViewShareToken).HasMaxLength(64);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.OwnerName).HasMaxLength(200);
        builder.Property(x => x.OwnerPhone).HasMaxLength(30);
        builder.Property(x => x.OwnerEmail).HasMaxLength(256);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.OwnsOne(x => x.Address, a =>
        {
            a.Property(p => p.Village).HasMaxLength(150).HasColumnName("Village");
            a.Property(p => p.GramaNiladhariDivision).HasMaxLength(150).HasColumnName("GramaNiladhariDivision");
            a.Property(p => p.DivisionalSecretariat).HasMaxLength(150).HasColumnName("DivisionalSecretariat");
            a.Property(p => p.PradeshiyaSabha).HasMaxLength(150).HasColumnName("PradeshiyaSabha");
            a.Property(p => p.Korale).HasMaxLength(150).HasColumnName("Korale");
            a.Property(p => p.Hatpattu).HasMaxLength(150).HasColumnName("Hatpattu");
            a.Property(p => p.District).HasMaxLength(150).HasColumnName("District");
            a.Property(p => p.Province).HasMaxLength(150).HasColumnName("Province");
        });

        builder.HasIndex(x => x.WorkspaceId);
        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => x.LocationShareToken).IsUnique().HasFilter("[LocationShareToken] IS NOT NULL");
        builder.HasIndex(x => x.MapViewShareToken).IsUnique().HasFilter("[MapViewShareToken] IS NOT NULL");

        builder.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Surveys).WithOne(x => x.Land).HasForeignKey(x => x.LandId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Deeds).WithOne(x => x.Land).HasForeignKey(x => x.LandId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Boundaries).WithOne(x => x.Land).HasForeignKey(x => x.LandId).OnDelete(DeleteBehavior.Cascade);
    }
}
