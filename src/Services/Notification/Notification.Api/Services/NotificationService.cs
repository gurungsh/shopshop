using BuildingBlocks.Contracts.Orders;

namespace Notification.Api.Services
{
    // First iteration: notifications are only logged.
    public class NotificationService : INotificationService
    {
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(ILogger<NotificationService> logger)
        {
            _logger = logger;
        }

        public Task NotifyOrderConfirmedAsync(OrderConfirmedEvent orderConfirmed, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Order confirmed. Order Id:{OrderId}, Customer Id:{CustomerId}, Total Amount:{TotalAmount}",
                orderConfirmed.OrderId, orderConfirmed.CustomerId, orderConfirmed.TotalAmount);

            return Task.CompletedTask;
        }

        public Task NotifyOrderPaymentFailedAsync(OrderPaymentFailedEvent orderPaymentFailed, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Order payment failed. Order Id:{OrderId}, Customer Id:{CustomerId}, Reason:{Reason}, Retries Remaining:{RetriesRemaining}",
                orderPaymentFailed.OrderId, orderPaymentFailed.CustomerId, orderPaymentFailed.Reason, orderPaymentFailed.RetriesRemaining);

            return Task.CompletedTask;
        }

        public Task NotifyOrderCancelledAsync(OrderCancelledEvent orderCancelled, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Order cancelled. Order Id:{OrderId}, Customer Id:{CustomerId}, Cancelled By:{CancelledBy}",
                orderCancelled.OrderId, orderCancelled.CustomerId, orderCancelled.CancelledBy);

            return Task.CompletedTask;
        }
    }
}
