using FluentValidation;
using Ordering.Api.DTOs;

namespace Ordering.Api.Validators
{
    public sealed class AdminUpdateOrderRequestValidator : AbstractValidator<AdminUpdateOrderRequest>
    {
        public AdminUpdateOrderRequestValidator()
        {
            RuleFor(x => x.Status)
                .IsInEnum().WithMessage("Status is not a valid order status.")
                .When(x => x.Status.HasValue);

            RuleFor(x => x.ShippingAddress)
                .MaximumLength(500).WithMessage("Shipping address must not exceed 500 characters.")
                .When(x => x.ShippingAddress is not null);
        }
    }
}
