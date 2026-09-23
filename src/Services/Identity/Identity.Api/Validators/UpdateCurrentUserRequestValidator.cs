using FluentValidation;
using Identity.Api.DTOs;

namespace Identity.Api.Validatiors
{
    public sealed class UpdateCurrentUserRequestValidator : AbstractValidator<UpdateCurrentUserRequest>
    {
        public UpdateCurrentUserRequestValidator()
        {
            RuleFor(x => x.Email).ValidEmail();
            RuleFor(x => x.Password).ComplexPassword();
        }
    }
}
