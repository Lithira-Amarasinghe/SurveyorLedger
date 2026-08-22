using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.OwnerId);
        builder.HasIndex(x => x.IsActive);

        builder.HasOne(x => x.Owner).WithMany(x => x.OwnedOrganizations).HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        // Workspace <-> Organization relationship is configured on WorkspaceConfiguration's side
        // (IsRequired there matters - Workspace.OrganizationId is non-nullable).
    }
}
