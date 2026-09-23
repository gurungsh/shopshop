using Identity.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Configurations
{
    public class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.ToTable("Users");

            builder.HasKey(u => u.Id);

            builder.Property(u => u.Email)
                .IsRequired()
                .HasMaxLength(256);

            builder.HasIndex(u => u.Email)
                .IsUnique();

            builder.Property(u => u.PasswordHash)
                .IsRequired();

            builder.Property(u => u.Role)
                .HasMaxLength(10)
                .IsRequired();

            builder.Property(u => u.CreatedAtUtc)
                .IsRequired()
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("now()");

            builder.Property(u => u.UpdatedAtUtc)
                .IsRequired()
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("now()");
        }
    }
}