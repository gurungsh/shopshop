using Common.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Ordering.Api.DTOs;
using Ordering.Api.Services;
using Ordering.Infrastructure.Constants;
using Ordering.Infrastructure.Data;
using Ordering.Infrastructure.Models;

namespace Ordering.Api.UnitTests
{
    public class OrderingServiceTests
    {
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

            _orderingService = new OrderingService(_dbContext, _mockCatalogClient.Object, _mockLogger.Object);
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
        public async Task CreateOrderAsync_WithNoValidItems_ShouldReturnBadRequest()
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

        #endregion

        #region CancelOrderForCustomerAsync Tests

        [Theory]
        [InlineData(OrderStatus.Pending)]
        [InlineData(OrderStatus.Confirmed)]
        [InlineData(OrderStatus.Processing)]
        public async Task CancelOrderForCustomerAsync_WithCancellableStatus_ShouldCancelOrder(OrderStatus status)
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
        }

        [Theory]
        [InlineData(OrderStatus.Shipped)]
        [InlineData(OrderStatus.Delivered)]
        [InlineData(OrderStatus.Cancelled)]
        public async Task CancelOrderForCustomerAsync_WithNonCancellableStatus_ShouldReturnConflict(OrderStatus status)
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
        public async Task CancelOrderForAdminAsync_WithAnyStatus_ShouldCancelOrder(OrderStatus status)
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

        #endregion

        #region UpdateOrderAsync Tests

        [Fact]
        public async Task UpdateOrderAsync_WithAnyStatus_ShouldSetStatus()
        {
            // Arrange
            var order = CreateTestOrder(status: OrderStatus.Delivered);
            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();

            var request = new AdminUpdateOrderRequest(Status: OrderStatus.Pending);

            // Act
            var result = await _orderingService.UpdateOrderAsync(order.Id, request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(OrderStatus.Pending, result.Value.Status);
            Assert.Equal("1 Test Street", result.Value.ShippingAddress);
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
        public async Task GetOrderForCustomerAsync_WithOwnOrder_ShouldReturnOrderWithItems()
        {
            // Arrange
            var customerId = Guid.NewGuid();
            var order = CreateTestOrder(customerId);
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
            Assert.Equal(2, result.Value.Count);
            Assert.All(result.Value, o => Assert.Equal(customerId, o.UserId));
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
            Assert.Equal(2, result.Value.Count);
            Assert.All(result.Value, o => Assert.True(o.TotalAmount <= 100m));
        }

        #endregion
    }
}
