using BuildingBlocks.Core;
using FluentValidation;
using Identity.Api.DTOs;

namespace Identity.Api.Validators
{
    public sealed class UserQueryValidator : AbstractValidator<UserQuery>
    {
        public UserQueryValidator()
        {
            RuleFor(x => x.Page)
                .GreaterThanOrEqualTo(1).WithMessage("Page must be at least 1.");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, PagingDefaults.MaxPageSize)
                .WithMessage($"Page size must be between 1 and {PagingDefaults.MaxPageSize}.");
        }
    }
}
