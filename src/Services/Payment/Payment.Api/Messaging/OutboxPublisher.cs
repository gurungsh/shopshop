using System.Text;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payment.Api.Options;
using Payment.Infrastructure.Data;

namespace Payment.Api.Messaging;

// Publishes pending outbox rows (payment results) to the payments exchange
public class OutboxPublisher : BackgroundService
{
    private const int MaxErrorLength = 2000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessagePublisher _publisher;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxPublisher> _logger;

    public OutboxPublisher(
        IServiceScopeFactory scopeFactory,
        IMessagePublisher publisher,
        IOptions<OutboxOptions> options,
        ILogger<OutboxPublisher> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PollingInterval);

        do
        {
            try
            {
                await PublishPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Outbox publishing cycle failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<int> PublishPendingAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();

        var messages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null)
            .OrderBy(m => m.OccurredAtUtc)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        var published = 0;

        foreach (var message in messages)
        {
            try
            {
                await _publisher.PublishAsync(
                    MessagingTopology.PaymentsExchange,
                    message.Type,
                    message.Id.ToString(),
                    Encoding.UTF8.GetBytes(message.Payload),
                    cancellationToken);

                message.ProcessedAtUtc = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                published++;

                _logger.LogInformation("Outbox message published. Message Id:{MessageId}, Type:{Type}", message.Id, message.Type);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > MaxErrorLength ? ex.Message[..MaxErrorLength] : ex.Message;
                await dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogWarning(ex, "Outbox message publish failed. Message Id:{MessageId}, Type:{Type}", message.Id, message.Type);

                // Stop here so messages keep their order; the rest are retried on the next poll
                break;
            }
        }

        return published;
    }
}
