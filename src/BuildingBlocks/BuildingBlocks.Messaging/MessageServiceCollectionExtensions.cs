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

        services.Configure<RabbitMqOptions>(section);
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();

        return services;
    }
}
