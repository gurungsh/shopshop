using System.Collections.Concurrent;
using BuildingBlocks.Contracts.Orders;
using Notification.Api.Services;

namespace Notification.Api.IntegrationTests;

/// <summary>Records what the consumer hands to the notification service so tests can assert on it.</summary>
public sealed class RecordingNotificationService : INotificationService
{
    public ConcurrentQueue<OrderConfirmedEvent> Confirmed { get; } = new();
    public ConcurrentQueue<OrderPaymentFailedEvent> PaymentFailed { get; } = new();
    public ConcurrentQueue<OrderCancelledEvent> Cancelled { get; } = new();

    public Task NotifyOrderConfirmedAsync(OrderConfirmedEvent orderConfirmed, CancellationToken cancellationToken = default)
    {
        Confirmed.Enqueue(orderConfirmed);
        return Task.CompletedTask;
    }

    public Task NotifyOrderPaymentFailedAsync(OrderPaymentFailedEvent orderPaymentFailed, CancellationToken cancellationToken = default)
    {
        PaymentFailed.Enqueue(orderPaymentFailed);
        return Task.CompletedTask;
    }

    public Task NotifyOrderCancelledAsync(OrderCancelledEvent orderCancelled, CancellationToken cancellationToken = default)
    {
        Cancelled.Enqueue(orderCancelled);
        return Task.CompletedTask;
    }
}
