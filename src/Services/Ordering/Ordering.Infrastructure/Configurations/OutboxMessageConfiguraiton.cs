using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering.Infrastructure.Models;

namespace Ordering.Infrastructure.Configurations;

public class OutboxMessageConfiguraiton : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(m => m.Payload)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(m => m.OccurredAtUtc)
            .IsRequired();

        builder.Property(m => m.LastError)
            .HasMaxLength(2000);

        builder.HasIndex(m => m.ProcessedAtUtc);
    }
}
