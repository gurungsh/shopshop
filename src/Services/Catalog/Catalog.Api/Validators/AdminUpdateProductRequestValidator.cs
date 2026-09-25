using Catalog.Api.DTOs;
using FluentValidation;

namespace Catalog.Api.Validators
{
    public sealed class AdminUpdateProductRequestValidator : AbstractValidator<AdminUpdateProductRequest>
    {
        public AdminUpdateProductRequestValidator()
        {
            RuleFor(x => x.CategoryId)
                .NotEmpty().WithMessage("CategoryId cannot be empty when provided.")
                .When(x => x.CategoryId is not null);

            RuleFor(x => x.Name)
                .MaximumLength(200).WithMessage("Name must not exceed 200 characters.")
                .When(x => !string.IsNullOrWhiteSpace(x.Name));

            RuleFor(x => x.Description)
                .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters.")
                .When(x => !string.IsNullOrWhiteSpace(x.Description));

            RuleFor(x => x.Sku)
                .MaximumLength(64).WithMessage("Sku must not exceed 64 characters.")
                .Matches("^[a-zA-Z0-9-_]+$").WithMessage("Sku may only contain letters, numbers, hyphens, and underscores.")
                .When(x => !string.IsNullOrWhiteSpace(x.Sku));

            RuleFor(x => x.Price)
                .GreaterThan(0m).WithMessage("Price must be greater than 0.")
                .When(x => x.Price is not null);

            RuleFor(x => x)
                .Must(x =>
                    x.CategoryId is not null
                    || !string.IsNullOrWhiteSpace(x.Name)
                    || !string.IsNullOrWhiteSpace(x.Description)
                    || !string.IsNullOrWhiteSpace(x.Sku)
                    || x.Price is not null
                    || x.IsActive is not null)
                .WithMessage("At least one field must be provided.");


        }
    }
}
