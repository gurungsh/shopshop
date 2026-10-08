using BuildingBlocks.Core;
using FluentValidation;
using Ordering.Api.DTOs;

namespace Ordering.Api.Validators;

public sealed class OrderQueryValidator : AbstractValidator<OrderQuery>
{
    public OrderQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PagingDefaults.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {PagingDefaults.MaxPageSize}.");
    }
}
