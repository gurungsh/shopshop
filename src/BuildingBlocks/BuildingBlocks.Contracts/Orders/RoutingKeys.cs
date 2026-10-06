namespace BuildingBlocks.Contracts.Orders
{
    public static class RoutingKeys
    {
        public const string OrderPlaced = "order.placed";
        public const string OrderCancelled = "order.cancelled";

        public const string AllOrderEvents = "order.*";
    }
}
