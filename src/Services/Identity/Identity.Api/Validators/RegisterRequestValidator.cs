using FluentValidation;
using Identity.Api.DTOs;

namespace Identity.Api.Validatiors
{
    public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
    {
        public RegisterRequestValidator()
        {
            RuleFor(x => x.Email).ValidEmail();
            RuleFor(x => x.Password).ComplexPassword();
        }
    }
}
