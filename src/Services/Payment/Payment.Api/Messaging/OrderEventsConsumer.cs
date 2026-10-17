using BuildingBlocks.Contracts.Orders;
using BuildingBlocks.Messaging;
using Payment.Api.DTOs;
using Payment.Api.Services;

namespace Payment.Api.Messaging;

// Charges the first attempt on order.placed and each customer retry on order.payment-retried
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

    protected override string QueueName => "payment.order-events";
    protected override string ExchangeName => MessagingTopology.OrdersExchange;
    protected override IReadOnlyCollection<string> BindingKeys => [RoutingKeys.OrderPlaced, RoutingKeys.OrderPaymentRetried];

    protected override async Task HandleAsync(
        string routingKey,
        string messageId,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        PaymentChargeRequest request;

        switch (routingKey)
        {
            case RoutingKeys.OrderPlaced:
                var placed = MessageSerializer.Deserialize<OrderPlacedEvent>(body);
                request = new PaymentChargeRequest(placed.OrderId, 1, placed.TotalAmount, placed.PaymentMethodId);
                break;

            case RoutingKeys.OrderPaymentRetried:
                var retried = MessageSerializer.Deserialize<OrderPaymentRetriedEvent>(body);
                request = new PaymentChargeRequest(retried.OrderId, retried.Attempt, retried.TotalAmount, retried.PaymentMethodId);
                break;

            default:
                _logger.LogWarning("Ignoring message with unknown routing key. Routing Key:{RoutingKey}, Message Id:{MessageId}", routingKey, messageId);
                return;
        }

        using var scope = _scopeFactory.CreateScope();
        var processingService = scope.ServiceProvider.GetRequiredService<IPaymentProcessingService>();
        var result = await processingService.ProcessPaymentAsync(request, cancellationToken);

        // A message that can never succeed goes to the dead-letter queue
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Error);
        }
    }
}
