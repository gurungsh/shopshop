using BuildingBlocks.Core;
using Catalog.Api.DTOs;
using FluentValidation;

namespace Catalog.Api.Validators
{
    public sealed class CategoryQueryValidator : AbstractValidator<CategoryQuery>
    {
        public CategoryQueryValidator()
        {
            RuleFor(x => x.Page)
                .GreaterThanOrEqualTo(1).WithMessage("Page must be at least 1.");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, PagingDefaults.MaxPageSize)
                .WithMessage($"Page size must be between 1 and {PagingDefaults.MaxPageSize}.");
        }
    }
}
