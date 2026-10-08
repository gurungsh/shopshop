using BuildingBlocks.Core;
using Ordering.Api.DTOs;

namespace Ordering.Api.Services
{
    public interface IOrderingService
    {
        Task<Result<PagedResponse<OrderResponse>>> GetOrdersForCustomerAsync(Guid customerId, OrderQuery query);
        Task<Result<PagedResponse<OrderResponse>>> GetOrdersForAdminAsync(OrderQuery query);
        Task<Result<OrderDetailResponse>> GetOrderForCustomerAsync(Guid customerId, Guid id);
        Task<Result<OrderDetailResponse>> GetOrderForAdminAsync(Guid id);
        Task<Result<CreateOrderResponse>> CreateOrderAsync(Guid customerId, CreateOrderRequest request);
        Task<Result<OrderDetailResponse>> UpdateOrderAsync(Guid id, AdminUpdateOrderRequest request);
        Task<Result<OrderDetailResponse>> CancelOrderForCustomerAsync(Guid customerId, Guid id);
        Task<Result<OrderDetailResponse>> CancelOrderForAdminAsync(Guid id);
        Task<Result<OrderDetailResponse>> RetryOrderPaymentAsync(Guid customerId, Guid id, RetryOrderPaymentRequest request);
        Task<Result<bool>> ConfirmOrderPaymentAsync(Guid orderId, int attempt);
        Task<Result<bool>> FailOrderPaymentAsync(Guid orderId, int attempt, string reason);
    }
}
