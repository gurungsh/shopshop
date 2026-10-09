using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Notification.Api.Services;
using Testcontainers.RabbitMq;

namespace Notification.Api.IntegrationTests;

/// <summary>
/// Starts the real Notification.Api against a throwaway RabbitMQ container.
/// The notification service is replaced by <see cref="RecordingNotificationService"/>,
/// which the real one would only log.
/// </summary>
public sealed class NotificationApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4").WithUsername("guest").WithPassword("guest").Build();

    public RecordingNotificationService Notifications { get; } = new();

    /// <summary>Publishes order events the way Ordering would.</summary>
    public RabbitMqTestBus Bus { get; private set; } = null!;

    // xUnit v2's IAsyncLifetime returns Task, WebApplicationFactory.DisposeAsync returns ValueTask,
    // so the interface is implemented explicitly.
    async Task IAsyncLifetime.InitializeAsync()
    {
        await _rabbit.StartAsync();

        Bus = await RabbitMqTestBus.ConnectAsync(_rabbit.Hostname, _rabbit.GetMappedPublicPort(5672));

        // Start the host now so the consumer's queue is being declared before the first test
        using var _ = CreateClient();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Bus.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("RabbitMq:HostName", _rabbit.Hostname);
        builder.UseSetting("RabbitMq:Port", _rabbit.GetMappedPublicPort(5672).ToString());
        builder.UseSetting("RabbitMq:UserName", "guest");
        builder.UseSetting("RabbitMq:Password", "guest");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationService>();
            services.AddSingleton<INotificationService>(Notifications);
        });
    }
}
