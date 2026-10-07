using BuildingBlocks.Core;
using Payment.Api.DTOs;

namespace Payment.Api.Services
{
    public interface IPaymentService
    {
        Task<Result<PaymentChargeResult>> ChargeAsync(PaymentChargeRequest request, CancellationToken cancellationToken = default);
    }
}
