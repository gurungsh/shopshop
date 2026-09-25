using Catalog.Api.DTOs;
using FluentValidation;

namespace Catalog.Api.Validators
{
    public sealed class AdminCreateCategoryRequestValidator : AbstractValidator<AdminCreateCategoryRequest>
    {
        public AdminCreateCategoryRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name is required.")
                .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");

            RuleFor(x => x.Description)
                .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters.")
                .When(x => x.Description is not null);
        }
    }

}
