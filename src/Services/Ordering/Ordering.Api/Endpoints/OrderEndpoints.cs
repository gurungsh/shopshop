using System.Security.Claims;
using Common.Web;
using Ordering.Api.DTOs;
using Ordering.Api.Extensions;
using Ordering.Api.Filters;
using Ordering.Api.Services;

namespace Ordering.Api.Endpoints
{
    public static class OrderEndpoints
    {
        public static IEndpointRouteBuilder MapOrderEndpoints(
            this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/orders")
                .RequireAuthorization()
                .WithTags("Orders");

            // POST /api/orders
            group.MapPost("", async (
                CreateOrderRequest request,
                ClaimsPrincipal principal,
                IOrderingService orderingService) =>
            {
                var caller = principal.ToCallerContext();
                var result = await orderingService.CreateOrderAsync(caller.UserId, request);

                return result.IsSuccess
                    ? Results.Created(
                        $"/api/orders/{result.Value!.Order.Id}",
                        result.Value)
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("CreateOrder")
            .AddEndpointFilter<ValidationFilter<CreateOrderRequest>>();

            // POST /api/orders/search
            group.MapPost("/search", async (
                OrderQuery query,
                ClaimsPrincipal principal,
                IOrderingService orderingService) =>
            {
                var caller = principal.ToCallerContext();
                var result = await orderingService.GetOrdersForCustomerAsync(caller.UserId, query);

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("GetOrders");

            // GET /api/orders/{id}
            group.MapGet("/{id:guid}", async (
                Guid id,
                ClaimsPrincipal principal,
                IOrderingService orderingService) =>
            {
                var caller = principal.ToCallerContext();
                var result = await orderingService.GetOrderForCustomerAsync(caller.UserId, id);

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("GetOrder");

            // POST /api/orders/{id}/cancel
            group.MapPost("/{id:guid}/cancel", async (
                Guid id,
                ClaimsPrincipal principal,
                IOrderingService orderingService) =>
            {
                var caller = principal.ToCallerContext();
                var result = await orderingService.CancelOrderForCustomerAsync(caller.UserId, id);

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("CancelOrder");

            return app;
        }
    }
}
