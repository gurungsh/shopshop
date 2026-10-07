using BuildingBlocks.Contracts.Payments;
using BuildingBlocks.Core;
using BuildingBlocks.Messaging;
using Ordering.Api.Services;

namespace Ordering.Api.Messaging
{
    public class PaymentEventsConsumer : RabbitMqConsumer
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PaymentEventsConsumer> _logger;

        public PaymentEventsConsumer(
            RabbitMqConnection connection,
            IServiceScopeFactory scopeFactory,
            ILogger<PaymentEventsConsumer> logger) : base(connection, logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override string QueueName => "ordering.payment-events";
        protected override string ExchangeName => MessagingTopology.PaymentsExchange;
        protected override IReadOnlyCollection<string> BindingKeys => [PaymentRoutingKeys.AllPaymentEvents];

        protected override async Task HandleAsync(
            string routingKey,
            string messageId,
            ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var orderingService = scope.ServiceProvider.GetRequiredService<IOrderingService>();

            Result<bool> result;

            switch (routingKey)
            {
                case PaymentRoutingKeys.PaymentSucceeded:
                    var succeeded = MessageSerializer.Deserialize<PaymentSucceededEvent>(body);
                    result = await orderingService.ConfirmOrderPaymentAsync(succeeded.OrderId, succeeded.Attempt);
                    break;

                case PaymentRoutingKeys.PaymentFailed:
                    var failed = MessageSerializer.Deserialize<PaymentFailedEvent>(body);
                    result = await orderingService.FailOrderPaymentAsync(failed.OrderId, failed.Attempt, failed.Reason);
                    break;

                default:
                    _logger.LogWarning("Ignoring message with unknown routing key. Routing Key:{RoutingKey}, Message Id:{MessageId}", routingKey, messageId);
                    return;
            }

            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(result.Error);
            }
        }
    }
}
