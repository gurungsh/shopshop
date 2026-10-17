using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ordering.Api.Services;
using Ordering.Infrastructure.Data;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Ordering.Api.IntegrationTests;

/// <summary>
/// Starts the real Ordering.Api against throwaway PostgreSQL and RabbitMQ containers.
/// Catalog is replaced by <see cref="FakeCatalogServiceClient"/> and JWT validation by
/// <see cref="TestAuthHandler"/>. The app applies its migrations on startup (Development environment).
/// </summary>
public sealed class OrderingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4").WithUsername("guest").WithPassword("guest").Build();

    public FakeCatalogServiceClient Catalog { get; } = new();

    /// <summary>Listens to everything Ordering publishes on the orders exchange.</summary>
    public RabbitMqTestBus Bus { get; private set; } = null!;

    // xUnit v2's IAsyncLifetime returns Task, WebApplicationFactory.DisposeAsync returns ValueTask,
    // so the interface is implemented explicitly.
    async Task IAsyncLifetime.InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

        Bus = await RabbitMqTestBus.ConnectAsync(_rabbit.Hostname, _rabbit.GetMappedPublicPort(5672));
        await Bus.ListenAsync(MessagingTopology.OrdersExchange, "order.*");

        // Start the host now so migrations and the consumer's queue exist before the first test
        using var _ = CreateClient();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Bus.DisposeAsync();
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:OrderDb", _postgres.GetConnectionString());
        builder.UseSetting("RabbitMq:HostName", _rabbit.Hostname);
        builder.UseSetting("RabbitMq:Port", _rabbit.GetMappedPublicPort(5672).ToString());
        builder.UseSetting("RabbitMq:UserName", "guest");
        builder.UseSetting("RabbitMq:Password", "guest");
        builder.UseSetting("Outbox:PollingInterval", "00:00:00.200");
        builder.UseSetting("JwtSettings:MetadataAddress", "https://localhost/.well-known/openid-configuration");
        builder.UseSetting("JwtSettings:Issuer", "test");
        builder.UseSetting("JwtSettings:Audience", "test");
        builder.UseSetting("CatalogService:BaseUrl", "https://localhost:1/");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            services.RemoveAll<ICatalogServiceClient>();
            services.AddSingleton<ICatalogServiceClient>(Catalog);
        });
    }

    public OrderingDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
    }
}
