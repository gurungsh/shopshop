using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payment.Infrastructure.Models;

namespace Payment.Infrastructure.Configurations;

public class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.ToTable("PaymentTransactions");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.OrderId)
            .IsRequired();

        builder.Property(p => p.Attempt)
            .IsRequired();

        builder.Property(p => p.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.FailureReason)
            .HasMaxLength(500);

        builder.Property(p => p.ProviderReference)
            .HasMaxLength(100);

        builder.Property(p => p.CreatedAtUtc)
            .IsRequired();

        builder.Property(p => p.UpdatedAtUtc)
            .IsRequired();

        // One payment per order attempt: a redelivered payment request is never charged twice
        builder.HasIndex(p => new { p.OrderId, p.Attempt })
            .IsUnique();
    }
}
