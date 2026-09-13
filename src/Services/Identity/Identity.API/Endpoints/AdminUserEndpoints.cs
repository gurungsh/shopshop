using Identity.API.Common;
using Identity.API.Constants;
using Identity.API.DTOs;
using Identity.API.Filters;
using Identity.API.Services;

namespace Identity.API.Endpoints;

public static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users")
            .RequireAuthorization(policy =>
                policy.RequireRole(Roles.Admin))
            .WithTags("Admin");

        // GET /api/admin/users
        group.MapGet("", async (
            IAuthService authService) =>
        {
            var result = await authService.GetUsersAsync();

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : EndpointResults.ToHttpResult(result);
        })
        .WithName("GetUsers");

        // GET /api/admin/users/{email}
        group.MapGet("/{email}", async (
            string email,
            IAuthService authService) =>
        {
            var result = await authService.GetUserAsync(email);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : EndpointResults.ToHttpResult(result);
        })
        .WithName("GetUser");

        // POST /api/admin/users
        group.MapPost("", async (
            AdminCreateUserRequest request,
            IAuthService authService) =>
        {
            var result = await authService.CreateUserAsync(request);

            return result.IsSuccess
                ? Results.Created(
                    $"/api/admin/users/{result.Value!.Id}",
                    result.Value)
                : EndpointResults.ToHttpResult(result);
        })
        .WithName("CreateUser")
        .AddEndpointFilter<ValidationFilter<AdminCreateUserRequest>>();

        // PUT /api/admin/users/{id}
        group.MapPut("/{id:guid}", async (
            Guid id,
            AdminUpdateUserRequest request,
            IAuthService authService) =>
        {
            var result = await authService.UpdateUserAsync(
                id,
                request);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : EndpointResults.ToHttpResult(result);
        })
        .WithName("UpdateUser")
        .AddEndpointFilter<ValidationFilter<AdminUpdateUserRequest>>();

        // DELETE /api/admin/users/{id}
        group.MapDelete("/{id:guid}", async (
            Guid id,
            IAuthService authService) =>
        {
            var result = await authService.DeleteUserAsync(id);

            return result.IsSuccess
                ? Results.NoContent()
                : EndpointResults.ToHttpResult(result);
        })
        .WithName("DeleteUser");

        return app;
    }
}