namespace BuildingBlocks.Messaging;

public static class MessagingTopology
{
    public const string OrdersExchange = "shopshop.orders";

    public const string PaymentsExchange = "shopshop.payments";

    public const string DeadLetterExchange = "shopshop.dlx";

    public static string DeadLetterQueueFor(string queueName) => $"{queueName}.dead";
}
