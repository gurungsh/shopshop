using Common.Core;
using Common.Web;
using Ordering.Api.DTOs;
using Ordering.Api.Filters;
using Ordering.Api.Services;

namespace Ordering.Api.Endpoints
{
    public static class AdminOrderEndpoints
    {
        public static IEndpointRouteBuilder MapAdminOrderEndpoints(
            this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/admin/orders")
                .RequireAuthorization(policy => policy.RequireRole(Roles.Admin))
                .WithTags("Admin");

            // POST /api/admin/orders/search
            group.MapPost("/search", async (
                OrderQuery query,
                IOrderingService orderingService) =>
            {
                var result = await orderingService.GetOrdersForAdminAsync(query);

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("AdminSearchOrders");

            // GET /api/admin/orders/{id}
            group.MapGet("/{id:guid}", async (
                Guid id,
                IOrderingService orderingService) =>
            {
                var result = await orderingService.GetOrderForAdminAsync(id);

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("AdminGetOrder");

            // PUT /api/admin/orders/{id}/status
            group.MapPut("/{id:guid}/status", async (
                Guid id,
                AdminUpdateOrderRequest request,
                IOrderingService orderingService) =>
            {
                var result = await orderingService.UpdateOrderAsync(id, request);

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("AdminUpdateOrder")
            .AddEndpointFilter<ValidationFilter<AdminUpdateOrderRequest>>();

            // POST /api/admin/orders/{id}/cancel
            group.MapPost("/{id:guid}/cancel", async (
                Guid id,
                IOrderingService orderingService) =>
            {
                var result = await orderingService.CancelOrderForAdminAsync(id);

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("AdminCancelOrder");

            return app;
        }
    }
}
