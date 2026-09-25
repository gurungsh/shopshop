using Ordering.Infrastructure.Constants;

namespace Ordering.Api.DTOs
{
    public sealed record OrderQuery(
        Guid[]? Ids = null,
        Guid[]? UserIds = null,
        OrderStatus[]? Statuses = null,
        decimal? MinTotalAmount = null,
        decimal? MaxTotalAmount = null,
        DateTime? CreatedBeforeUtc = null,
        DateTime? CreatedAfterUtc = null,
        DateTime? UpdatedBeforeUtc = null,
        DateTime? UpdatedAfterUtc = null,
        Guid[]? ProductIds = null,
        int? MinItemCount = null,
        int? MaxItemCount = null
        );
    public sealed record OrderResponse(
        Guid Id,
        Guid UserId,
        decimal TotalAmount,
        OrderStatus Status,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);
    public sealed record OrderItemResponse(
        Guid Id,
        Guid ProductId,
        string ProductName,
        decimal UnitPrice,
        int Quantity,
        decimal TotalPrice);
    public sealed record OrderDetailResponse(
        Guid Id,
        Guid UserId,
        decimal TotalAmount,
        OrderStatus Status,
        string ShippingAddress,
        OrderItemResponse[] Items,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);
    public sealed record CreateOrderResponse(
        OrderDetailResponse Order,
        Guid[] SkippedProductIds);
    public sealed record CreateOrderRequest(
        string ShippingAddress,
        CreateOrderItemRequest[] Items);
    public sealed record AdminUpdateOrderRequest(
        OrderStatus? Status = null,
        string? ShippingAddress = null);
    public sealed record CreateOrderItemRequest(
        Guid ProductId,
        int Quantity);
}
