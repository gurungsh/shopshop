using FluentValidation;

namespace Ordering.Api.Filters
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

            var argumentToValidate = context.Arguments.OfType<T>().FirstOrDefault();
            if (argumentToValidate is null)
            {
                return Results.BadRequest(new { error = "Request body is required." });
            }

            var validationResult = await _validator.ValidateAsync(argumentToValidate, context.HttpContext.RequestAborted);
            if (!validationResult.IsValid)
            {
                var errors = validationResult.ToDictionary();
                return Results.ValidationProblem(errors);
            }

            return await next(context);
        }
    }
}
