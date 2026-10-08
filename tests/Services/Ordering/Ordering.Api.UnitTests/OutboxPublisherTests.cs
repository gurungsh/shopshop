using BuildingBlocks.Contracts.Orders;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Ordering.Api.Messaging;
using Ordering.Api.Options;
using Ordering.Infrastructure.Data;
using Ordering.Infrastructure.Models;

namespace Ordering.Api.UnitTests;

public class OutboxPublisherTests
{
    private readonly OutboxPublisher _outboxPublisher;
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<IMessagePublisher> _mockPublisher;
    private readonly Mock<ILogger<OutboxPublisher>> _mockLogger;

    public OutboxPublisherTests()
    {
        var databaseName = $"test-db-{Guid.NewGuid()}";

        // OutboxPublisher resolves the DbContext from a new scope, so it needs a real service provider
        _serviceProvider = new ServiceCollection()
            .AddDbContext<OrderingDbContext>(options => options.UseInMemoryDatabase(databaseName))
            .BuildServiceProvider();

        _mockPublisher = new Mock<IMessagePublisher>();
        _mockLogger = new Mock<ILogger<OutboxPublisher>>();

        _outboxPublisher = new OutboxPublisher(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _mockPublisher.Object,
            Microsoft.Extensions.Options.Options.Create(new OutboxOptions { BatchSize = 2 }),
            _mockLogger.Object);
    }

    private async Task SeedAsync(params OutboxMessage[] messages)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        dbContext.OutboxMessages.AddRange(messages);
        await dbContext.SaveChangesAsync();
    }

    private async Task<List<OutboxMessage>> GetMessagesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        return await dbContext.OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredAtUtc).ToListAsync();
    }

    private static OutboxMessage CreateTestMessage(DateTime occurredAtUtc, DateTime? processedAtUtc = null)
    {
        return new OutboxMessage
        {
            Type = RoutingKeys.OrderPlaced,
            Payload = "{\"orderId\":\"00000000-0000-0000-0000-000000000001\"}",
            OccurredAtUtc = occurredAtUtc,
            ProcessedAtUtc = processedAtUtc
        };
    }

    #region PublishPendingAsync Tests

    [Fact]
    public async Task PublishPendingAsync_WithPendingMessages_ShouldPublishInOrderAndMarkProcessed()
    {
        // Arrange
        var older = CreateTestMessage(DateTime.UtcNow.AddMinutes(-2));
        var newer = CreateTestMessage(DateTime.UtcNow.AddMinutes(-1));
        await SeedAsync(newer, older);

        var publishedIds = new List<string>();
        _mockPublisher
            .Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, ReadOnlyMemory<byte>, CancellationToken>((_, _, messageId, _, _) => publishedIds.Add(messageId))
            .Returns(Task.CompletedTask);

        // Act
        var published = await _outboxPublisher.PublishPendingAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, published);
        Assert.Equal([older.Id.ToString(), newer.Id.ToString()], publishedIds);

        _mockPublisher.Verify(p => p.PublishAsync(
            MessagingTopology.OrdersExchange,
            RoutingKeys.OrderPlaced,
            It.IsAny<string>(),
            It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));

        var messages = await GetMessagesAsync();
        Assert.All(messages, m => Assert.NotNull(m.ProcessedAtUtc));
    }

    [Fact]
    public async Task PublishPendingAsync_WhenPublishFails_ShouldRecordErrorAndStopBatch()
    {
        // Arrange
        var first = CreateTestMessage(DateTime.UtcNow.AddMinutes(-2));
        var second = CreateTestMessage(DateTime.UtcNow.AddMinutes(-1));
        await SeedAsync(first, second);

        _mockPublisher
            .Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Broker unreachable."));

        // Act
        var published = await _outboxPublisher.PublishPendingAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, published);
        _mockPublisher.Verify(p => p.PublishAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<CancellationToken>()), Times.Once);

        var messages = await GetMessagesAsync();
        Assert.All(messages, m => Assert.Null(m.ProcessedAtUtc));
        Assert.Equal(1, messages[0].Attempts);
        Assert.Equal("Broker unreachable.", messages[0].LastError);
        Assert.Equal(0, messages[1].Attempts);
    }

    [Fact]
    public async Task PublishPendingAsync_WithProcessedMessages_ShouldSkipThem()
    {
        // Arrange
        var processed = CreateTestMessage(DateTime.UtcNow.AddMinutes(-2), processedAtUtc: DateTime.UtcNow.AddMinutes(-1));
        await SeedAsync(processed);

        // Act
        var published = await _outboxPublisher.PublishPendingAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, published);
        _mockPublisher.Verify(p => p.PublishAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishPendingAsync_WithMoreMessagesThanBatchSize_ShouldPublishOnlyOneBatch()
    {
        // Arrange
        await SeedAsync(
            CreateTestMessage(DateTime.UtcNow.AddMinutes(-3)),
            CreateTestMessage(DateTime.UtcNow.AddMinutes(-2)),
            CreateTestMessage(DateTime.UtcNow.AddMinutes(-1)));

        // Act
        var published = await _outboxPublisher.PublishPendingAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, published);

        var messages = await GetMessagesAsync();
        Assert.Equal(2, messages.Count(m => m.ProcessedAtUtc is not null));
        Assert.Null(messages[2].ProcessedAtUtc);
    }

    #endregion
}
