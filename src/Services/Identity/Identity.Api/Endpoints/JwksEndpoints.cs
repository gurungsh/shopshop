using Identity.Api.DTOs;
using Identity.Api.Options;
using Identity.Api.Security;
using Microsoft.Extensions.Options;

namespace Identity.Api.Endpoints
{
    public static class JwksEndpoints
    {
        public static IEndpointRouteBuilder MapJwksEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/.well-known")
                .WithTags("Discovery");

            // GET /.well-known/jwks.json
            group.MapGet("/jwks.json", (IJwtKeyProvider jwtKeyProvider) =>
                Results.Ok(jwtKeyProvider.GetJwks()))
            .WithName("GetJwks");

            // GET /.well-known/openid-configuration
            group.MapGet("/openid-configuration", (IOptions<JwtOptions> jwtOptions, HttpContext context) =>
            {
                var request = context.Request;
                var jwksUri = $"{request.Scheme}://{request.Host}/.well-known/jwks.json";

                return Results.Ok(new OpenIdConfigurationResponse(jwtOptions.Value.Issuer, jwksUri));
            })
            .WithName("GetOpenIdConfiguration");

            return app;
        }
    }
}
