namespace BuildingBlocks.Contracts.Payments
{
    public static class PaymentRoutingKeys
    {
        public const string PaymentSucceeded = "payment.succeeded";
        public const string PaymentFailed = "payment.failed";

        public const string AllPaymentEvents = "payment.*";
    }
}
