using BuildingBlocks.Core;
using Payment.Api.DTOs;
using Payment.Api.Services;
using Payment.Infrastructure.Constants;

namespace Payment.Api.IntegrationTests;

/// <summary>
/// Stands in for Stripe so the tests never call the internet. Mirrors Stripe test mode:
/// <c>pm_card_visa</c> succeeds and any other payment method is declined.
/// </summary>
public sealed class FakePaymentService : IPaymentService
{
    public const string SucceedingPaymentMethod = "pm_card_visa";
    public const string DeclineReason = "Your card was declined.";

    public Task<Result<PaymentChargeResult>> ChargeAsync(
        PaymentChargeRequest request, CancellationToken cancellationToken = default)
    {
        var result = request.PaymentMethodId == SucceedingPaymentMethod
            ? new PaymentChargeResult(PaymentStatus.Succeeded, null, $"pi_{Guid.NewGuid():N}")
            : new PaymentChargeResult(PaymentStatus.Failed, DeclineReason, null);

        return Task.FromResult(Result<PaymentChargeResult>.Success(result));
    }
}
