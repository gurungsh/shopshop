namespace Payment.Api.Options
{
    public sealed class StripeOptions
    {
        public const string SectionName = "Stripe";

        public string SecretKey { get; set; } = string.Empty;
    }
}
