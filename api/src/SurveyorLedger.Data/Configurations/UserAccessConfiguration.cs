using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class UserAccessConfiguration : IEntityTypeConfiguration<UserAccess>
{
    public void Configure(EntityTypeBuilder<UserAccess> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ScopeType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        // Filtered to active rows only - a soft-deleted (IsActive = false) UserAccess from a
        // prior role/removal must not permanently block a user from ever holding that exact
        // (role, scope) combination again (e.g. promoted, removed, re-invited to the same role).
        builder.HasIndex(x => new { x.UserId, x.RoleId, x.ScopeType, x.ScopeId })
            .IsUnique()
            .HasFilter("[IsActive] = 1");
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.RoleId);
        builder.HasIndex(x => new { x.ScopeType, x.ScopeId });
        builder.HasIndex(x => x.IsActive);

        builder.HasOne(x => x.User).WithMany(x => x.UserAccesses).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Role).WithMany(x => x.UserAccesses).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}
