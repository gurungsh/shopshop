using Catalog.Api.DTOs;
using Catalog.Api.Filters;
using Catalog.Api.Services;
using Common.Core;
using Common.Web;

namespace Catalog.Api.Endpoints
{
    public static class AdminCategoryEndpoints
    {
        public static IEndpointRouteBuilder MapAdminCategoryEndpoints(
            this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/admin/categories")
                .RequireAuthorization(policy => policy.RequireRole(Roles.Admin))
                .WithTags("Admin");

            // POST /api/admin/categories
            group.MapPost("", async (
                AdminCreateCategoryRequest request,
                ICategoryService categoryService) =>
            {
                var result = await categoryService.CreateCategoryAsync(request);

                return result.IsSuccess
                    ? Results.Created(
                        $"/api/admin/categories/{result.Value!.Id}",
                        result.Value)
                    : EndpointResults.ToHttpResult(result);

            })
            .WithName("CreateCategory")
            .AddEndpointFilter<ValidationFilter<AdminCreateCategoryRequest>>();

            // PUT /api/admin/categories/{id}
            group.MapPut("/{id:guid}", async (
                Guid id,
                AdminUpdateCategoryRequest request,
                ICategoryService categoryService) =>
            {
                var result = await categoryService.UpdateCategoryAsync(id, request);

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : EndpointResults.ToHttpResult(result);

            })
            .WithName("UpdateCategory")
            .AddEndpointFilter<ValidationFilter<AdminUpdateCategoryRequest>>();

            // DELETE /api/admin/categories/{id}
            group.MapDelete("/{id:guid}", async (
                Guid id,
                ICategoryService categoryService) =>
            {
                var result = await categoryService.DeleteCategoryAsync(id);

                return result.IsSuccess
                    ? Results.NoContent()
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("DeleteCategory");

            return app;
        }
    }
}
