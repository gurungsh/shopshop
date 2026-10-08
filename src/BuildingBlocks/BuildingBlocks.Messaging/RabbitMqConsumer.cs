using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BuildingBlocks.Messaging;

public abstract class RabbitMqConsumer : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private readonly RabbitMqConnection _connection;
    private readonly ILogger _logger;
    private IChannel? _channel;

    protected RabbitMqConsumer(RabbitMqConnection connection, ILogger logger)
    {
        _connection = connection;
        _logger = logger;
    }

    protected abstract string QueueName { get; }
    protected abstract string ExchangeName { get; }
    protected abstract IReadOnlyCollection<string> BindingKeys { get; }

    protected abstract Task HandleAsync(
        string routingKey,
        string messageId,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumingAsync(stoppingToken);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not start consuming. Queue:{Queue}. Retrying in {Delay}.", QueueName, ReconnectDelay);
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task StartConsumingAsync(CancellationToken cancellationToken)
    {
        var connection = await _connection.GetConnectionAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await DeclareTopologyAsync(channel, cancellationToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, args) => OnReceivedAsync(channel, args, cancellationToken);

        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, cancellationToken);

        _channel = channel;
        _logger.LogInformation("Consuming messages. Queue:{Queue}", QueueName);
    }

    private async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        var deadLetterQueue = MessagingTopology.DeadLetterQueueFor(QueueName);

        await channel.ExchangeDeclareAsync(
            exchange: MessagingTopology.DeadLetterExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: deadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(deadLetterQueue, MessagingTopology.DeadLetterExchange, QueueName, cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = MessagingTopology.DeadLetterExchange,
                ["x-dead-letter-routing-key"] = QueueName
            },
            cancellationToken: cancellationToken);

        foreach (var bindingKey in BindingKeys)
        {
            await channel.QueueBindAsync(QueueName, ExchangeName, bindingKey, cancellationToken: cancellationToken);
        }
    }

    private async Task OnReceivedAsync(IChannel channel, BasicDeliverEventArgs args, CancellationToken cancellationToken)
    {
        var messageId = args.BasicProperties.MessageId ?? string.Empty;

        try
        {
            await HandleAsync(args.RoutingKey, messageId, args.Body, cancellationToken);
            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Message handling failed; sent to dead-letter queue. Queue:{Queue}, Message Id:{MessageId}, Routing Key:{RoutingKey}", QueueName, messageId, args.RoutingKey);
            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
        }
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        base.Dispose();
    }
}
