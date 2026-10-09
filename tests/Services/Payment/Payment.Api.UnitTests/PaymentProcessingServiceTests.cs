using System.Diagnostics;
using System.Text;
using BuildingBlocks.Contracts.Payments;
using BuildingBlocks.Core;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Payment.Api.DTOs;
using Payment.Api.Services;
using Payment.Infrastructure.Constants;
using Payment.Infrastructure.Data;
using Payment.Infrastructure.Models;

namespace Payment.Api.UnitTests;

public class PaymentProcessingServiceTests
{
    private readonly PaymentProcessingService _processingService;
    private readonly PaymentDbContext _dbContext;
    private readonly Mock<IPaymentService> _mockPaymentService;
    private readonly Mock<ILogger<PaymentProcessingService>> _mockLogger;

    public PaymentProcessingServiceTests()
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase($"test-db-{Guid.NewGuid()}")
            .Options;

        _dbContext = new PaymentDbContext(options);
        _mockPaymentService = new Mock<IPaymentService>();
        _mockLogger = new Mock<ILogger<PaymentProcessingService>>();

        _processingService = new PaymentProcessingService(_dbContext, _mockPaymentService.Object, _mockLogger.Object);
    }

    private static PaymentChargeRequest CreateRequest(int attempt = 1, decimal amount = 100m)
        => new(Guid.NewGuid(), attempt, amount, "pm_card_visa");

    private void SetupCharge(Result<PaymentChargeResult> result)
    {
        _mockPaymentService
            .Setup(s => s.ChargeAsync(It.IsAny<PaymentChargeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }

    private static TEvent ReadOutboxEvent<TEvent>(OutboxMessage message)
    {
        return MessageSerializer.Deserialize<TEvent>(Encoding.UTF8.GetBytes(message.Payload));
    }

    #region ProcessPaymentAsync Tests

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProcessPaymentAsync_ChargeSucceeds_ShouldRecordPaymentAndQueuePaymentSucceededWithCurrentTraceParent(bool hasActiveTrace)
    {
        // Arrange
        Activity.Current = null;
        using var activity = hasActiveTrace ? new Activity("test-delivery").Start() : null;

        var request = CreateRequest(attempt: 2, amount: 49.99m);
        SetupCharge(Result<PaymentChargeResult>.Success(new PaymentChargeResult(PaymentStatus.Succeeded, null, "pi_123")));

        // Act
        var result = await _processingService.ProcessPaymentAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);

        var payment = await _dbContext.PaymentTransactions.SingleAsync();
        Assert.Equal(request.OrderId, payment.OrderId);
        Assert.Equal(2, payment.Attempt);
        Assert.Equal(49.99m, payment.Amount);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal("pi_123", payment.ProviderReference);

        var message = await _dbContext.OutboxMessages.SingleAsync();
        Assert.Equal(PaymentRoutingKeys.PaymentSucceeded, message.Type);
        Assert.Equal(activity?.Id, message.TraceParent);
        var succeeded = ReadOutboxEvent<PaymentSucceededEvent>(message);
        Assert.Equal(request.OrderId, succeeded.OrderId);
        Assert.Equal(payment.Id, succeeded.PaymentId);
        Assert.Equal(2, succeeded.Attempt);
    }

    [Fact]
    public async Task ProcessPaymentAsync_CardDeclined_ShouldQueuePaymentFailedWithProviderReason()
    {
        // Arrange
        var request = CreateRequest();
        SetupCharge(Result<PaymentChargeResult>.Success(new PaymentChargeResult(PaymentStatus.Failed, "Your card was declined.", "pi_declined")));

        // Act
        var result = await _processingService.ProcessPaymentAsync(request);

        // Assert
        Assert.True(result.IsSuccess);

        var payment = await _dbContext.PaymentTransactions.SingleAsync();
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("Your card was declined.", payment.FailureReason);

        var message = await _dbContext.OutboxMessages.SingleAsync();
        Assert.Equal(PaymentRoutingKeys.PaymentFailed, message.Type);
        var failed = ReadOutboxEvent<PaymentFailedEvent>(message);
        Assert.Equal(request.OrderId, failed.OrderId);
        Assert.Equal(1, failed.Attempt);
        Assert.Equal("Your card was declined.", failed.Reason);
    }

    [Theory]
    [InlineData(ResultErrorType.ServiceUnavailable)]
    [InlineData(ResultErrorType.BadRequest)]
    public async Task ProcessPaymentAsync_ProviderError_ShouldQueuePaymentFailedWithGenericReason(ResultErrorType errorType)
    {
        // Arrange
        var request = CreateRequest();
        SetupCharge(Result<PaymentChargeResult>.Failure("Internal provider detail", errorType));

        // Act
        var result = await _processingService.ProcessPaymentAsync(request);

        // Assert
        Assert.True(result.IsSuccess);

        var payment = await _dbContext.PaymentTransactions.SingleAsync();
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(PaymentProcessingService.GenericFailureReason, payment.FailureReason);

        var message = await _dbContext.OutboxMessages.SingleAsync();
        Assert.Equal(PaymentProcessingService.GenericFailureReason, ReadOutboxEvent<PaymentFailedEvent>(message).Reason);
    }

    [Fact]
    public async Task ProcessPaymentAsync_AttemptAlreadyProcessed_ShouldNotChargeAgain()
    {
        // Arrange
        var request = CreateRequest(attempt: 1);
        _dbContext.PaymentTransactions.Add(new PaymentTransaction
        {
            OrderId = request.OrderId,
            Attempt = 1,
            Amount = request.Amount,
            Status = PaymentStatus.Failed
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _processingService.ProcessPaymentAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        _mockPaymentService.Verify(s => s.ChargeAsync(It.IsAny<PaymentChargeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Single(_dbContext.PaymentTransactions);
        Assert.Empty(_dbContext.OutboxMessages);
    }

    [Fact]
    public async Task ProcessPaymentAsync_NewAttemptForSameOrder_ShouldCharge()
    {
        // Arrange
        var request = CreateRequest(attempt: 2);
        _dbContext.PaymentTransactions.Add(new PaymentTransaction
        {
            OrderId = request.OrderId,
            Attempt = 1,
            Amount = request.Amount,
            Status = PaymentStatus.Failed
        });
        await _dbContext.SaveChangesAsync();
        SetupCharge(Result<PaymentChargeResult>.Success(new PaymentChargeResult(PaymentStatus.Succeeded, null, "pi_456")));

        // Act
        var result = await _processingService.ProcessPaymentAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.Equal(2, await _dbContext.PaymentTransactions.CountAsync(p => p.OrderId == request.OrderId));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, 0)]
    public async Task ProcessPaymentAsync_InvalidAmountOrAttempt_ShouldReturnBadRequest(int amount, int attempt)
    {
        // Arrange
        var request = CreateRequest(attempt: attempt, amount: amount);

        // Act
        var result = await _processingService.ProcessPaymentAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorType.BadRequest, result.ErrorType);
        _mockPaymentService.Verify(s => s.ChargeAsync(It.IsAny<PaymentChargeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}
