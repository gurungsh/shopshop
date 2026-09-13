using Identity.API.Common;
using Identity.API.DTOs;
using Identity.API.Filters;
using Identity.API.Services;

namespace Identity.API.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .RequireAuthorization()
            .WithTags("Users");

        // GET /api/users/me
        group.MapGet("/me", async (
            HttpContext httpContext,
            IAuthService authService) =>
        {
            var result = await authService.GetCurrentUserAsync(
                httpContext.User);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : EndpointResults.ToHttpResult(result);
        })
        .WithName("GetCurrentUser");

        // PUT /api/users/me
        group.MapPut("/me", async (
            UpdateCurrentUserRequest request,
            HttpContext httpContext,
            IAuthService authService) =>
        {
            var result = await authService.UpdateCurrentUserAsync(
                httpContext.User,
                request);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : EndpointResults.ToHttpResult(result);
        })
        .WithName("UpdateCurrentUser")
        .AddEndpointFilter<ValidationFilter<UpdateUserRequest>>();

        return app;
    }
}