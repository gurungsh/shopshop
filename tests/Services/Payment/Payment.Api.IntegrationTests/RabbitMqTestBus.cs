using System.Collections.Concurrent;
using BuildingBlocks.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Payment.Api.IntegrationTests;

/// <summary>
/// A tiny RabbitMQ client for tests: records every message published to the exchanges it
/// listens on, and publishes events the way another service would.
/// </summary>
public sealed class RabbitMqTestBus : IAsyncDisposable
{
    private readonly ConcurrentQueue<(string RoutingKey, byte[] Body)> _received = new();
    private readonly IConnection _connection;
    private readonly IChannel _channel;

    private RabbitMqTestBus(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    public static async Task<RabbitMqTestBus> ConnectAsync(string host, int port)
    {
        var factory = new ConnectionFactory { HostName = host, Port = port, UserName = "guest", Password = "guest" };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        return new RabbitMqTestBus(connection, channel);
    }

    /// <summary>
    /// Binds a private queue to the exchange. Call it before the service publishes: the services
    /// publish with mandatory: true, so a message with no bound queue is returned and retried.
    /// </summary>
    public async Task ListenAsync(string exchange, string bindingKey)
    {
        await _channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        var queue = await _channel.QueueDeclareAsync(queue: "", durable: false, exclusive: true, autoDelete: true);
        await _channel.QueueBindAsync(queue.QueueName, exchange, bindingKey);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, args) =>
        {
            _received.Enqueue((args.RoutingKey, args.Body.ToArray()));
            return Task.CompletedTask;
        };

        await _channel.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer);
    }

    public async Task PublishAsync<T>(string exchange, string routingKey, T message)
    {
        await _channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false);

        var body = System.Text.Encoding.UTF8.GetBytes(MessageSerializer.Serialize(message));
        var properties = new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent
        };

        await _channel.BasicPublishAsync(exchange, routingKey, mandatory: false, properties, body);
    }

    public async Task<T> WaitForAsync<T>(string routingKey, Func<T, bool> match, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));

        while (DateTime.UtcNow < deadline)
        {
            foreach (var (key, body) in _received.ToArray())
            {
                if (key != routingKey)
                {
                    continue;
                }

                var message = MessageSerializer.Deserialize<T>(body);
                if (match(message))
                {
                    return message;
                }
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"No '{routingKey}' message matching the predicate arrived in time.");
    }

    /// <summary>Number of messages waiting in a queue, e.g. a dead-letter queue.</summary>
    public async Task<uint> GetMessageCountAsync(string queue)
    {
        var declared = await _channel.QueueDeclarePassiveAsync(queue);
        return declared.MessageCount;
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
