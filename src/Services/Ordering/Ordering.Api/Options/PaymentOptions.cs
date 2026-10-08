namespace Ordering.Api.Options;

public record PaymentOptions
{
    public const string SectionName = "Payment";

    public int MaxRetries { get; init; } = 3;
}
