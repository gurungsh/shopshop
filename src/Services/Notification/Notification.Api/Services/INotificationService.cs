using BuildingBlocks.Contracts.Orders;

namespace Notification.Api.Services
{
    public interface INotificationService
    {
        Task NotifyOrderConfirmedAsync(OrderConfirmedEvent orderConfirmed, CancellationToken cancellationToken = default);
        Task NotifyOrderPaymentFailedAsync(OrderPaymentFailedEvent orderPaymentFailed, CancellationToken cancellationToken = default);
        Task NotifyOrderCancelledAsync(OrderCancelledEvent orderCancelled, CancellationToken cancellationToken = default);
    }
}
