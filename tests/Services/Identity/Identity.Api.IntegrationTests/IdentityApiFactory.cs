using System.Security.Cryptography;
using Identity.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Identity.Api.IntegrationTests;

/// <summary>
/// Starts the real Identity.Api against a throwaway PostgreSQL container with a freshly
/// generated signing key. The app applies its migrations on startup (Development environment).
/// </summary>
public sealed class IdentityApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Issuer = "shopshop-tests";
    public const string Audience = "shopshop-tests";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private readonly string _privateKeyPem = RSA.Create(2048).ExportPkcs8PrivateKeyPem();

    // xUnit v2's IAsyncLifetime returns Task, WebApplicationFactory.DisposeAsync returns ValueTask,
    // so the interface is implemented explicitly.
    async Task IAsyncLifetime.InitializeAsync() => await _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync() => await _postgres.DisposeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:AuthDb", _postgres.GetConnectionString());
        builder.UseSetting("JwtSettings:PrivateKeyPem", _privateKeyPem);
        builder.UseSetting("JwtSettings:KeyId", "test-key");
        builder.UseSetting("JwtSettings:Issuer", Issuer);
        builder.UseSetting("JwtSettings:Audience", Audience);
        builder.UseSetting("JwtSettings:ExpirationInMinutes", "5");
    }

    public IdentityDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    }
}
