using Identity.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(t => t.TokenHash)
            .IsUnique();

        builder.Property(t => t.ExpiresAtUtc)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(t => t.CreatedAtUtc)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");

        // Concurrency token: two refreshes racing on the same token can't both revoke it and get new tokens.
        builder.Property(t => t.RevokedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
