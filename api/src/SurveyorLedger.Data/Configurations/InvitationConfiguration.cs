using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ScopeType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Token).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("Pending");
        builder.Property(x => x.DescendantScopeType).HasMaxLength(50);
        builder.Property(x => x.EmailFailed).HasDefaultValue(false);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.Token).IsUnique();
        builder.HasIndex(x => new { x.ScopeType, x.ScopeId, x.Email });
        builder.HasIndex(x => x.UserId);

        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.InvitedByUser).WithMany().HasForeignKey(x => x.InvitedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
