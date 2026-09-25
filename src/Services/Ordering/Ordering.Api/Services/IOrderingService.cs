using Common.Core;
using Ordering.Api.DTOs;

namespace Ordering.Api.Services
{
    public interface IOrderingService
    {
        Task<Result<List<OrderResponse>>> GetOrdersForCustomerAsync(Guid customerId, OrderQuery query);
        Task<Result<List<OrderResponse>>> GetOrdersForAdminAsync(OrderQuery query);
        Task<Result<OrderDetailResponse>> GetOrderForCustomerAsync(Guid customerId, Guid id);
        Task<Result<OrderDetailResponse>> GetOrderForAdminAsync(Guid id);
        Task<Result<CreateOrderResponse>> CreateOrderAsync(Guid customerId, CreateOrderRequest request);
        Task<Result<OrderDetailResponse>> UpdateOrderAsync(Guid id, AdminUpdateOrderRequest request);
        Task<Result<OrderDetailResponse>> CancelOrderForCustomerAsync(Guid customerId, Guid id);
        Task<Result<OrderDetailResponse>> CancelOrderForAdminAsync(Guid id);
    }
}
