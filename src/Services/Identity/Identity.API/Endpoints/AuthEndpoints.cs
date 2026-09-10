using Identity.API.Constants;
using Identity.API.DTOs;
using Identity.API.Filters;
using Identity.API.Services;
using Identity.API.Services.Common;
using Serilog;

namespace Identity.API.Endpoints
{
    public static class AuthEndpoints
    {
        public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/auth");

            // POST /api/auth/register
            group.MapPost("/register", async (RegisterRequest request, IAuthService authService) =>
            {
                var result = await authService.RegisterAsync(request); ;

                return result.IsSuccess
                ? Results.Created($"/api/users/{result.Value!.Id}", result.Value)
                : ToHttpResult(result);
            })
            .WithName("Register")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin))
            .AddEndpointFilter<ValidationFilter<RegisterRequest>>();


            // POST /api/auth/login
            group.MapPost("/login", async (LoginRequest request, IAuthService authService) =>
            {
                var result = await authService.LoginAsync(request);

                return result.IsSuccess
                ? Results.Ok(result.Value)
                : ToHttpResult(result);
            })
            .WithName("Login")
            .AddEndpointFilter<ValidationFilter<LoginRequest>>();

            return app;
        }

        private static IResult ToHttpResult<T>(Result<T> result) => result.ErrorType switch
        {
            ResultErrorType.BadRequest => Results.BadRequest(new { error = result.Error }),
            ResultErrorType.Unauthorized => Results.Unauthorized(),
            ResultErrorType.Conflict => Results.Conflict(new { error = result.Error }),
            ResultErrorType.NotFound => Results.NotFound(new { error = result.Error }),
            _ => Results.Problem(result.Error)
        };
    }
}
