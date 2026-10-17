using FluentValidation;
using Ordering.Api.DTOs;

namespace Ordering.Api.Validators;

public sealed class RetryOrderPaymentRequestValidator : AbstractValidator<RetryOrderPaymentRequest>
{
    public RetryOrderPaymentRequestValidator()
    {
        RuleFor(x => x.PaymentMethodId)
            .NotEmpty().WithMessage("Payment method id is required.")
            .Matches(PaymentMethodIdRules.Pattern).WithMessage(PaymentMethodIdRules.Message);
    }
}
