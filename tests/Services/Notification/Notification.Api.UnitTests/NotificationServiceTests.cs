using BuildingBlocks.Contracts.Orders;
using Microsoft.Extensions.Logging;
using Moq;
using Notification.Api.Services;

namespace Notification.Api.UnitTests
{
    public class NotificationServiceTests
    {
        private readonly NotificationService _notificationService;
        private readonly Mock<ILogger<NotificationService>> _mockLogger;

        public NotificationServiceTests()
        {
            _mockLogger = new Mock<ILogger<NotificationService>>();

            _notificationService = new NotificationService(_mockLogger.Object);
        }

        private void VerifyLoggedInformation(params string[] expectedFragments)
        {
            _mockLogger.Verify(l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => expectedFragments.All(f => state.ToString()!.Contains(f))),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #region NotifyOrderConfirmedAsync Tests

        [Fact]
        public async Task NotifyOrderConfirmedAsync_ValidEvent_ShouldLogOrderDetails()
        {
            // Arrange
            var orderConfirmed = new OrderConfirmedEvent(Guid.NewGuid(), Guid.NewGuid(), 49.99m, DateTime.UtcNow);

            // Act
            await _notificationService.NotifyOrderConfirmedAsync(orderConfirmed);

            // Assert
            VerifyLoggedInformation("Order confirmed", orderConfirmed.OrderId.ToString(), orderConfirmed.CustomerId.ToString());
        }

        #endregion

        #region NotifyOrderPaymentFailedAsync Tests

        [Fact]
        public async Task NotifyOrderPaymentFailedAsync_ValidEvent_ShouldLogReasonAndRetriesRemaining()
        {
            // Arrange
            var orderPaymentFailed = new OrderPaymentFailedEvent(Guid.NewGuid(), Guid.NewGuid(), "Your card was declined.", 2, DateTime.UtcNow);

            // Act
            await _notificationService.NotifyOrderPaymentFailedAsync(orderPaymentFailed);

            // Assert
            VerifyLoggedInformation("Order payment failed", orderPaymentFailed.OrderId.ToString(), "Your card was declined.", "Retries Remaining:2");
        }

        #endregion

        #region NotifyOrderCancelledAsync Tests

        [Fact]
        public async Task NotifyOrderCancelledAsync_ValidEvent_ShouldLogWhoCancelled()
        {
            // Arrange
            var orderCancelled = new OrderCancelledEvent(Guid.NewGuid(), Guid.NewGuid(), "Customer", DateTime.UtcNow);

            // Act
            await _notificationService.NotifyOrderCancelledAsync(orderCancelled);

            // Assert
            VerifyLoggedInformation("Order cancelled", orderCancelled.OrderId.ToString(), "Cancelled By:Customer");
        }

        #endregion
    }
}
