using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class AuthTokenConfiguration : IEntityTypeConfiguration<AuthToken>
{
    public void Configure(EntityTypeBuilder<AuthToken> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.TokenHash);
        builder.HasIndex(x => x.ExpiresAt);

        builder.HasOne(x => x.User).WithMany(x => x.AuthTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
