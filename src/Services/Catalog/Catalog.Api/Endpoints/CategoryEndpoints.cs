using Catalog.Api.DTOs;
using Catalog.Api.Services;
using Common.Web;

namespace Catalog.Api.Endpoints
{
    public static class CategoryEndpoints
    {
        public static IEndpointRouteBuilder MapCategoryEndpoints(
            this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("api/categories")
                .WithTags("Category");

            // POST /api/categories
            group.MapPost("", async (
                CategoryQuery query,
                ICategoryService categoryService) =>
            {
                var result = await categoryService.GetCategoriesAsync(query);

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("GetCategories");

            // GET /api/categories/{id}
            group.MapGet("/{id}", async (
                Guid id,
                ICategoryService categoryService) =>
            {
                var result = await categoryService.GetCategoryAsync(id);

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("GetCategory");

            return app;
        }
    }
}
