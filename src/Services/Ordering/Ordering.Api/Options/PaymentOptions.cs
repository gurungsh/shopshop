using System.ComponentModel.DataAnnotations;

namespace Ordering.Api.Options;

public record PaymentOptions
{
    public const string SectionName = "Payment";

    [Range(0, 20)]
    public int MaxRetries { get; init; } = 3;
}
