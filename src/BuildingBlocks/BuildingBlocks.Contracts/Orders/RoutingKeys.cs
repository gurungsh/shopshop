namespace BuildingBlocks.Contracts.Orders
{
    public static class RoutingKeys
    {
        public const string OrderPlaced = "order.placed";
        public const string OrderCancelled = "order.cancelled";
        public const string OrderPaymentRetried = "order.payment-retried";
        public const string OrderConfirmed = "order.confirmed";
        public const string OrderPaymentFailed = "order.payment-failed";

        public const string AllOrderEvents = "order.*";
    }
}
