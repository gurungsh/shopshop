using Common.Core;
using FluentValidation;
using Identity.Api.DTOs;

namespace Identity.Api.Validatiors
{
    public sealed class AdminUpdateUserRequestValidator : AbstractValidator<AdminUpdateUserRequest>
    {
        public AdminUpdateUserRequestValidator()
        {
            RuleFor(x => x.Email).ValidEmail();
            RuleFor(x => x.Role)
                .NotEmpty().WithMessage("Role is required.")
                .Must(role => role == Roles.Admin || role == Roles.Customer)
                .WithMessage("Role must be either Admin or Customer.");
        }
    }
}
