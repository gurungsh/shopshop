using BuildingBlocks.Contracts.Orders;
using BuildingBlocks.Messaging;
using Notification.Api.Services;

namespace Notification.Api.Messaging
{
    public class OrderEventsConsumer : RabbitMqConsumer
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OrderEventsConsumer> _logger;

        public OrderEventsConsumer(
            RabbitMqConnection connection,
            IServiceScopeFactory scopeFactory,
            ILogger<OrderEventsConsumer> logger) : base(connection, logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override string QueueName => "notification.order-events";
        protected override string ExchangeName => MessagingTopology.OrdersExchange;
        protected override IReadOnlyCollection<string> BindingKeys =>
            [RoutingKeys.OrderConfirmed, RoutingKeys.OrderPaymentFailed, RoutingKeys.OrderCancelled];

        protected override async Task HandleAsync(
            string routingKey,
            string messageId,
            ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            switch (routingKey)
            {
                case RoutingKeys.OrderConfirmed:
                    await notificationService.NotifyOrderConfirmedAsync(
                        MessageSerializer.Deserialize<OrderConfirmedEvent>(body), cancellationToken);
                    break;

                case RoutingKeys.OrderPaymentFailed:
                    await notificationService.NotifyOrderPaymentFailedAsync(
                        MessageSerializer.Deserialize<OrderPaymentFailedEvent>(body), cancellationToken);
                    break;

                case RoutingKeys.OrderCancelled:
                    await notificationService.NotifyOrderCancelledAsync(
                        MessageSerializer.Deserialize<OrderCancelledEvent>(body), cancellationToken);
                    break;

                default:
                    _logger.LogWarning("Ignoring message with unknown routing key. Routing Key:{RoutingKey}, Message Id:{MessageId}", routingKey, messageId);
                    break;
            }
        }
    }
}
