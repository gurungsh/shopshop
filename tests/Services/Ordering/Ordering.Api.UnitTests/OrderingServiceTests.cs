using System.Diagnostics;
using System.Text;
using BuildingBlocks.Contracts.Orders;
using BuildingBlocks.Core;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Ordering.Api.DTOs;
using Ordering.Api.Options;
using Ordering.Api.Services;
using Ordering.Infrastructure.Constants;
using Ordering.Infrastructure.Data;
using Ordering.Infrastructure.Models;

namespace Ordering.Api.UnitTests;

public class OrderingServiceTests
{
    private const int MaxRetries = 3;

    private readonly OrderingService _orderingService;
    private readonly OrderingDbContext _dbContext;
    private readonly Mock<ICatalogServiceClient> _mockCatalogClient;
    private readonly Mock<ILogger<OrderingService>> _mockLogger;

    public OrderingServiceTests()
    {
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseInMemoryDatabase($"test-db-{Guid.NewGuid()}")
            .Options;

        _dbContext = new OrderingDbContext(options);
        _mockCatalogClient = new Mock<ICatalogServiceClient>();
        _mockLogger = new Mock<ILogger<OrderingService>>();

        _orderingService = CreateService(MaxRetries);
    }

    private OrderingService CreateService(int maxRetries)
    {
        return new OrderingService(
            _dbContext,
            _mockCatalogClient.Object,
            Microsoft.Extensions.Options.Options.Create(new PaymentOptions { MaxRetries = maxRetries }),
            _mockLogger.Object);
    }

    private static ProductDetails CreateTestProduct(
        Guid? id = null,
        string name = "Running Shoe",
        decimal price = 49.99m,
        bool isActive = true)
    {
        return new ProductDetails(
            id ?? Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            "Description",
            $"SKU-{Guid.NewGuid():N}",
            price,
            isActive,
            DateTime.UtcNow,
            DateTime.UtcNow);
    }

    private void SetupCatalogProducts(params ProductDetails[] products)
    {
        _mockCatalogClient
            .Setup(c => c.GetProductsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyDictionary<Guid, ProductDetails>>.Success(
                products.ToDictionary(p => p.Id)));
    }

    private static Order CreateTestOrder(
        Guid? userId = null,
        OrderStatus status = OrderStatus.Pending,
        decimal totalAmount = 100m,
        string shippingAddress = "1 Test Street")
    {
        return new Order
        {
            UserId = userId ?? Guid.NewGuid(),
            Status = status,
            TotalAmount = totalAmount,
            ShippingAddress = shippingAddress,
            Items =
            [
                new OrderItem
                {
                    Id = Guid.NewGuid(),
                    ProductId = Guid.NewGuid(),
                    ProductName = "Running Shoe",
                    UnitPrice = totalAmount,
                    Quantity = 1
                }
            ]
        };
    }
    private static TEvent ReadOutboxEvent<TEvent>(OutboxMessage message)
    {
        return MessageSerializer.Deserialize<TEvent>(Encoding.UTF8.GetBytes(message.Payload));
    }

    #region CreateOrderAsync Tests

    [Fact]
    public async Task CreateOrderAsync_WithAllValidItems_ShouldCreatePendingOrderUsingCatalogPrices()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var shoe = CreateTestProduct(name: "Running Shoe", price: 50m);
        var boot = CreateTestProduct(name: "Hiking Boot", price: 120m);
        SetupCatalogProducts(shoe, boot);

        var request = new CreateOrderRequest("1 Test Street",
        [
            new CreateOrderItemRequest(shoe.Id, 2),
            new CreateOrderItemRequest(boot.Id, 1)
        ]);

        // Act
        var result = await _orderingService.CreateOrderAsync(customerId, request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value.SkippedProductIds);

