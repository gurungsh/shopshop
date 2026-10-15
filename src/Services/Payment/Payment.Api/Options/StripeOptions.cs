namespace Payment.Api.Options;

public record StripeOptions
{
    public const string SectionName = "Stripe";

    public string SecretKey { get; init; } = string.Empty;
}
