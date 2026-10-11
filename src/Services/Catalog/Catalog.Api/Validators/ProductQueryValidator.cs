using BuildingBlocks.Core;
using Catalog.Api.DTOs;
using FluentValidation;

namespace Catalog.Api.Validators;

public sealed class ProductQueryValidator : AbstractValidator<ProductQuery>
{
    public ProductQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PagingDefaults.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {PagingDefaults.MaxPageSize}.");

        RuleFor(x => x.MaxPrice)
            .GreaterThanOrEqualTo(x => x.MinPrice)
            .When(x => x.MinPrice.HasValue && x.MaxPrice.HasValue)
            .WithMessage("MaxPrice must be greater than or equal to MinPrice.");
    }
}
