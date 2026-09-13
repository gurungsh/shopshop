namespace Identity.API.Common;

public static class EndpointResults
{
    public static IResult ToHttpResult<T>(Result<T> result) =>
        result.ErrorType switch
        {
            ResultErrorType.BadRequest =>
                Results.BadRequest(new { error = result.Error }),

            ResultErrorType.Unauthorized =>
                Results.Unauthorized(),

            ResultErrorType.Conflict =>
                Results.Conflict(new { error = result.Error }),

            ResultErrorType.NotFound =>
                Results.NotFound(new { error = result.Error }),

            _ =>
                Results.Problem(result.Error)
        };
}
