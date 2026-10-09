using Catalog.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Catalog.Api.IntegrationTests;

/// <summary>
/// Starts the real Catalog.Api against throwaway PostgreSQL and Redis containers.
/// The app applies its migrations on startup (Development environment).
/// </summary>
public sealed class CatalogApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7").Build();

    // xUnit v2's IAsyncLifetime returns Task, WebApplicationFactory.DisposeAsync returns ValueTask,
    // so the interface is implemented explicitly.
    async Task IAsyncLifetime.InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:CatalogDb", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
        builder.UseSetting("JwtSettings:MetadataAddress", "https://localhost/.well-known/openid-configuration");
        builder.UseSetting("JwtSettings:Issuer", "test");
        builder.UseSetting("JwtSettings:Audience", "test");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    public CatalogDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    }
}
