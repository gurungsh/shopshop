using System.Collections.Concurrent;
using BuildingBlocks.Contracts.Orders;
using BuildingBlocks.Messaging;

namespace Notification.Api.IntegrationTests;

[Collection(NotificationApiCollection.Name)]
public class NotificationApiTests(NotificationApiFactory factory)
{
    /// <summary>
    /// The consumer declares its queue asynchronously after startup, and an event published before
    /// the queue is bound is dropped. Re-publishing is fine here because the tests only look for
    /// "at least one" matching notification.
    /// </summary>
    private async Task<T> PublishUntilReceivedAsync<T>(
        string routingKey, T message, ConcurrentQueue<T> received, Func<T, bool> match)
    {
        for (var i = 0; i < 40; i++)
        {
            await factory.Bus.PublishAsync(MessagingTopology.OrdersExchange, routingKey, message);
            await Task.Delay(500);

            var found = received.ToArray().FirstOrDefault(match);
            if (found is not null)
            {
                return found;
            }
        }

        throw new TimeoutException($"No '{routingKey}' notification was received.");
    }

    [Fact]
    public async Task OrderConfirmed_ShouldNotifyCustomer()
    {
        // Arrange
        var message = new OrderConfirmedEvent(Guid.NewGuid(), Guid.NewGuid(), 99.90m, DateTime.UtcNow);

        // Act
        var received = await PublishUntilReceivedAsync(
            RoutingKeys.OrderConfirmed, message, factory.Notifications.Confirmed, e => e.OrderId == message.OrderId);

        // Assert
        Assert.Equal(message.CustomerId, received.CustomerId);
        Assert.Equal(99.90m, received.TotalAmount);
    }

    [Fact]
    public async Task OrderPaymentFailed_ShouldNotifyCustomerWithReasonAndRetries()
    {
        // Arrange
        var message = new OrderPaymentFailedEvent(Guid.NewGuid(), Guid.NewGuid(), "Your card was declined.", 2, DateTime.UtcNow);

        // Act
        var received = await PublishUntilReceivedAsync(
            RoutingKeys.OrderPaymentFailed, message, factory.Notifications.PaymentFailed, e => e.OrderId == message.OrderId);

        // Assert
        Assert.Equal("Your card was declined.", received.Reason);
        Assert.Equal(2, received.RetriesRemaining);
    }

    [Fact]
    public async Task OrderCancelled_ShouldNotifyCustomer()
    {
        // Arrange
        var message = new OrderCancelledEvent(Guid.NewGuid(), Guid.NewGuid(), "Customer", DateTime.UtcNow);

        // Act
        var received = await PublishUntilReceivedAsync(
            RoutingKeys.OrderCancelled, message, factory.Notifications.Cancelled, e => e.OrderId == message.OrderId);

        // Assert
        Assert.Equal("Customer", received.CancelledBy);
    }

    [Fact]
    public async Task OrderPlaced_ShouldNotReachNotification()
    {
        // Arrange: make sure the queue is bound by waiting for a routed event first
        var probe = new OrderConfirmedEvent(Guid.NewGuid(), Guid.NewGuid(), 1m, DateTime.UtcNow);
        await PublishUntilReceivedAsync(RoutingKeys.OrderConfirmed, probe, factory.Notifications.Confirmed, e => e.OrderId == probe.OrderId);
        var deadLetterQueue = MessagingTopology.DeadLetterQueueFor("notification.order-events");
        var deadBefore = await factory.Bus.GetMessageCountAsync(deadLetterQueue);
        var placed = new OrderPlacedEvent(Guid.NewGuid(), Guid.NewGuid(), 10m, [], DateTime.UtcNow, "pm_card_visa");

        // Act
        await factory.Bus.PublishAsync(MessagingTopology.OrdersExchange, RoutingKeys.OrderPlaced, placed);
        await Task.Delay(1000);

        // Assert: the queue only binds confirmed, payment-failed and cancelled, so nothing was queued and nothing new was dead-lettered
        Assert.Equal(0u, await factory.Bus.GetMessageCountAsync("notification.order-events"));
        Assert.Equal(deadBefore, await factory.Bus.GetMessageCountAsync(deadLetterQueue));
    }

    [Fact]
    public async Task MalformedMessage_ShouldBeDeadLettered()
    {
        // Arrange: wait until the queue is bound
        var probe = new OrderCancelledEvent(Guid.NewGuid(), Guid.NewGuid(), "Customer", DateTime.UtcNow);
        await PublishUntilReceivedAsync(RoutingKeys.OrderCancelled, probe, factory.Notifications.Cancelled, e => e.OrderId == probe.OrderId);

        // Act: a JSON string cannot be read as an OrderConfirmedEvent
        await factory.Bus.PublishAsync(MessagingTopology.OrdersExchange, RoutingKeys.OrderConfirmed, "not an event");

        // Assert
        var deadLetterQueue = MessagingTopology.DeadLetterQueueFor("notification.order-events");
        for (var i = 0; i < 50; i++)
        {
            if (await factory.Bus.GetMessageCountAsync(deadLetterQueue) > 0)
            {
                return;
            }

            await Task.Delay(200);
        }

        Assert.Fail($"Nothing arrived in {deadLetterQueue}.");
    }
}
