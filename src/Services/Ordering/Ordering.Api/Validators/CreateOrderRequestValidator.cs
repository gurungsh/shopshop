using FluentValidation;
using Ordering.Api.DTOs;

namespace Ordering.Api.Validators
{
    public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
    {
        public CreateOrderRequestValidator()
        {
            RuleFor(x => x.ShippingAddress)
                .NotEmpty().WithMessage("Shipping address is required.")
                .MaximumLength(500).WithMessage("Shipping address must not exceed 500 characters.");

            RuleFor(x => x.Items)
                .NotEmpty().WithMessage("At least one item is required.");

            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.ProductId)
                    .NotEmpty().WithMessage("Product id is required.");

                item.RuleFor(i => i.Quantity)
                    .GreaterThan(0).WithMessage("Quantity must be greater than 0.");
            });
        }
    }
}
