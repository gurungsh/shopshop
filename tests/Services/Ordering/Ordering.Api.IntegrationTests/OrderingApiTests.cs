using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Contracts.Orders;
using BuildingBlocks.Contracts.Payments;
using BuildingBlocks.Core;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Ordering.Api.DTOs;
using Ordering.Infrastructure.Constants;

namespace Ordering.Api.IntegrationTests;

[Collection(OrderingApiCollection.Name)]
public class OrderingApiTests(OrderingApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client = factory.CreateClient();

    private static HttpRequestMessage As(Guid userId, string role, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, role);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }
        return request;
    }

    private async Task<OrderDetailResponse> PlaceOrderAsync(Guid userId, decimal unitPrice = 25m, int quantity = 2)
    {
        var product = factory.Catalog.AddProduct($"Product {Guid.NewGuid():N}", unitPrice);

        var response = await _client.SendAsync(As(userId, Roles.Customer, HttpMethod.Post, "/api/orders",
            new CreateOrderRequest("1 Test Street", [new CreateOrderItemRequest(product.Id, quantity)], "pm_card_visa")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(JsonOptions);
        return created!.Order;
    }

    private async Task<OrderDetailResponse> GetOrderAsync(Guid userId, Guid orderId)
    {
        var response = await _client.SendAsync(As(userId, Roles.Customer, HttpMethod.Get, $"/api/orders/{orderId}"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderDetailResponse>(JsonOptions))!;
    }

    /// <summary>
    /// The Ordering consumer declares its queue asynchronously after startup, and an event published
    /// before the queue is bound is dropped. Re-publishing is safe because stale results are ignored.
    /// </summary>
    private async Task<OrderDetailResponse> PublishUntilStatusAsync<T>(
        Guid userId, Guid orderId, string routingKey, T paymentEvent, OrderStatus expected)
    {
        for (var i = 0; i < 40; i++)
        {
            await factory.Bus.PublishAsync(MessagingTopology.PaymentsExchange, routingKey, paymentEvent);
            await Task.Delay(500);

            var order = await GetOrderAsync(userId, orderId);
            if (order.Status == expected)
            {
                return order;
            }
        }

        throw new TimeoutException($"Order {orderId} never reached {expected}.");
    }

    #region Authorization Tests

    [Fact]
    public async Task CreateOrder_WithoutToken_ShouldReturnUnauthorized()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/orders",
            new CreateOrderRequest("1 Test Street", [new CreateOrderItemRequest(Guid.NewGuid(), 1)], "pm_card_visa"));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOrder_AsAnotherCustomer_ShouldReturnNotFound()
    {
        // Arrange
        var order = await PlaceOrderAsync(Guid.NewGuid());

        // Act
        var response = await _client.SendAsync(
            As(Guid.NewGuid(), Roles.Customer, HttpMethod.Get, $"/api/orders/{order.Id}"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Create Order Tests

    [Fact]
    public async Task CreateOrder_ShouldSnapshotPricesAndPublishOrderPlacedThroughOutbox()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var order = await PlaceOrderAsync(userId, unitPrice: 25m, quantity: 2);

        // Assert: database state
        await using var db = factory.CreateDbContext();
        var row = await db.Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Pending, row.Status);
        Assert.Equal(50m, row.TotalAmount);
        Assert.Equal(25m, Assert.Single(row.Items).UnitPrice);

        // Assert: the event reaches RabbitMQ and the outbox row is marked processed
        var placed = await factory.Bus.WaitForAsync<OrderPlacedEvent>(RoutingKeys.OrderPlaced, e => e.OrderId == order.Id);
        Assert.Equal(userId, placed.CustomerId);
        Assert.Equal(50m, placed.TotalAmount);
        Assert.Equal("pm_card_visa", placed.PaymentMethodId);

        await AssertOutboxProcessedAsync(RoutingKeys.OrderPlaced, order.Id);
    }

    [Fact]
    public async Task CreateOrder_WithOnlyUnknownProducts_ShouldReturnBadRequestAndStoreNothing()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var response = await _client.SendAsync(As(userId, Roles.Customer, HttpMethod.Post, "/api/orders",
            new CreateOrderRequest("1 Test Street", [new CreateOrderItemRequest(Guid.NewGuid(), 1)], "pm_card_visa")));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = factory.CreateDbContext();
        Assert.False(await db.Orders.AnyAsync(o => o.UserId == userId));
    }

    #endregion

    #region Payment Result Tests

    [Fact]
    public async Task PaymentSucceeded_ShouldConfirmOrderAndPublishOrderConfirmed()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var order = await PlaceOrderAsync(userId);
        var succeeded = new PaymentSucceededEvent(order.Id, Guid.NewGuid(), 1, order.TotalAmount, "pi_test", DateTime.UtcNow);

        // Act
        var confirmed = await PublishUntilStatusAsync(
            userId, order.Id, PaymentRoutingKeys.PaymentSucceeded, succeeded, OrderStatus.Confirmed);

        // Assert
        Assert.Equal(OrderStatus.Confirmed, confirmed.Status);
        var published = await factory.Bus.WaitForAsync<OrderConfirmedEvent>(
            RoutingKeys.OrderConfirmed, e => e.OrderId == order.Id);
        Assert.Equal(order.TotalAmount, published.TotalAmount);
    }

    [Fact]
    public async Task PaymentFailed_ShouldMarkOrderPaymentFailedAndAllowRetry()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var order = await PlaceOrderAsync(userId);
        var failed = new PaymentFailedEvent(order.Id, Guid.NewGuid(), 1, order.TotalAmount, "Your card was declined.", DateTime.UtcNow);

        // Act
        var afterFailure = await PublishUntilStatusAsync(
            userId, order.Id, PaymentRoutingKeys.PaymentFailed, failed, OrderStatus.PaymentFailed);

        // Assert: failure is stored and announced
        Assert.Equal("Your card was declined.", afterFailure.PaymentFailureReason);
        Assert.Equal(3, afterFailure.PaymentRetriesRemaining);
        var announced = await factory.Bus.WaitForAsync<OrderPaymentFailedEvent>(
            RoutingKeys.OrderPaymentFailed, e => e.OrderId == order.Id);
        Assert.Equal(3, announced.RetriesRemaining);

        // Act: the customer retries
        var retry = await _client.SendAsync(As(userId, Roles.Customer, HttpMethod.Post,
            $"/api/orders/{order.Id}/retry-payment", new RetryOrderPaymentRequest("pm_card_mastercard")));

        // Assert: attempt 2 is requested through the outbox
        retry.EnsureSuccessStatusCode();
        var retried = await factory.Bus.WaitForAsync<OrderPaymentRetriedEvent>(
            RoutingKeys.OrderPaymentRetried, e => e.OrderId == order.Id);
        Assert.Equal(2, retried.Attempt);
        Assert.Equal("pm_card_mastercard", retried.PaymentMethodId);
    }

    #endregion

    #region Cancel Tests

    [Fact]
    public async Task CancelOrder_Pending_ShouldCancelAndPublishOnce()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var order = await PlaceOrderAsync(userId);

        // Act
        var first = await _client.SendAsync(As(userId, Roles.Customer, HttpMethod.Post, $"/api/orders/{order.Id}/cancel"));
        var second = await _client.SendAsync(As(userId, Roles.Customer, HttpMethod.Post, $"/api/orders/{order.Id}/cancel"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(OrderStatus.Cancelled, (await GetOrderAsync(userId, order.Id)).Status);

        await factory.Bus.WaitForAsync<OrderCancelledEvent>(RoutingKeys.OrderCancelled, e => e.OrderId == order.Id);
        await using var db = factory.CreateDbContext();
        var cancelled = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type == RoutingKeys.OrderCancelled)
            .ToListAsync();
        Assert.Single(cancelled, m => m.Payload.Contains(order.Id.ToString()));
    }

    #endregion

    private async Task AssertOutboxProcessedAsync(string routingKey, Guid orderId)
    {
        for (var i = 0; i < 50; i++)
        {
            await using var db = factory.CreateDbContext();
            // Payload is a jsonb column, so it is filtered in memory rather than with LIKE
            var messages = await db.OutboxMessages.AsNoTracking()
                .Where(m => m.Type == routingKey)
                .ToListAsync();
            var message = messages.Single(m => m.Payload.Contains(orderId.ToString()));

            if (message.ProcessedAtUtc is not null)
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail($"Outbox message '{routingKey}' for order {orderId} was never marked processed.");
    }
}
