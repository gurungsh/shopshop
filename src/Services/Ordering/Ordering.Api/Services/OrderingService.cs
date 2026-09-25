using Common.Core;
using Microsoft.EntityFrameworkCore;
using Ordering.Api.DTOs;
using Ordering.Infrastructure.Constants;
using Ordering.Infrastructure.Data;
using Ordering.Infrastructure.Models;

namespace Ordering.Api.Services
{
    public class OrderingService : IOrderingService
    {
        private static readonly OrderStatus[] CustomerCancellableStatuses =
        [
            OrderStatus.Pending,
            OrderStatus.Confirmed,
            OrderStatus.Processing
        ];

        private readonly OrderingDbContext _dbContext;
        private readonly ICatalogServiceClient _catalogServiceClient;
        private readonly ILogger<OrderingService> _logger;

        public OrderingService(
            OrderingDbContext dbContext,
            ICatalogServiceClient catalogServiceClient,
            ILogger<OrderingService> logger)
        {
            _dbContext = dbContext;
            _catalogServiceClient = catalogServiceClient;
            _logger = logger;
        }

        public async Task<Result<List<OrderResponse>>> GetOrdersForCustomerAsync(Guid customerId, OrderQuery query)
        {
            var orders = _dbContext.Orders.AsNoTracking().Where(o => o.UserId == customerId);

            var result = await ApplyGetOrdersFilters(orders, query)
                .Select(o => new OrderResponse(
                    o.Id,
                    o.UserId,
                    o.TotalAmount,
                    o.Status,
                    o.CreatedAtUtc,
                    o.UpdatedAtUtc))
                .ToListAsync();

            return Result<List<OrderResponse>>.Success(result);
        }

        public async Task<Result<List<OrderResponse>>> GetOrdersForAdminAsync(OrderQuery query)
        {
            var orders = _dbContext.Orders.AsNoTracking();

            var result = await ApplyGetOrdersFilters(orders, query)
                .Select(o => new OrderResponse(
                    o.Id,
                    o.UserId,
                    o.TotalAmount,
                    o.Status,
                    o.CreatedAtUtc,
                    o.UpdatedAtUtc))
                .ToListAsync();

            return Result<List<OrderResponse>>.Success(result);
        }

        public async Task<Result<OrderDetailResponse>> GetOrderForCustomerAsync(Guid customerId, Guid id)
        {
            var order = await _dbContext.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order is null)
            {
                _logger.LogWarning("Order not found. Order Id:{OrderId}", id);
                return Result<OrderDetailResponse>.Failure("Order not found.", ResultErrorType.NotFound);
            }

            if (order.UserId != customerId)
            {
                _logger.LogWarning("Customer tried to access invalid order. Customer Id:{CustomerId}, Order Id:{OrderId}", customerId, id);
                return Result<OrderDetailResponse>.Failure("Order not found.", ResultErrorType.NotFound);
            }

            return Result<OrderDetailResponse>.Success(ToDetailResponse(order));
        }

        public async Task<Result<OrderDetailResponse>> GetOrderForAdminAsync(Guid id)
        {
            var order = await _dbContext.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order is null)
            {
                _logger.LogWarning("Order not found. Order Id:{OrderId}", id);
                return Result<OrderDetailResponse>.Failure("Order not found.", ResultErrorType.NotFound);
            }

