using System.Net;
using BuildingBlocks.Core;
using Microsoft.Extensions.Logging;
using Moq;
using Payment.Api.DTOs;
using Payment.Api.Services;
using Payment.Infrastructure.Constants;
using Stripe;

namespace Payment.Api.UnitTests
{
    public class StripePaymentServiceTests
    {
        private readonly StripePaymentService _paymentService;
        private readonly Mock<PaymentIntentService> _mockPaymentIntents;
        private readonly Mock<ILogger<StripePaymentService>> _mockLogger;

        public StripePaymentServiceTests()
        {
            _mockPaymentIntents = new Mock<PaymentIntentService>();
            _mockLogger = new Mock<ILogger<StripePaymentService>>();

            _paymentService = new StripePaymentService(_mockPaymentIntents.Object, _mockLogger.Object);
        }

        private static PaymentChargeRequest CreateRequest(int attempt = 1, decimal amount = 49.99m, string? paymentMethodId = "pm_card_visa")
            => new(Guid.NewGuid(), attempt, amount, paymentMethodId);

        private void SetupCreateReturns(PaymentIntent intent, Action<PaymentIntentCreateOptions, RequestOptions>? capture = null)
        {
            _mockPaymentIntents
                .Setup(s => s.CreateAsync(It.IsAny<PaymentIntentCreateOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
                .Callback<PaymentIntentCreateOptions, RequestOptions, CancellationToken>((o, r, _) => capture?.Invoke(o, r))
                .ReturnsAsync(intent);
        }

        private void SetupCreateThrows(StripeException exception)
        {
            _mockPaymentIntents
                .Setup(s => s.CreateAsync(It.IsAny<PaymentIntentCreateOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);
        }

        private static StripeException CreateStripeException(string type, string message, string? paymentIntentId = null)
            => new(HttpStatusCode.PaymentRequired,
                new StripeError
                {
                    Type = type,
                    Message = message,
                    Code = "card_declined",
                    DeclineCode = "generic_decline",
                    PaymentIntent = paymentIntentId is null ? null : new PaymentIntent { Id = paymentIntentId }
                },
                message);

        #region ChargeAsync Tests

        [Fact]
        public async Task ChargeAsync_StripeSucceeds_ShouldReturnSucceededAndSendUsdMinorUnits()
        {
            // Arrange
            var request = CreateRequest(amount: 49.99m);
            PaymentIntentCreateOptions? sent = null;
            SetupCreateReturns(new PaymentIntent { Id = "pi_123", Status = "succeeded" }, (o, _) => sent = o);

            // Act
            var result = await _paymentService.ChargeAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(PaymentStatus.Succeeded, result.Value!.Status);
            Assert.Equal("pi_123", result.Value.ProviderReference);
            Assert.Null(result.Value.FailureReason);

            Assert.NotNull(sent);
            Assert.Equal(4999, sent!.Amount);
            Assert.Equal("usd", sent.Currency);
            Assert.Equal("pm_card_visa", sent.PaymentMethod);
            Assert.True(sent.Confirm);
            Assert.Equal(request.OrderId.ToString(), sent.Metadata["orderId"]);
        }

        [Fact]
        public async Task ChargeAsync_SameAttempt_ShouldReuseIdempotencyKey()
        {
            // Arrange
            var request = CreateRequest(attempt: 2);
            var keys = new List<string>();
            SetupCreateReturns(new PaymentIntent { Id = "pi_123", Status = "succeeded" }, (_, r) => keys.Add(r.IdempotencyKey));

            // Act
            await _paymentService.ChargeAsync(request);
            await _paymentService.ChargeAsync(request);
            await _paymentService.ChargeAsync(request with { Attempt = 3 });

            // Assert
            Assert.Equal(3, keys.Count);
            Assert.Equal(keys[0], keys[1]);
            Assert.NotEqual(keys[0], keys[2]);
            Assert.Equal($"payment:{request.OrderId}:2", keys[0]);
        }

        [Fact]
        public async Task ChargeAsync_CardDeclined_ShouldReturnFailedWithStripeMessage()
        {
            // Arrange
            SetupCreateThrows(CreateStripeException("card_error", "Your card was declined.", "pi_declined"));

            // Act
            var result = await _paymentService.ChargeAsync(CreateRequest(paymentMethodId: "pm_card_chargeDeclined"));

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(PaymentStatus.Failed, result.Value!.Status);
            Assert.Equal("Your card was declined.", result.Value.FailureReason);
            Assert.Equal("pi_declined", result.Value.ProviderReference);
        }

        [Fact]
        public async Task ChargeAsync_RequiresAction_ShouldReturnFailedWithNotCompletedReason()
        {
            // Arrange
            SetupCreateReturns(new PaymentIntent { Id = "pi_3ds", Status = "requires_action" });

            // Act
            var result = await _paymentService.ChargeAsync(CreateRequest());

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(PaymentStatus.Failed, result.Value!.Status);
            Assert.Equal(StripePaymentService.NotCompletedReason, result.Value.FailureReason);
            Assert.Equal("pi_3ds", result.Value.ProviderReference);
        }

        [Fact]
        public async Task ChargeAsync_NoPaymentMethod_ShouldReturnFailedWithoutCallingStripe()
        {
            // Act
            var result = await _paymentService.ChargeAsync(CreateRequest(paymentMethodId: null));

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(PaymentStatus.Failed, result.Value!.Status);
            Assert.Equal(StripePaymentService.MissingPaymentMethodReason, result.Value.FailureReason);
            _mockPaymentIntents.Verify(s => s.CreateAsync(It.IsAny<PaymentIntentCreateOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ChargeAsync_InvalidRequestError_ShouldReturnBadRequest()
        {
            // Arrange
            SetupCreateThrows(CreateStripeException("invalid_request_error", "No such PaymentMethod: 'pm_unknown'"));

            // Act
            var result = await _paymentService.ChargeAsync(CreateRequest(paymentMethodId: "pm_unknown"));

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(ResultErrorType.BadRequest, result.ErrorType);
        }

        [Fact]
        public async Task ChargeAsync_StripeUnavailable_ShouldReturnServiceUnavailable()
        {
            // Arrange
            SetupCreateThrows(new StripeException("Connection failed"));

            // Act
            var result = await _paymentService.ChargeAsync(CreateRequest());

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(ResultErrorType.ServiceUnavailable, result.ErrorType);
        }

        #endregion
    }
}
