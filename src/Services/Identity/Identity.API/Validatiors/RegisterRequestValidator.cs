using FluentValidation;
using Identity.API.Constants;
using Identity.API.DTOs;

namespace Identity.API.Validatiors
{
    public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
    {
        public RegisterRequestValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email address is required.")
                .EmailAddress().WithMessage("A valid email address is required.");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required.")
                .MinimumLength(8).WithMessage("Password must be at least 8 characters long.")
                .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
                .Matches(@"[a-z]").WithMessage("Password must contain at least one lowercase letter.")
                .Matches(@"[0-9]").WithMessage("Password must contain at least one numeric digit.")
                .Matches(@"[\^$*.\[\]{}()?\-!@#%&/\\,><':;|_~`]").WithMessage("Password must contain at least one special character.");

            RuleFor(x=>x.Role)
                .Must(role => string.IsNullOrEmpty(role) 
                || role.Equals(Roles.Admin, StringComparison.OrdinalIgnoreCase)
                || role.Equals(Roles.Customer, StringComparison.OrdinalIgnoreCase))
                .WithMessage("Invalid role specified.");
        }
    }
}
