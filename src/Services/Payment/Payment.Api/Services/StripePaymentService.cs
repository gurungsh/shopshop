using BuildingBlocks.Core;
using Payment.Api.DTOs;
using Payment.Infrastructure.Constants;
using Stripe;

namespace Payment.Api.Services
{
    // Charges through Stripe. With a test-mode key (sk_test_...) the outcome is chosen by the test payment method,
    // e.g. pm_card_visa succeeds and pm_card_chargeDeclined is declined.
    public class StripePaymentService : IPaymentService
    {
        public const string Currency = "usd";
        public const string MissingPaymentMethodReason = "A payment method is required.";
        public const string NotCompletedReason = "This card needs additional verification, which isn't supported. Please use a different card.";

        private readonly PaymentIntentService _paymentIntents;
        private readonly ILogger<StripePaymentService> _logger;

        public StripePaymentService(PaymentIntentService paymentIntents, ILogger<StripePaymentService> logger)
        {
            _paymentIntents = paymentIntents;
            _logger = logger;
        }

        public async Task<Result<PaymentChargeResult>> ChargeAsync(PaymentChargeRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.PaymentMethodId))
            {
                _logger.LogWarning("Payment has no payment method. Order Id:{OrderId}, Attempt:{Attempt}", request.OrderId, request.Attempt);
                return Result<PaymentChargeResult>.Success(new PaymentChargeResult(PaymentStatus.Failed, MissingPaymentMethodReason, null));
            }

            var options = new PaymentIntentCreateOptions
            {
                Amount = (long)Math.Round(request.Amount * 100, MidpointRounding.AwayFromZero),
                Currency = Currency,
                PaymentMethod = request.PaymentMethodId,
                Confirm = true,
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true,
                    AllowRedirects = "never"
                },
                Metadata = new Dictionary<string, string>
                {
                    ["orderId"] = request.OrderId.ToString(),
                    ["attempt"] = request.Attempt.ToString()
                }
            };

            var requestOptions = new RequestOptions { IdempotencyKey = $"payment:{request.OrderId}:{request.Attempt}" };

            try
            {
                var intent = await _paymentIntents.CreateAsync(options, requestOptions, cancellationToken);

                if (intent.Status == "succeeded")
                {
                    _logger.LogInformation("Stripe payment succeeded. Order Id:{OrderId}, Attempt:{Attempt}, Payment Intent:{PaymentIntentId}", request.OrderId, request.Attempt, intent.Id);
                    return Result<PaymentChargeResult>.Success(new PaymentChargeResult(PaymentStatus.Succeeded, null, intent.Id));
                }

                _logger.LogWarning("Stripe payment not completed. Order Id:{OrderId}, Attempt:{Attempt}, Payment Intent:{PaymentIntentId}, Status:{Status}", request.OrderId, request.Attempt, intent.Id, intent.Status);
                return Result<PaymentChargeResult>.Success(new PaymentChargeResult(PaymentStatus.Failed, NotCompletedReason, intent.Id));
            }
            catch (StripeException ex) when (ex.StripeError?.Type == "card_error")
            {
                // Stripe's card_error messages are written to be shown to customers (fraud declines get a generic message)
                _logger.LogWarning("Stripe payment declined. Order Id:{OrderId}, Attempt:{Attempt}, Code:{Code}, Decline Code:{DeclineCode}", request.OrderId, request.Attempt, ex.StripeError!.Code, ex.StripeError.DeclineCode);
                return Result<PaymentChargeResult>.Success(new PaymentChargeResult(
                    PaymentStatus.Failed,
                    ex.StripeError.Message ?? "Your card was declined.",
                    ex.StripeError.PaymentIntent?.Id));
            }
            catch (StripeException ex) when (ex.StripeError?.Type == "invalid_request_error")
            {
                _logger.LogWarning("Stripe rejected the payment request. Order Id:{OrderId}, Attempt:{Attempt}, Message:{Message}", request.OrderId, request.Attempt, ex.StripeError!.Message);
                return Result<PaymentChargeResult>.Failure(ex.StripeError.Message ?? "Payment request was rejected.", ResultErrorType.BadRequest);
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe is unavailable or misconfigured. Order Id:{OrderId}, Attempt:{Attempt}", request.OrderId, request.Attempt);
                return Result<PaymentChargeResult>.Failure("Payment provider is unavailable.", ResultErrorType.ServiceUnavailable);
            }
        }
    }
}
