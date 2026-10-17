using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Payment.Api.Services;
using Payment.Infrastructure.Data;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Payment.Api.IntegrationTests;

/// <summary>
/// Starts the real Payment.Api against throwaway PostgreSQL and RabbitMQ containers.
/// Stripe is replaced by <see cref="FakePaymentService"/>. The app applies its migrations
/// on startup (Development environment).
/// </summary>
public sealed class PaymentApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4").WithUsername("guest").WithPassword("guest").Build();

    /// <summary>Publishes order events and listens to everything Payment publishes.</summary>
    public RabbitMqTestBus Bus { get; private set; } = null!;

    // xUnit v2's IAsyncLifetime returns Task, WebApplicationFactory.DisposeAsync returns ValueTask,
    // so the interface is implemented explicitly.
    async Task IAsyncLifetime.InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

        Bus = await RabbitMqTestBus.ConnectAsync(_rabbit.Hostname, _rabbit.GetMappedPublicPort(5672));
        await Bus.ListenAsync(MessagingTopology.PaymentsExchange, "payment.*");

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
        builder.UseSetting("ConnectionStrings:PaymentDb", _postgres.GetConnectionString());
        builder.UseSetting("RabbitMq:HostName", _rabbit.Hostname);
        builder.UseSetting("RabbitMq:Port", _rabbit.GetMappedPublicPort(5672).ToString());
        builder.UseSetting("RabbitMq:UserName", "guest");
        builder.UseSetting("RabbitMq:Password", "guest");
        builder.UseSetting("Outbox:PollingInterval", "00:00:00.200");
        builder.UseSetting("Stripe:SecretKey", "sk_test_not_used_in_tests");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPaymentService>();
            services.AddScoped<IPaymentService, FakePaymentService>();
        });
    }

    public PaymentDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    }
}
