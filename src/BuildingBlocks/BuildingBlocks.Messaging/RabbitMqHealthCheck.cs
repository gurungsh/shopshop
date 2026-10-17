using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.Messaging;

public sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly RabbitMqConnection _connection;

    public RabbitMqHealthCheck(RabbitMqConnection connection)
    {
        _connection = connection;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await _connection.GetConnectionAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
