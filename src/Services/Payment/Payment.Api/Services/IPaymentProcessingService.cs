using BuildingBlocks.Core;
using Payment.Api.DTOs;

namespace Payment.Api.Services;

public interface IPaymentProcessingService
{
    Task<Result<bool>> ProcessPaymentAsync(PaymentChargeRequest request, CancellationToken cancellationToken = default);
}
