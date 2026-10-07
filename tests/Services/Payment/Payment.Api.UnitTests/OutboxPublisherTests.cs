using BuildingBlocks.Contracts.Payments;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Payment.Api.Messaging;
using Payment.Api.Options;
using Payment.Infrastructure.Data;
using Payment.Infrastructure.Models;

namespace Payment.Api.UnitTests
{
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
                .AddDbContext<PaymentDbContext>(options => options.UseInMemoryDatabase(databaseName))
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
            var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            dbContext.OutboxMessages.AddRange(messages);
            await dbContext.SaveChangesAsync();
        }

        private async Task<List<OutboxMessage>> GetMessagesAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            return await dbContext.OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredAtUtc).ToListAsync();
        }

        private static OutboxMessage CreateTestMessage(DateTime occurredAtUtc, DateTime? processedAtUtc = null)
        {
            return new OutboxMessage
            {
                Type = PaymentRoutingKeys.PaymentSucceeded,
                Payload = "{\"orderId\":\"00000000-0000-0000-0000-000000000001\"}",
                OccurredAtUtc = occurredAtUtc,
                ProcessedAtUtc = processedAtUtc
            };
        }

        private void SetupPublish(Action<string, string, string>? capture = null)
        {
            _mockPublisher
                .Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, string, ReadOnlyMemory<byte>, CancellationToken>((exchange, routingKey, messageId, _, _) => capture?.Invoke(exchange, routingKey, messageId))
                .Returns(Task.CompletedTask);
        }

        #region PublishPendingAsync Tests

        [Fact]
        public async Task PublishPendingAsync_WithPendingMessages_ShouldPublishToPaymentsExchangeInOrderAndMarkProcessed()
        {
            // Arrange
            var older = CreateTestMessage(DateTime.UtcNow.AddMinutes(-2));
            var newer = CreateTestMessage(DateTime.UtcNow.AddMinutes(-1));
            await SeedAsync(newer, older);

            var published = new List<(string Exchange, string RoutingKey, string MessageId)>();
            SetupPublish((exchange, routingKey, messageId) => published.Add((exchange, routingKey, messageId)));

            // Act
            var count = await _outboxPublisher.PublishPendingAsync(CancellationToken.None);

            // Assert
            Assert.Equal(2, count);
            Assert.Equal([older.Id.ToString(), newer.Id.ToString()], published.Select(p => p.MessageId));
            Assert.All(published, p => Assert.Equal(MessagingTopology.PaymentsExchange, p.Exchange));
            Assert.All(published, p => Assert.Equal(PaymentRoutingKeys.PaymentSucceeded, p.RoutingKey));
            Assert.All(await GetMessagesAsync(), m => Assert.NotNull(m.ProcessedAtUtc));
        }

        [Fact]
        public async Task PublishPendingAsync_WithProcessedMessages_ShouldSkipThem()
        {
            // Arrange
            await SeedAsync(CreateTestMessage(DateTime.UtcNow.AddMinutes(-1), processedAtUtc: DateTime.UtcNow));
            SetupPublish();

            // Act
            var count = await _outboxPublisher.PublishPendingAsync(CancellationToken.None);

            // Assert
            Assert.Equal(0, count);
            _mockPublisher.Verify(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
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
                .ThrowsAsync(new InvalidOperationException("Broker unreachable"));

            // Act
            var count = await _outboxPublisher.PublishPendingAsync(CancellationToken.None);

            // Assert
            Assert.Equal(0, count);
            _mockPublisher.Verify(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Once);

            var messages = await GetMessagesAsync();
            Assert.Equal(1, messages[0].Attempts);
            Assert.Equal("Broker unreachable", messages[0].LastError);
            Assert.Null(messages[0].ProcessedAtUtc);
            Assert.Equal(0, messages[1].Attempts);
        }

        [Fact]
        public async Task PublishPendingAsync_WithMoreThanBatchSize_ShouldPublishOnlyBatch()
        {
            // Arrange
            await SeedAsync(
                CreateTestMessage(DateTime.UtcNow.AddMinutes(-3)),
                CreateTestMessage(DateTime.UtcNow.AddMinutes(-2)),
                CreateTestMessage(DateTime.UtcNow.AddMinutes(-1)));
            SetupPublish();

            // Act
            var count = await _outboxPublisher.PublishPendingAsync(CancellationToken.None);

            // Assert
            Assert.Equal(2, count);
            Assert.Single(await GetMessagesAsync(), m => m.ProcessedAtUtc is null);
        }

        #endregion
    }
}
