using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ResourceType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ScopeType).HasMaxLength(100);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.WorkspaceId);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => new { x.ResourceType, x.ResourceId });

        builder.HasOne(x => x.Workspace).WithMany(x => x.AuditLogs).HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.User).WithMany(x => x.AuditLogs).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}