        var order = result.Value.Order;
        Assert.Equal(customerId, order.UserId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal("1 Test Street", order.ShippingAddress);
        Assert.Equal(220m, order.TotalAmount);
        Assert.Equal(2, order.Items.Length);
        Assert.Contains(order.Items, i => i.ProductId == shoe.Id && i.ProductName == "Running Shoe" && i.UnitPrice == 50m && i.Quantity == 2);

        var savedOrder = await _dbContext.Orders.Include(o => o.Items).SingleAsync();
        Assert.Equal(220m, savedOrder.TotalAmount);
        Assert.Equal(2, savedOrder.Items.Count);
    }

    [Fact]
    public async Task CreateOrderAsync_WithMissingAndInactiveProducts_ShouldSkipThemAndCreateOrder()
    {
        // Arrange
        var active = CreateTestProduct(price: 10m);
        var inactive = CreateTestProduct(isActive: false);
        var missingId = Guid.NewGuid();
        SetupCatalogProducts(active, inactive);

        var request = new CreateOrderRequest("1 Test Street",
        [
            new CreateOrderItemRequest(active.Id, 3),
            new CreateOrderItemRequest(inactive.Id, 1),
            new CreateOrderItemRequest(missingId, 1)
        ]);

        // Act
        var result = await _orderingService.CreateOrderAsync(Guid.NewGuid(), request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.SkippedProductIds.Length);
        Assert.Contains(inactive.Id, result.Value.SkippedProductIds);
        Assert.Contains(missingId, result.Value.SkippedProductIds);
        Assert.Single(result.Value.Order.Items);
        Assert.Equal(30m, result.Value.Order.TotalAmount);
    }

    [Fact]
    public async Task CreateOrderAsync_WithNoValidItems_ShouldReturnBadRequestAndAddNothing()
    {
        // Arrange
        var inactive = CreateTestProduct(isActive: false);
        SetupCatalogProducts(inactive);

        var request = new CreateOrderRequest("1 Test Street",
        [
            new CreateOrderItemRequest(inactive.Id, 1),
            new CreateOrderItemRequest(Guid.NewGuid(), 1)
        ]);

        // Act
        var result = await _orderingService.CreateOrderAsync(Guid.NewGuid(), request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorType.BadRequest, result.ErrorType);
        Assert.Empty(_dbContext.Orders);
        Assert.Empty(_dbContext.OutboxMessages);
    }

    [Fact]
    public async Task CreateOrderAsync_WhenCatalogUnavailable_ShouldReturnServiceUnavailable()
    {
        // Arrange
        _mockCatalogClient
            .Setup(c => c.GetProductsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyDictionary<Guid, ProductDetails>>.Failure(
                "Catalog service is unavailable.", ResultErrorType.ServiceUnavailable));

        var request = new CreateOrderRequest("1 Test Street", [new CreateOrderItemRequest(Guid.NewGuid(), 1)]);

        // Act
        var result = await _orderingService.CreateOrderAsync(Guid.NewGuid(), request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorType.ServiceUnavailable, result.ErrorType);
        Assert.Empty(_dbContext.Orders);
    }

