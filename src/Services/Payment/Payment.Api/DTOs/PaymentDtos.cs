using Payment.Infrastructure.Constants;

namespace Payment.Api.DTOs;

public sealed record PaymentChargeRequest(
    Guid OrderId,
    int Attempt,
    decimal Amount,
    string? PaymentMethodId);

public sealed record PaymentChargeResult(
    PaymentStatus Status,
    string? FailureReason,
    string? ProviderReference);
