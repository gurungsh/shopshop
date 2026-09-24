using Catalog.Api.DTOs;
using FluentValidation;

namespace Catalog.Api.Validators
{
    public sealed class AdminCreateProductRequestValidator : AbstractValidator<AdminCreateProductRequest>
    {
        public AdminCreateProductRequestValidator()
        {
            RuleFor(x => x.CategoryId)
                .NotEmpty().WithMessage("CategoryId is required.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name is required.")
                .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");

            RuleFor(x => x.Description)
                .NotEmpty().WithMessage("Description is required.")
                .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters.");

            RuleFor(x => x.Sku)
                .NotEmpty().WithMessage("Sku is required.")
                .MaximumLength(64).WithMessage("Sku must not exceed 64 characters.")
                .Matches("^[a-zA-Z0-9-_]+$").WithMessage("Sku may only contain letters, numbers, hyphens, and underscores.");

            RuleFor(x => x.Price)
                .GreaterThan(0m).WithMessage("Price must be greater than 0.");

        }
    }
