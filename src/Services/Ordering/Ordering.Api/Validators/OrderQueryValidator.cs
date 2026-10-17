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

        RuleFor(x => x.MaxTotalAmount)
            .GreaterThanOrEqualTo(x => x.MinTotalAmount)
            .When(x => x.MinTotalAmount.HasValue && x.MaxTotalAmount.HasValue)
            .WithMessage("MaxTotalAmount must be greater than or equal to MinTotalAmount.");

        RuleFor(x => x.MaxItemCount)
            .GreaterThanOrEqualTo(x => x.MinItemCount)
            .When(x => x.MinItemCount.HasValue && x.MaxItemCount.HasValue)
            .WithMessage("MaxItemCount must be greater than or equal to MinItemCount.");

        RuleFor(x => x.CreatedAfterUtc)
            .LessThanOrEqualTo(x => x.CreatedBeforeUtc)
            .When(x => x.CreatedAfterUtc.HasValue && x.CreatedBeforeUtc.HasValue)
            .WithMessage("CreatedAfterUtc must be on or before CreatedBeforeUtc.");

        RuleFor(x => x.UpdatedAfterUtc)
            .LessThanOrEqualTo(x => x.UpdatedBeforeUtc)
            .When(x => x.UpdatedAfterUtc.HasValue && x.UpdatedBeforeUtc.HasValue)
            .WithMessage("UpdatedAfterUtc must be on or before UpdatedBeforeUtc.");
    }
}
