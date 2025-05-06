using Sekiban.Pure.Exceptions;

public class ExceptionEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            var result = await next(context);

            return result;
        }
        // catch (AuthenticationException)
        // {
        //     return Results.Unauthorized();
        // }
        // catch (UnauthorizedAccessException)
        // {
        //     return Results.Forbid();
        // }
        // catch (DataNotFoundException)
        // {
        //     return Results.NotFound();
        // }
        catch (SekibanValidationErrorsException vex)
        {
            return CreateValidationProblemResult(vex);
        }
        // catch (ValidationError verr)
        // {
        //     return CreateValidationProblemResult(
        //         new SekibanValidationErrorsException([new ValidationResult(verr.Message, [verr.PropertyName])])
        //     );
        // }
        catch (Exception ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: ex.GetType().FullName,
                detail: ex.Message
            );
        }
    }

    public static IResult CreateValidationProblemResult(SekibanValidationErrorsException exception) =>
        Results.ValidationProblem(new Dictionary<string, string[]>(
            exception.Errors.Select(s => new KeyValuePair<string, string[]>(s.PropertyName, s.ErrorMessages.ToArray()))
        ));
}