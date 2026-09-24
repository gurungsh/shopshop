using Catalog.Api.DTOs;
using FluentValidation;

namespace Catalog.Api.Validators
{
    public sealed class AdminUpdateCategoryRequestValidator : AbstractValidator<AdminUpdateCategoryRequest>
    {
        public AdminUpdateCategoryRequestValidator()
        {
            RuleFor(x => x.Name)
                .MaximumLength(200).WithMessage("Name must not exceed 200 characters.")
                .When(x => !string.IsNullOrWhiteSpace(x.Name));

            RuleFor(x => x.Description)
                .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters.")
                .When(x => !string.IsNullOrWhiteSpace(x.Description));

            RuleFor(x => x)
                .Must(x =>
                    !string.IsNullOrWhiteSpace(x.Name)
                    || !string.IsNullOrWhiteSpace(x.Description)
                    || x.IsActive is not null)
                .WithMessage("At least one field must be provided.");
        }

    }
}
