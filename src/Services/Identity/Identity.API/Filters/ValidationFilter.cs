using FluentValidation;

namespace Identity.API.Filters
{
    public class ValidationFilter<T> : IEndpointFilter where T : class
    {
        private readonly IValidator<T>? _validator;

        public ValidationFilter(IValidator<T>? validator = null)
        {
            _validator = validator;
        }

        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            if (_validator is null)
            {
                return await next(context);
            }

            var arguementToValidate = context.Arguments.OfType<T>().FirstOrDefault();
            if (arguementToValidate is null)
            {
                return Results.BadRequest(new { error = "Request body is required." });
            }

            var validationResult = await _validator.ValidateAsync(arguementToValidate, context.HttpContext.RequestAborted);
            if (!validationResult.IsValid)
            {
                var errors = validationResult.ToDictionary();
                return Results.ValidationProblem(errors);
            }

            return await next(context);
        }
    }
}
