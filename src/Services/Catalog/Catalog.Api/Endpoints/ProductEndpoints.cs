using Catalog.Api.DTOs;
using Catalog.Api.Services;
using Common.Web;

namespace Catalog.Api.Endpoints
{
    public static class ProductEndpoints
    {
        public static IEndpointRouteBuilder MapProductEndpoints(
            this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/products")
                .WithTags("Product");

            // POST /api/products/search
            group.MapPost("/search", async (
                ProductQuery query,
                IProductService productService) =>
            {
                var result = await productService.GetProductsAsync(query);

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("GetProducts");

            // GET /api/products/{id}
            group.MapGet("/{id:guid}", async (
                Guid id,
                IProductService productService) =>
            {
                var result = await productService.GetProductAsync(id);

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("GetProduct");

            return app;
        }
    }
}
