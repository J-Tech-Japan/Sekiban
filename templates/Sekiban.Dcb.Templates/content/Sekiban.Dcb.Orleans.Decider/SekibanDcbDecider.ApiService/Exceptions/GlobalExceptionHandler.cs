using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace SekibanDcbDecider.ApiService.Exceptions;

/// <summary>
/// Global exception handler that maps exceptions to appropriate HTTP responses
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problemDetails = exception switch
        {
            NotFoundException notFound => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Resource Not Found",
                Detail = notFound.Message,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                Extensions =
                {
                    ["resourceType"] = notFound.ResourceType,
                    ["resourceId"] = notFound.ResourceId
                }
            },

            ValidationException validation => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation Error",
                Detail = validation.Message,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                Extensions =
                {
                    ["errors"] = validation.Errors ?? new Dictionary<string, string[]>()
                }
            },

            ConflictException conflict => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = conflict.Message,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8"
            },

            UnauthorizedException unauthorized => new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = unauthorized.Message,
                Type = "https://tools.ietf.org/html/rfc7235#section-3.1"
            },

            ApplicationException appEx => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad Request",
                Detail = appEx.Message,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1"
            },

            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while processing your request.",
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1"
            }
        };

        // Log the exception
        LogException(exception, problemDetails.Status!.Value);

        // Set response status code
        httpContext.Response.StatusCode = problemDetails.Status.Value;

        // Write ProblemDetails response
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true; // Exception was handled
    }

    private void LogException(Exception exception, int statusCode)
    {
        var logLevel = statusCode switch
        {
            >= 500 => LogLevel.Error,
            >= 400 => LogLevel.Warning,
            _ => LogLevel.Information
        };

        _logger.Log(
            logLevel,
            exception,
            "Exception occurred: {ExceptionType} - {Message}",
            exception.GetType().Name,
            exception.Message);
    }
}
