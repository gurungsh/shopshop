using BuildingBlocks.Contracts.Payments;
using BuildingBlocks.Core;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Payment.Api.DTOs;
using Payment.Infrastructure.Constants;
using Payment.Infrastructure.Data;
using Payment.Infrastructure.Models;

namespace Payment.Api.Services;

public class PaymentProcessingService : IPaymentProcessingService
{
    public const string GenericFailureReason = "We couldn't process your payment. Please try again.";

    private readonly PaymentDbContext _dbContext;
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentProcessingService> _logger;

    public PaymentProcessingService(
        PaymentDbContext dbContext,
        IPaymentService paymentService,
        ILogger<PaymentProcessingService> logger)
    {
        _dbContext = dbContext;
        _paymentService = paymentService;
        _logger = logger;
    }

    public async Task<Result<bool>> ProcessPaymentAsync(PaymentChargeRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0 || request.Attempt < 1)
        {
            _logger.LogWarning("Invalid payment request. Order Id:{OrderId}, Attempt:{Attempt}, Amount:{Amount}", request.OrderId, request.Attempt, request.Amount);
            return Result<bool>.Failure("Payment amount and attempt must be greater than zero.", ResultErrorType.BadRequest);
        }

        var alreadyProcessed = await _dbContext.PaymentTransactions
            .AnyAsync(p => p.OrderId == request.OrderId && p.Attempt == request.Attempt, cancellationToken);

        if (alreadyProcessed)
        {
            _logger.LogWarning("Payment attempt already processed, skipping. Order Id:{OrderId}, Attempt:{Attempt}", request.OrderId, request.Attempt);
            return Result<bool>.Success(false);
        }

        var chargeResult = await _paymentService.ChargeAsync(request, cancellationToken);

        var payment = new PaymentTransaction
        {
            OrderId = request.OrderId,
            Attempt = request.Attempt,
            Amount = request.Amount,
            Status = chargeResult.IsSuccess ? chargeResult.Value!.Status : PaymentStatus.Failed,
            FailureReason = chargeResult.IsSuccess ? chargeResult.Value!.FailureReason : GenericFailureReason,
            ProviderReference = chargeResult.IsSuccess ? chargeResult.Value!.ProviderReference : null
        };

        _dbContext.PaymentTransactions.Add(payment);
        AddPaymentOutboxMessage(payment);

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (payment.Status == PaymentStatus.Succeeded)
        {
            _logger.LogInformation("Payment succeeded. Payment Id:{PaymentId}, Order Id:{OrderId}, Attempt:{Attempt}, Amount:{Amount}", payment.Id, payment.OrderId, payment.Attempt, payment.Amount);
        }
        else
        {
            _logger.LogWarning("Payment failed. Payment Id:{PaymentId}, Order Id:{OrderId}, Attempt:{Attempt}, Reason:{Reason}", payment.Id, payment.OrderId, payment.Attempt, payment.FailureReason);
        }

        return Result<bool>.Success(true);
    }

    private void AddPaymentOutboxMessage(PaymentTransaction payment)
    {
        if (payment.Status == PaymentStatus.Succeeded)
        {
            AddOutboxMessage(PaymentRoutingKeys.PaymentSucceeded, new PaymentSucceededEvent(
                payment.OrderId,
                payment.Id,
                payment.Attempt,
                payment.Amount,
                payment.ProviderReference,
                payment.CreatedAtUtc));
        }
        else
        {
            AddOutboxMessage(PaymentRoutingKeys.PaymentFailed, new PaymentFailedEvent(
                payment.OrderId,
                payment.Id,
                payment.Attempt,
                payment.Amount,
                payment.FailureReason ?? GenericFailureReason,
                payment.CreatedAtUtc));
        }
    }

    private void AddOutboxMessage<TEvent>(string routingKey, TEvent @event)
    {
        _dbContext.OutboxMessages.Add(new OutboxMessage
        {
            Type = routingKey,
            Payload = MessageSerializer.Serialize(@event)
        });
    }
}
