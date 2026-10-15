using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging;

public static class MessageServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMq(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(RabbitMqOptions.SectionName);
        var options = section.Get<RabbitMqOptions>()
            ?? throw new InvalidOperationException("RabbitMq configuration is missing.");

        if (string.IsNullOrWhiteSpace(options.HostName)
            || string.IsNullOrWhiteSpace(options.UserName)
            || string.IsNullOrWhiteSpace(options.Password))
        {
            throw new InvalidOperationException("RabbitMq HostName, UserName and Password are required.");
        }

        if (options.Port is < 1 or > 65535)
        {
            throw new InvalidOperationException("RabbitMq Port must be between 1 and 65535.");
        }

        services.Configure<RabbitMqOptions>(section);
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();

        return services;
    }

    public static IServiceCollection AddOutboxOptions(this IServiceCollection services)
    {
        services.AddOptions<OutboxOptions>()
            .BindConfiguration(OutboxOptions.SectionName)
            .Validate(o => o.PollingInterval > TimeSpan.Zero, "Outbox PollingInterval must be greater than zero.")
            .Validate(o => o.BatchSize > 0, "Outbox BatchSize must be greater than zero.")
            .ValidateOnStart();

        return services;
    }
}
