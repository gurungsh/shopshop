using BuildingBlocks.Contracts.Orders;
using BuildingBlocks.Contracts.Payments;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Payment.Infrastructure.Constants;
using Payment.Infrastructure.Models;

namespace Payment.Api.IntegrationTests;

[Collection(PaymentApiCollection.Name)]
public class PaymentApiTests(PaymentApiFactory factory)
{
    private static OrderPlacedEvent Placed(Guid orderId, string paymentMethodId, decimal total = 42.50m) =>
        new(orderId, Guid.NewGuid(), total, [], DateTime.UtcNow, paymentMethodId);

    /// <summary>
    /// The consumer declares its queue asynchronously after startup, and an event published before
    /// the queue is bound is dropped. Re-publishing is safe because the handler is idempotent.
    /// </summary>
    private async Task<List<PaymentTransaction>> PublishUntilStoredAsync<T>(string routingKey, T message, Guid orderId, int expectedRows = 1)
    {
        for (var i = 0; i < 40; i++)
        {
            await factory.Bus.PublishAsync(MessagingTopology.OrdersExchange, routingKey, message);
            await Task.Delay(500);

            await using var db = factory.CreateDbContext();
            var rows = await db.PaymentTransactions.AsNoTracking().Where(p => p.OrderId == orderId).ToListAsync();
            if (rows.Count >= expectedRows)
            {
                return rows;
            }
        }

        throw new TimeoutException($"No payment transaction was stored for order {orderId}.");
    }

    private async Task WaitForDeadLetterAsync()
    {
        var deadLetterQueue = MessagingTopology.DeadLetterQueueFor("payment.order-events");

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

    #region Order Placed Tests

    [Fact]
    public async Task OrderPlaced_WithSucceedingCard_ShouldStoreTransactionAndPublishPaymentSucceeded()
    {
        // Arrange
        var orderId = Guid.NewGuid();

        // Act
        var rows = await PublishUntilStoredAsync(RoutingKeys.OrderPlaced, Placed(orderId, FakePaymentService.SucceedingPaymentMethod), orderId);

        // Assert: database
        var payment = Assert.Single(rows);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(1, payment.Attempt);
        Assert.Equal(42.50m, payment.Amount);
        Assert.NotNull(payment.ProviderReference);

        // Assert: event published through the outbox
        var succeeded = await factory.Bus.WaitForAsync<PaymentSucceededEvent>(
            PaymentRoutingKeys.PaymentSucceeded, e => e.OrderId == orderId);
        Assert.Equal(payment.Id, succeeded.PaymentId);
        Assert.Equal(1, succeeded.Attempt);
        Assert.Equal(42.50m, succeeded.Amount);
    }

    [Fact]
    public async Task OrderPlaced_WithDecliningCard_ShouldStoreFailureAndPublishPaymentFailed()
    {
        // Arrange
        var orderId = Guid.NewGuid();

        // Act
        var rows = await PublishUntilStoredAsync(RoutingKeys.OrderPlaced, Placed(orderId, "pm_card_chargeDeclined"), orderId);

        // Assert
        var payment = Assert.Single(rows);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(FakePaymentService.DeclineReason, payment.FailureReason);

        var failed = await factory.Bus.WaitForAsync<PaymentFailedEvent>(
            PaymentRoutingKeys.PaymentFailed, e => e.OrderId == orderId);
        Assert.Equal(FakePaymentService.DeclineReason, failed.Reason);
    }

    [Fact]
    public async Task OrderPlaced_DeliveredRepeatedly_ShouldChargeOnlyOnce()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var message = Placed(orderId, FakePaymentService.SucceedingPaymentMethod);
        await PublishUntilStoredAsync(RoutingKeys.OrderPlaced, message, orderId);

        // Act: a redelivery of the same event
        await factory.Bus.PublishAsync(MessagingTopology.OrdersExchange, RoutingKeys.OrderPlaced, message);
        await Task.Delay(1500);

        // Assert
        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.PaymentTransactions.CountAsync(p => p.OrderId == orderId));
        var outbox = await db.OutboxMessages.AsNoTracking().Where(m => m.Type == PaymentRoutingKeys.PaymentSucceeded).ToListAsync();
        Assert.Single(outbox, m => m.Payload.Contains(orderId.ToString()));
    }

    [Fact]
    public async Task OrderPlaced_WithInvalidAmount_ShouldBeDeadLetteredWithoutStoringAnything()
    {
        // Arrange
        var orderId = Guid.NewGuid();

        // Act
        await factory.Bus.PublishAsync(MessagingTopology.OrdersExchange, RoutingKeys.OrderPlaced, Placed(orderId, "pm_card_visa", total: 0m));

        // Assert: the poison message ends up in the dead-letter queue and nothing is stored
        await WaitForDeadLetterAsync();
        await using var db = factory.CreateDbContext();
        Assert.False(await db.PaymentTransactions.AnyAsync(p => p.OrderId == orderId));
    }

    #endregion

    #region Order Payment Retried Tests

    [Fact]
    public async Task OrderPaymentRetried_ShouldStoreSecondAttemptAndPublishResultWithAttemptNumber()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        await PublishUntilStoredAsync(RoutingKeys.OrderPlaced, Placed(orderId, "pm_card_chargeDeclined"), orderId);
        var retried = new OrderPaymentRetriedEvent(
            orderId, Guid.NewGuid(), 42.50m, Attempt: 2, FakePaymentService.SucceedingPaymentMethod, DateTime.UtcNow);

        // Act
        var rows = await PublishUntilStoredAsync(RoutingKeys.OrderPaymentRetried, retried, orderId, expectedRows: 2);

        // Assert
        Assert.Equal([1, 2], rows.Select(r => r.Attempt).Order());
        var succeeded = await factory.Bus.WaitForAsync<PaymentSucceededEvent>(
            PaymentRoutingKeys.PaymentSucceeded, e => e.OrderId == orderId);
        Assert.Equal(2, succeeded.Attempt);
    }

    #endregion
}
