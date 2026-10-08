using BuildingBlocks.Web;
using Catalog.Api.DTOs;
using Catalog.Api.Filters;
using Catalog.Api.Services;

namespace Catalog.Api.Endpoints
{
    public static class CategoryEndpoints
    {
        public static IEndpointRouteBuilder MapCategoryEndpoints(
            this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/categories")
                .WithTags("Category");

            // POST /api/categories/search
            group.MapPost("/search", async (
                CategoryQuery query,
                ICategoryService categoryService) =>
            {
                var result = await categoryService.GetCategoriesAsync(query);

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("GetCategories")
            .AddEndpointFilter<ValidationFilter<CategoryQuery>>();

            // GET /api/categories/{id}
            group.MapGet("/{id:guid}", async (
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
