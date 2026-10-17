using RabbitMQ.Client;

namespace BuildingBlocks.Messaging;

public sealed class RabbitMqPublisher : IMessagePublisher, IAsyncDisposable
{
    private readonly RabbitMqConnection _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly HashSet<string> _declareExchanges = [];
    private IChannel? _channel;

    public RabbitMqPublisher(RabbitMqConnection connection)
    {
        _connection = connection;
    }

    public async Task PublishAsync(
        string exchange,
        string routingKey,
        string messageId,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);

            if (!_declareExchanges.Contains(exchange))
            {
                await channel.ExchangeDeclareAsync(
                    exchange: exchange,
                    type: ExchangeType.Topic,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: cancellationToken);

                _declareExchanges.Add(exchange);
            }

            var properties = new BasicProperties
            {
                MessageId = messageId,
                Type = routingKey,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            };

            await channel.BasicPublishAsync(
                exchange: exchange,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _declareExchanges.Clear();
        }

        var connection = await _connection.GetConnectionAsync(cancellationToken);

        _channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _lock.Dispose();
    }
}