    [Fact]
    public async Task CreateOrderAsync_WithDuplicateProductLines_ShouldMergeQuantities()
    {
        // Arrange
        var shoe = CreateTestProduct(price: 25m);
        SetupCatalogProducts(shoe);

        var request = new CreateOrderRequest("1 Test Street",
        [
            new CreateOrderItemRequest(shoe.Id, 1),
            new CreateOrderItemRequest(shoe.Id, 2)
        ]);

        // Act
        var result = await _orderingService.CreateOrderAsync(Guid.NewGuid(), request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        var item = Assert.Single(result.Value.Order.Items);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(75m, result.Value.Order.TotalAmount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateOrderAsync_WithValidItems_ShouldStartFirstPaymentAttemptAndQueueOrderPlacedWithTraceParent(bool hasActiveTrace)
    {
        // Arrange
        Activity.Current = null;
        using var activity = hasActiveTrace ? new Activity("test-request").Start() : null;

        var customerId = Guid.NewGuid();
        var shoe = CreateTestProduct(name: "Running Shoe", price: 50m);
        SetupCatalogProducts(shoe);

        var request = new CreateOrderRequest("1 Test Street", [new CreateOrderItemRequest(shoe.Id, 2)], "pm_card_chargeDeclined");

        // Act
        var result = await _orderingService.CreateOrderAsync(customerId, request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);

        var order = await _dbContext.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(1, order.PaymentAttempts);

        var message = await _dbContext.OutboxMessages.SingleAsync();
        Assert.Equal(RoutingKeys.OrderPlaced, message.Type);
        Assert.Null(message.ProcessedAtUtc);
        Assert.Equal(activity?.Id, message.TraceParent);

        var orderPlaced = ReadOutboxEvent<OrderPlacedEvent>(message);
        Assert.Equal(result.Value.Order.Id, orderPlaced.OrderId);
        Assert.Equal(customerId, orderPlaced.CustomerId);
        Assert.Equal(100m, orderPlaced.TotalAmount);
        Assert.Equal("pm_card_chargeDeclined", orderPlaced.PaymentMethodId);
        var item = Assert.Single(orderPlaced.Items);
        Assert.Equal(shoe.Id, item.ProductId);
        Assert.Equal("Running Shoe", item.ProductName);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(50m, item.UnitPrice);
    }

    #endregion

    #region CancelOrderForCustomerAsync Tests

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Processing)]
    [InlineData(OrderStatus.PaymentFailed)]
    public async Task CancelOrderForCustomerAsync_WithCancellableStatus_ShouldCancelOrderAndAddOutboxMessage(OrderStatus status)
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var order = CreateTestOrder(customerId, status);
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _orderingService.CancelOrderForCustomerAsync(customerId, order.Id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(OrderStatus.Cancelled, result.Value.Status);

        var savedOrder = await _dbContext.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Cancelled, savedOrder.Status);

        var message = await _dbContext.OutboxMessages.SingleAsync();
        Assert.Equal(RoutingKeys.OrderCancelled, message.Type);

        var orderCancelled = ReadOutboxEvent<OrderCancelledEvent>(message);
        Assert.Equal(order.Id, orderCancelled.OrderId);
        Assert.Equal(customerId, orderCancelled.CustomerId);
        Assert.Equal(Roles.Customer, orderCancelled.CancelledBy);
    }