            return Result<OrderDetailResponse>.Success(ToDetailResponse(order));
        }

        public async Task<Result<CreateOrderResponse>> CreateOrderAsync(Guid customerId, CreateOrderRequest request)
        {
            // Merge duplicate product lines
            var requestedItems = request.Items
                .GroupBy(i => i.ProductId)
                .Select(g => new CreateOrderItemRequest(g.Key, g.Sum(i => i.Quantity)))
                .ToList();

            var productsResult = await _catalogServiceClient.GetProductsAsync(requestedItems.Select(i => i.ProductId));
            if (!productsResult.IsSuccess)
            {
                return Result<CreateOrderResponse>.Failure(productsResult.Error!, productsResult.ErrorType);
            }

            var products = productsResult.Value!;

            var validItems = requestedItems
                .Where(i => products.TryGetValue(i.ProductId, out var product) && product.IsActive)
                .ToList();

            var skippedProductIds = requestedItems
                .Where(i => !validItems.Contains(i))
                .Select(i => i.ProductId)
                .ToArray();

            if (validItems.Count == 0)
            {
                _logger.LogWarning("Order creation failed: no available products. Customer Id:{CustomerId}", customerId);
                return Result<CreateOrderResponse>.Failure("None of the requested products are available.", ResultErrorType.BadRequest);
            }

            var order = new Order
            {
                UserId = customerId,
                Status = OrderStatus.Pending,
                ShippingAddress = request.ShippingAddress,
                Items = validItems
                    .Select(i => new OrderItem
                    {
                        ProductId = i.ProductId,
                        ProductName = products[i.ProductId].Name,
                        UnitPrice = products[i.ProductId].Price,
                        Quantity = i.Quantity
                    })
                    .ToList()
            };

            order.TotalAmount = order.Items.Sum(i => i.TotalPrice);

            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();

            if (skippedProductIds.Length > 0)
            {
                _logger.LogWarning("Order created with skipped products. Order Id:{OrderId}, Skipped Product Ids:{SkippedProductIds}", order.Id, skippedProductIds);
            }

            _logger.LogInformation("Order created. Order Id:{OrderId}, Customer Id:{CustomerId}, Total Amount:{TotalAmount}", order.Id, customerId, order.TotalAmount);

            return Result<CreateOrderResponse>.Success(new CreateOrderResponse(ToDetailResponse(order), skippedProductIds));
        }

        public async Task<Result<OrderDetailResponse>> UpdateOrderAsync(Guid id, AdminUpdateOrderRequest request)
        {
            var order = await _dbContext.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order is null)
            {
                _logger.LogWarning("Order not found. Order Id:{OrderId}", id);
                return Result<OrderDetailResponse>.Failure("Order not found.", ResultErrorType.NotFound);
            }

            if (request.Status.HasValue)
            {
                order.Status = request.Status.Value;
            }

            if (!string.IsNullOrWhiteSpace(request.ShippingAddress))
            {
                order.ShippingAddress = request.ShippingAddress;
            }

            order.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Order updated. Order Id:{OrderId}, Status:{Status}", order.Id, order.Status);

            return Result<OrderDetailResponse>.Success(ToDetailResponse(order));
        }

        public async Task<Result<OrderDetailResponse>> CancelOrderForCustomerAsync(Guid customerId, Guid id)
        {
            var order = await _dbContext.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order is null)
            {
                _logger.LogWarning("Order not found. Order Id:{OrderId}", id);
                return Result<OrderDetailResponse>.Failure("Order not found.", ResultErrorType.NotFound);
            }

            if (order.UserId != customerId)
            {
                _logger.LogWarning("Customer tried to cancel invalid order. Customer Id:{CustomerId}, Order Id:{OrderId}", customerId, id);
                return Result<OrderDetailResponse>.Failure("Order not found.", ResultErrorType.NotFound);
            }

            if (!CustomerCancellableStatuses.Contains(order.Status))
            {
                _logger.LogWarning("Order can no longer be cancelled. Order Id:{OrderId}, Status:{Status}", id, order.Status);
                return Result<OrderDetailResponse>.Failure("Order can no longer be cancelled.", ResultErrorType.Conflict);
            }

            order.Status = OrderStatus.Cancelled;
            order.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Order cancelled by customer. Order Id:{OrderId}, Customer Id:{CustomerId}", order.Id, customerId);

            return Result<OrderDetailResponse>.Success(ToDetailResponse(order));
        }

        public async Task<Result<OrderDetailResponse>> CancelOrderForAdminAsync(Guid id)
        {
            var order = await _dbContext.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order is null)
            {
                _logger.LogWarning("Order not found. Order Id:{OrderId}", id);
                return Result<OrderDetailResponse>.Failure("Order not found.", ResultErrorType.NotFound);
            }

            order.Status = OrderStatus.Cancelled;
            order.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Order cancelled by admin. Order Id:{OrderId}", order.Id);

            return Result<OrderDetailResponse>.Success(ToDetailResponse(order));
        }

        private static OrderDetailResponse ToDetailResponse(Order order)
        {
            return new OrderDetailResponse(
                order.Id,
                order.UserId,
                order.TotalAmount,
                order.Status,
                order.ShippingAddress,
                order.Items
                    .Select(i => new OrderItemResponse(
                        i.Id,
                        i.ProductId,
                        i.ProductName,
                        i.UnitPrice,
                        i.Quantity,
                        i.TotalPrice))
                    .ToArray(),
                order.CreatedAtUtc,
                order.UpdatedAtUtc);
        }

        private static IQueryable<Order> ApplyGetOrdersFilters(IQueryable<Order> orders, OrderQuery query)
        {
            if (query.Ids?.Length > 0)
            {
                orders = orders.Where(o => query.Ids.Contains(o.Id));
            }

            if (query.UserIds?.Length > 0)
            {
                orders = orders.Where(o => query.UserIds.Contains(o.UserId));
            }

            if (query.Statuses?.Length > 0)
            {
                orders = orders.Where(o => query.Statuses.Contains(o.Status));
            }

            if (query.MinTotalAmount.HasValue)
            {
                orders = orders.Where(o => o.TotalAmount >= query.MinTotalAmount);
            }

            if (query.MaxTotalAmount.HasValue)
            {
                orders = orders.Where(o => o.TotalAmount <= query.MaxTotalAmount);
            }

            if (query.CreatedBeforeUtc.HasValue)
            {
                orders = orders.Where(o => o.CreatedAtUtc <= query.CreatedBeforeUtc.Value);
            }

            if (query.CreatedAfterUtc.HasValue)
            {
                orders = orders.Where(o => o.CreatedAtUtc >= query.CreatedAfterUtc.Value);
            }

            if (query.UpdatedBeforeUtc.HasValue)
            {
                orders = orders.Where(o => o.UpdatedAtUtc <= query.UpdatedBeforeUtc.Value);
            }

            if (query.UpdatedAfterUtc.HasValue)
            {
                orders = orders.Where(o => o.UpdatedAtUtc >= query.UpdatedAfterUtc.Value);
            }

            if (query.ProductIds is { Length: > 0 })
            {
                orders = orders.Where(o => o.Items.Any(i => query.ProductIds.Contains(i.ProductId)));
            }

            if (query.MinItemCount.HasValue)
            {
                orders = orders.Where(o => o.Items.Count >= query.MinItemCount.Value);
            }

            if (query.MaxItemCount.HasValue)
            {
                orders = orders.Where(o => o.Items.Count <= query.MaxItemCount.Value);
            }

            return orders.OrderByDescending(o => o.CreatedAtUtc);
        }
    }

}
