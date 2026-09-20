using Catalog.Api.DTOs;
using Catalog.Api.Services;
using Common.Core;
using Common.Web;

namespace Catalog.Api.Endpoints
{
    public static class AdminProductEndpoints
    {
        public static IEndpointRouteBuilder MapAdminProductEndpoints(
            this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/admin/products")
                .RequireAuthorization(policy => policy.RequireRole(Roles.Admin))
                .WithTags("Admin");

            // POST /api/admin/products
            group.MapPost("", async (
                AdminCreateProductRequest request,
                IProductService productService) =>
            {
                var result = await productService.CreateProductAsync(request);

                return result.IsSuccess
                    ? Results.Created(
                        $"/api/admin/products/{result.Value!.Id}",
                        result.Value)
                    : EndpointResults.ToHttpResult(result);

            })
            .WithName("CreateProduct");

            // PUT /api/admin/products/{id}
            group.MapPut("/{id:guid}", async (
                Guid id,
                AdminUpdateProductRequest request,
                IProductService productService) =>
            {
                var result = await productService.UpdateProductAsync(id, request);

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : EndpointResults.ToHttpResult(result);

            })
            .WithName("UpdateProduct");

            // DELETE /api/admin/products/{id}
            group.MapDelete("/{id:guid}", async (
                Guid id,
                IProductService productService) =>
            {
                var result = await productService.DeleteProductAsync(id);

                return result.IsSuccess
                    ? Results.NoContent()
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("DeleteProduct");

            return app;
        }
    }
}