    [Theory]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Failed)]
    public async Task CancelOrderForCustomerAsync_WithNonCancellableStatus_ShouldReturnConflictAndAddNoOutboxMessage(OrderStatus status)
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var order = CreateTestOrder(customerId, status);
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _orderingService.CancelOrderForCustomerAsync(customerId, order.Id);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorType.Conflict, result.ErrorType);

        var savedOrder = await _dbContext.Orders.SingleAsync();
        Assert.Equal(status, savedOrder.Status);
        Assert.Empty(_dbContext.OutboxMessages);
    }

    [Fact]
    public async Task CancelOrderForCustomerAsync_WithOtherCustomersOrder_ShouldReturnNotFound()
    {
        // Arrange
        var order = CreateTestOrder(Guid.NewGuid());
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _orderingService.CancelOrderForCustomerAsync(Guid.NewGuid(), order.Id);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);

        var savedOrder = await _dbContext.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Pending, savedOrder.Status);
    }

    #endregion

    #region CancelOrderForAdminAsync Tests

    [Theory]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    public async Task CancelOrderForAdminAsync_WithAnyStatus_ShouldCancelOrderAndAddOutboxMessage(OrderStatus status)
    {
        // Arrange
        var order = CreateTestOrder(status: status);
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _orderingService.CancelOrderForAdminAsync(order.Id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(OrderStatus.Cancelled, result.Value.Status);

        var message = await _dbContext.OutboxMessages.SingleAsync();
        Assert.Equal(RoutingKeys.OrderCancelled, message.Type);
        Assert.Equal(Roles.Admin, ReadOutboxEvent<OrderCancelledEvent>(message).CancelledBy);
    }

    [Fact]
    public async Task CancelOrderForAdminAsync_WithNonExistentOrder_ShouldReturnNotFound()
    {
        // Act
        var result = await _orderingService.CancelOrderForAdminAsync(Guid.NewGuid());

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task CancelOrderForAdminAsync_WithAlreadyCancelledOrder_ShouldNotAddOutboxMessage()
    {
        // Arrange
        var order = CreateTestOrder(status: OrderStatus.Cancelled);
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _orderingService.CancelOrderForAdminAsync(order.Id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(OrderStatus.Cancelled, result.Value.Status);
        Assert.Empty(_dbContext.OutboxMessages);
    }

    #endregion

    #region UpdateOrderAsync Tests

    [Theory]
    [InlineData(OrderStatus.Delivered, OrderStatus.Pending, false)]
    [InlineData(OrderStatus.Pending, OrderStatus.Shipped, false)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled, true)]
    public async Task UpdateOrderAsync_WithAnyStatus_ShouldSetStatusAndAddOutboxMessageOnlyWhenCancelled(
        OrderStatus currentStatus, OrderStatus newStatus, bool expectsOutboxMessage)
    {
        // Arrange
        var order = CreateTestOrder(status: currentStatus);
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        var request = new AdminUpdateOrderRequest(Status: newStatus);

        // Act
        var result = await _orderingService.UpdateOrderAsync(order.Id, request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(newStatus, result.Value.Status);
        Assert.Equal("1 Test Street", result.Value.ShippingAddress);

        if (expectsOutboxMessage)
        {
            var message = await _dbContext.OutboxMessages.SingleAsync();
            Assert.Equal(RoutingKeys.OrderCancelled, message.Type);
            Assert.Equal(Roles.Admin, ReadOutboxEvent<OrderCancelledEvent>(message).CancelledBy);
        }
        else
        {
            Assert.Empty(_dbContext.OutboxMessages);
        }
    }

    [Fact]
    public async Task UpdateOrderAsync_WithOnlyShippingAddress_ShouldNotChangeStatus()
    {
        // Arrange
        var order = CreateTestOrder(status: OrderStatus.Confirmed);
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        var request = new AdminUpdateOrderRequest(ShippingAddress: "2 New Street");

        // Act
        var result = await _orderingService.UpdateOrderAsync(order.Id, request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(OrderStatus.Confirmed, result.Value.Status);
        Assert.Equal("2 New Street", result.Value.ShippingAddress);
    }

    [Fact]
    public async Task UpdateOrderAsync_WithNonExistentOrder_ShouldReturnNotFound()
    {
        // Arrange
        var request = new AdminUpdateOrderRequest(Status: OrderStatus.Shipped);

        // Act
        var result = await _orderingService.UpdateOrderAsync(Guid.NewGuid(), request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
    }

    #endregion

    #region GetOrderForCustomerAsync Tests

    [Fact]
    public async Task GetOrderForCustomerAsync_WithOwnOrder_ShouldReturnOrderWithItemsAndPaymentDetails()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var order = CreateTestOrder(customerId, OrderStatus.PaymentFailed);
        order.PaymentAttempts = 2;
        order.PaymentFailureReason = "Your card was declined.";
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _orderingService.GetOrderForCustomerAsync(customerId, order.Id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(order.Id, result.Value.Id);
        Assert.Equal("1 Test Street", result.Value.ShippingAddress);
        Assert.Single(result.Value.Items);
        Assert.Equal("Your card was declined.", result.Value.PaymentFailureReason);
        Assert.Equal(MaxRetries - 1, result.Value.PaymentRetriesRemaining);
    }

    [Fact]
    public async Task GetOrderForCustomerAsync_WithOtherCustomersOrder_ShouldReturnNotFound()
    {
        // Arrange
        var order = CreateTestOrder(Guid.NewGuid());
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _orderingService.GetOrderForCustomerAsync(Guid.NewGuid(), order.Id);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
    }

    #endregion

    #region GetOrdersForCustomerAsync Tests

    [Fact]
    public async Task GetOrdersForCustomerAsync_ShouldReturnOnlyCustomersOrders()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        _dbContext.Orders.AddRange(
            CreateTestOrder(customerId),
            CreateTestOrder(customerId),
            CreateTestOrder(Guid.NewGuid()));
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _orderingService.GetOrdersForCustomerAsync(customerId, new OrderQuery());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.All(result.Value.Items, o => Assert.Equal(customerId, o.UserId));
    }


    [Theory]
    [InlineData(2, 2)]
    [InlineData(10, 0)]
    public async Task GetOrdersForCustomerAsync_WithPageAndPageSize_ShouldReturnRequestedPageAndTotals(int page, int expectedItemCount)
    {
        // Arrange
        var customerId = Guid.NewGuid();
        _dbContext.Orders.AddRange(Enumerable.Range(1, 5).Select(_ => CreateTestOrder(customerId)));
        _dbContext.Orders.Add(CreateTestOrder(Guid.NewGuid()));
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _orderingService.GetOrdersForCustomerAsync(customerId, new OrderQuery(Page: page, PageSize: 2));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedItemCount, result.Value!.Items.Count);
        Assert.Equal(page, result.Value.Page);
        Assert.Equal(2, result.Value.PageSize);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(3, result.Value.TotalPages);
    }

    [Fact]
    public async Task GetOrdersForCustomerAsync_WithDifferentPages_ShouldNotReturnOverlappingItems()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        _dbContext.Orders.AddRange(Enumerable.Range(1, 5).Select(_ => CreateTestOrder(customerId)));
        _dbContext.Orders.Add(CreateTestOrder(Guid.NewGuid()));
        await _dbContext.SaveChangesAsync();

        // Act
        var first = await _orderingService.GetOrdersForCustomerAsync(customerId, new OrderQuery(Page: 1, PageSize: 3));
        var second = await _orderingService.GetOrdersForCustomerAsync(customerId, new OrderQuery(Page: 2, PageSize: 3));

        // Assert
        var ids = first.Value!.Items.Select(i => i.Id).Concat(second.Value!.Items.Select(i => i.Id)).ToList();
        Assert.Equal(5, ids.Count);
        Assert.Equal(5, ids.Distinct().Count());
    }

    #endregion

    #region GetOrdersForAdminAsync Tests

    [Fact]
    public async Task GetOrdersForAdminAsync_WithMaxTotalAmountFilter_ShouldReturnOrdersAtOrBelowMax()
    {
        // Arrange
        _dbContext.Orders.AddRange(
            CreateTestOrder(totalAmount: 50m),
            CreateTestOrder(totalAmount: 100m),
            CreateTestOrder(totalAmount: 150m));
        await _dbContext.SaveChangesAsync();

        var query = new OrderQuery(MinTotalAmount: 10m, MaxTotalAmount: 100m);

        // Act
        var result = await _orderingService.GetOrdersForAdminAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.All(result.Value.Items, o => Assert.True(o.TotalAmount <= 100m));
    }


    [Theory]
    [InlineData(2, 2)]
    [InlineData(10, 0)]
    public async Task GetOrdersForAdminAsync_WithPageAndPageSize_ShouldReturnRequestedPageAndTotals(int page, int expectedItemCount)
    {
        // Arrange
        _dbContext.Orders.AddRange(Enumerable.Range(1, 5).Select(_ => CreateTestOrder(Guid.NewGuid())));
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _orderingService.GetOrdersForAdminAsync(new OrderQuery(Page: page, PageSize: 2));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedItemCount, result.Value!.Items.Count);
        Assert.Equal(page, result.Value.Page);
        Assert.Equal(2, result.Value.PageSize);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(3, result.Value.TotalPages);
    }

    [Fact]
    public async Task GetOrdersForAdminAsync_WithDifferentPages_ShouldNotReturnOverlappingItems()
    {
        // Arrange
        _dbContext.Orders.AddRange(Enumerable.Range(1, 5).Select(_ => CreateTestOrder(Guid.NewGuid())));
        await _dbContext.SaveChangesAsync();

        // Act
        var first = await _orderingService.GetOrdersForAdminAsync(new OrderQuery(Page: 1, PageSize: 3));
        var second = await _orderingService.GetOrdersForAdminAsync(new OrderQuery(Page: 2, PageSize: 3));

        // Assert
        var ids = first.Value!.Items.Select(i => i.Id).Concat(second.Value!.Items.Select(i => i.Id)).ToList();
        Assert.Equal(5, ids.Count);
        Assert.Equal(5, ids.Distinct().Count());
    }

    #endregion

    private async Task<Order> SeedPaymentOrderAsync(OrderStatus status = OrderStatus.Pending, int paymentAttempts = 1, Guid? customerId = null)
    {
        var order = CreateTestOrder(customerId, status);
        order.PaymentAttempts = paymentAttempts;
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        return order;
    }

    #region ConfirmOrderPaymentAsync Tests

    [Fact]
    public async Task ConfirmOrderPaymentAsync_CurrentPendingAttempt_ShouldConfirmAndQueueOrderConfirmed()
    {
        // Arrange
        var order = await SeedPaymentOrderAsync(paymentAttempts: 2);

        // Act
        var result = await _orderingService.ConfirmOrderPaymentAsync(order.Id, 2);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);

        var saved = await _dbContext.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(OrderStatus.Confirmed, saved.Status);
        Assert.Null(saved.PaymentFailureReason);

        var message = await _dbContext.OutboxMessages.SingleAsync();
        Assert.Equal(RoutingKeys.OrderConfirmed, message.Type);
        var confirmed = ReadOutboxEvent<OrderConfirmedEvent>(message);
        Assert.Equal(order.Id, confirmed.OrderId);
        Assert.Equal(order.UserId, confirmed.CustomerId);
        Assert.Equal(order.TotalAmount, confirmed.TotalAmount);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, 2, 1)]
    [InlineData(OrderStatus.Confirmed, 1, 1)]
    [InlineData(OrderStatus.Cancelled, 1, 1)]
    public async Task ConfirmOrderPaymentAsync_StaleAttemptOrOrderNotPending_ShouldIgnoreResult(
        OrderStatus status, int currentAttempt, int reportedAttempt)
    {
        // Arrange
        var order = await SeedPaymentOrderAsync(status, paymentAttempts: currentAttempt);

        // Act
        var result = await _orderingService.ConfirmOrderPaymentAsync(order.Id, reportedAttempt);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        Assert.Equal(status, (await _dbContext.Orders.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(_dbContext.OutboxMessages);
    }

    [Fact]
    public async Task ConfirmOrderPaymentAsync_UnknownOrder_ShouldReturnNotFound()
    {
        // Act
        var result = await _orderingService.ConfirmOrderPaymentAsync(Guid.NewGuid(), 1);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
    }

    #endregion

    #region FailOrderPaymentAsync Tests

    [Theory]
    [InlineData(MaxRetries, 1, OrderStatus.PaymentFailed, MaxRetries)]
    [InlineData(MaxRetries, MaxRetries + 1, OrderStatus.Failed, 0)]
    [InlineData(0, 1, OrderStatus.Failed, 0)]
    public async Task FailOrderPaymentAsync_CurrentAttempt_ShouldSetStatusFromRemainingRetriesAndQueueEvent(
        int maxRetries, int attempt, OrderStatus expectedStatus, int expectedRetriesRemaining)
    {
        // Arrange
        var orderingService = CreateService(maxRetries);
        var order = await SeedPaymentOrderAsync(paymentAttempts: attempt);

        // Act
        var result = await orderingService.FailOrderPaymentAsync(order.Id, attempt, "Your card was declined.");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);

        var saved = await _dbContext.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(expectedStatus, saved.Status);
        Assert.Equal("Your card was declined.", saved.PaymentFailureReason);

        var message = await _dbContext.OutboxMessages.SingleAsync();
        Assert.Equal(RoutingKeys.OrderPaymentFailed, message.Type);
        var failed = ReadOutboxEvent<OrderPaymentFailedEvent>(message);
        Assert.Equal(order.Id, failed.OrderId);
        Assert.Equal("Your card was declined.", failed.Reason);
        Assert.Equal(expectedRetriesRemaining, failed.RetriesRemaining);
    }

    [Fact]
    public async Task FailOrderPaymentAsync_ReasonTooLong_ShouldTruncateToColumnLength()
    {
        // Arrange
        var order = await SeedPaymentOrderAsync(paymentAttempts: 1);

        // Act
        var result = await _orderingService.FailOrderPaymentAsync(order.Id, 1, new string('x', OrderFieldLengths.PaymentFailureReason + 50));

        // Assert
        Assert.True(result.IsSuccess);
        var saved = await _dbContext.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(OrderFieldLengths.PaymentFailureReason, saved.PaymentFailureReason!.Length);
    }

    [Fact]
    public async Task FailOrderPaymentAsync_StaleAttempt_ShouldIgnoreResult()
    {
        // Arrange
        var order = await SeedPaymentOrderAsync(paymentAttempts: 3);

        // Act
        var result = await _orderingService.FailOrderPaymentAsync(order.Id, 2, "Declined");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        Assert.Equal(OrderStatus.Pending, (await _dbContext.Orders.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(_dbContext.OutboxMessages);
    }

    [Fact]
    public async Task FailOrderPaymentAsync_UnknownOrder_ShouldReturnNotFound()
    {
        // Act
        var result = await _orderingService.FailOrderPaymentAsync(Guid.NewGuid(), 1, "Declined");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
    }

    #endregion

    #region RetryOrderPaymentAsync Tests

    [Fact]
    public async Task RetryOrderPaymentAsync_PaymentFailedOrder_ShouldStartNextAttemptAndQueueEvent()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var order = await SeedPaymentOrderAsync(OrderStatus.PaymentFailed, paymentAttempts: 1, customerId: customerId);

        // Act
        var result = await _orderingService.RetryOrderPaymentAsync(customerId, order.Id, new RetryOrderPaymentRequest("pm_card_visa"));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Pending, result.Value!.Status);
        Assert.Null(result.Value.PaymentFailureReason);

        var saved = await _dbContext.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(OrderStatus.Pending, saved.Status);
        Assert.Equal(2, saved.PaymentAttempts);

        var message = await _dbContext.OutboxMessages.SingleAsync();
        Assert.Equal(RoutingKeys.OrderPaymentRetried, message.Type);
        var retried = ReadOutboxEvent<OrderPaymentRetriedEvent>(message);
        Assert.Equal(order.Id, retried.OrderId);
        Assert.Equal(2, retried.Attempt);
        Assert.Equal(order.TotalAmount, retried.TotalAmount);
        Assert.Equal("pm_card_visa", retried.PaymentMethodId);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, 1)]
    [InlineData(OrderStatus.Confirmed, 1)]
    [InlineData(OrderStatus.Failed, 1)]
    [InlineData(OrderStatus.PaymentFailed, MaxRetries + 1)]
    public async Task RetryOrderPaymentAsync_OrderNotRetryable_ShouldReturnConflictAndAddNoOutboxMessage(
        OrderStatus status, int paymentAttempts)
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var order = await SeedPaymentOrderAsync(status, paymentAttempts, customerId);

        // Act
        var result = await _orderingService.RetryOrderPaymentAsync(customerId, order.Id, new RetryOrderPaymentRequest("pm_card_visa"));

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
        Assert.Empty(_dbContext.OutboxMessages);
    }

    [Fact]
    public async Task RetryOrderPaymentAsync_OtherCustomersOrder_ShouldReturnNotFound()
    {
        // Arrange
        var order = await SeedPaymentOrderAsync(OrderStatus.PaymentFailed);

        // Act
        var result = await _orderingService.RetryOrderPaymentAsync(Guid.NewGuid(), order.Id, new RetryOrderPaymentRequest("pm_card_visa"));

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
    }

    #endregion
}
