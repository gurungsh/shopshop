using BuildingBlocks.Contracts.Orders;
using BuildingBlocks.Core;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ordering.Api.DTOs;
using Ordering.Api.Options;
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
            OrderStatus.Processing,
            OrderStatus.PaymentFailed
        ];

        private readonly OrderingDbContext _dbContext;
        private readonly ICatalogServiceClient _catalogServiceClient;
        private readonly PaymentOptions _paymentOptions;
        private readonly ILogger<OrderingService> _logger;

        public OrderingService(
            OrderingDbContext dbContext,
            ICatalogServiceClient catalogServiceClient,
            IOptions<PaymentOptions> paymentOptions,
            ILogger<OrderingService> logger)
        {
            _dbContext = dbContext;
            _catalogServiceClient = catalogServiceClient;
            _paymentOptions = paymentOptions.Value;
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
                PaymentAttempts = 1,
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
            AddOutboxMessage(RoutingKeys.OrderPlaced, new OrderPlacedEvent(
                order.Id,
                order.UserId,
                order.TotalAmount,
                order.Items
                .Select(i => new OrderPlacedItem(
                    i.ProductId,
                    i.ProductName,
                    i.Quantity,
                    i.UnitPrice))
                .ToList(),
                order.CreatedAtUtc,
                request.PaymentMethodId));

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
                var previousStatus = order.Status;

                order.Status = request.Status.Value;

                if (order.Status == OrderStatus.Cancelled && previousStatus != OrderStatus.Cancelled)
                {
                    AddOrderCancelledOutboxMessage(order, Roles.Admin);
                }
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

            if (order.Status != OrderStatus.Cancelled)
            {
                order.Status = OrderStatus.Cancelled;
                AddOrderCancelledOutboxMessage(order, Roles.Customer);
            }
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

            if (order.Status != OrderStatus.Cancelled)
            {
                order.Status = OrderStatus.Cancelled;
                AddOrderCancelledOutboxMessage(order, Roles.Admin);
            }
            order.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Order cancelled by admin. Order Id:{OrderId}", order.Id);

            return Result<OrderDetailResponse>.Success(ToDetailResponse(order));
        }

        public async Task<Result<OrderDetailResponse>> RetryOrderPaymentAsync(Guid customerId, Guid id, RetryOrderPaymentRequest request)
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
                _logger.LogWarning("Customer tried to retry payment for invalid order. Customer Id:{CustomerId}, Order Id:{OrderId}", customerId, id);
                return Result<OrderDetailResponse>.Failure("Order not found.", ResultErrorType.NotFound);
            }

            if (order.Status != OrderStatus.PaymentFailed || GetPaymentRetriesRemaining(order) <= 0)
            {
                _logger.LogWarning("Order payment can't be retried. Order Id:{OrderId}, Status:{Status}, Payment Attempts:{PaymentAttempts}", id, order.Status, order.PaymentAttempts);
                return Result<OrderDetailResponse>.Failure("Payment can only be retried after a failed payment, while retries remain.", ResultErrorType.Conflict);
            }

            order.PaymentAttempts++;
            order.Status = OrderStatus.Pending;
            order.PaymentFailureReason = null;
            order.UpdatedAtUtc = DateTime.UtcNow;

            AddOutboxMessage(RoutingKeys.OrderPaymentRetried, new OrderPaymentRetriedEvent(
                order.Id,
                order.UserId,
                order.TotalAmount,
                order.PaymentAttempts,
                request.PaymentMethodId,
                order.UpdatedAtUtc));

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Order payment retried. Order Id:{OrderId}, Attempt:{Attempt}", order.Id, order.PaymentAttempts);

            return Result<OrderDetailResponse>.Success(ToDetailResponse(order));
        }

        public async Task<Result<bool>> ConfirmOrderPaymentAsync(Guid orderId, int attempt)
        {
            var order = await _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == orderId);

            if (order is null)
            {
                _logger.LogWarning("Payment succeeded for an unknown order. Order Id:{OrderId}", orderId);
                return Result<bool>.Failure("Order not found.", ResultErrorType.NotFound);
            }

            if (!IsCurrentPendingAttempt(order, attempt))
            {
                return Result<bool>.Success(false);
            }

            order.Status = OrderStatus.Confirmed;
            order.PaymentFailureReason = null;
            order.UpdatedAtUtc = DateTime.UtcNow;

            AddOutboxMessage(RoutingKeys.OrderConfirmed, new OrderConfirmedEvent(
                order.Id,
                order.UserId,
                order.TotalAmount,
                order.UpdatedAtUtc));

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Order confirmed after payment. Order Id:{OrderId}, Attempt:{Attempt}", order.Id, attempt);

            return Result<bool>.Success(true);
        }

        public async Task<Result<bool>> FailOrderPaymentAsync(Guid orderId, int attempt, string reason)
        {
            var order = await _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == orderId);

            if (order is null)
            {
                _logger.LogWarning("Payment failed for an unknown order. Order Id:{OrderId}", orderId);
                return Result<bool>.Failure("Order not found.", ResultErrorType.NotFound);
            }

            if (!IsCurrentPendingAttempt(order, attempt))
            {
                return Result<bool>.Success(false);
            }

            var retriesRemaining = GetPaymentRetriesRemaining(order);

            order.Status = retriesRemaining > 0 ? OrderStatus.PaymentFailed : OrderStatus.Failed;
            order.PaymentFailureReason = reason.Length > OrderFieldLengths.PaymentFailureReason
                ? reason[..OrderFieldLengths.PaymentFailureReason]
                : reason;
            order.UpdatedAtUtc = DateTime.UtcNow;

            AddOutboxMessage(RoutingKeys.OrderPaymentFailed, new OrderPaymentFailedEvent(
                order.Id,
                order.UserId,
                order.PaymentFailureReason,
                retriesRemaining,
                order.UpdatedAtUtc));

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Order payment failed. Order Id:{OrderId}, Attempt:{Attempt}, Status:{Status}, Retries Remaining:{RetriesRemaining}", order.Id, attempt, order.Status, retriesRemaining);

            return Result<bool>.Success(true);
        }

        private bool IsCurrentPendingAttempt(Order order, int attempt)
        {
            if (attempt != order.PaymentAttempts || order.Status != OrderStatus.Pending)
            {
                _logger.LogWarning("Ignoring payment result. Order Id:{OrderId}, Attempt:{Attempt}, Current Attempt:{CurrentAttempt}, Status:{Status}", order.Id, attempt, order.PaymentAttempts, order.Status);
                return false;
            }

            return true;
        }

        private int GetPaymentRetriesRemaining(Order order)
            => Math.Max(0, _paymentOptions.MaxRetries - (order.PaymentAttempts - 1));

        private void AddOutboxMessage<TEvent>(string routingKey, TEvent @event)
        {
            _dbContext.OutboxMessages.Add(new OutboxMessage
            {
                Type = routingKey,
                Payload = MessageSerializer.Serialize(@event)
            });
        }

        private void AddOrderCancelledOutboxMessage(Order order, string cancelledBy)
        {
            AddOutboxMessage(RoutingKeys.OrderCancelled, new OrderCancelledEvent(
                order.Id,
                order.UserId,
                cancelledBy,
                order.UpdatedAtUtc));
        }

        private OrderDetailResponse ToDetailResponse(Order order)
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
                order.UpdatedAtUtc,
                order.PaymentFailureReason,
                order.Status == OrderStatus.PaymentFailed ? GetPaymentRetriesRemaining(order) : 0);
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
