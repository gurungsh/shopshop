using System.Security.Claims;
using Common.Web;
using Identity.Api.DTOs;
using Identity.Api.Filters;
using Identity.Api.Services;

namespace Identity.Api.Endpoints
{
    public static class AuthEndpoints
    {
        public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/auth")
                .WithTags("Auth");

            // POST /api/auth/register
            group.MapPost("/register", async (RegisterRequest request, IAuthService authService) =>
            {
                var result = await authService.RegisterAsync(request);

                return result.IsSuccess
                    ? Results.Created($"/api/register/{result.Value!.Id}", result.Value)
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("Register")
            .AddEndpointFilter<ValidationFilter<RegisterRequest>>();

            // POST /api/auth/login
            group.MapPost("/login", async (LoginRequest request, IAuthService authService) =>
            {
                var result = await authService.LoginAsync(request);

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("Login")
            .AddEndpointFilter<ValidationFilter<LoginRequest>>();

            // POST /api/auth/refresh
            group.MapPost("/refresh", async (RefreshTokenRequest request, IAuthService authService) =>
            {
                var result = await authService.RefreshTokenAsync(request);

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("RefreshToken");

            // POST /api/auth/logout
            group.MapPost("/logout", async (ClaimsPrincipal principal, IAuthService authService) =>
            {
                var result = await authService.LogoutAsync(principal);

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : EndpointResults.ToHttpResult(result);
            })
            .WithName("Logout")
            .RequireAuthorization();

            return app;
        }
    }
}
