using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace SekibanDcbOrleans.ApiService;

public sealed class BadRequestExceptionHandler(ILogger<BadRequestExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest) return false;
        logger.LogWarning(exception, "Invalid request body");
        context.Response.StatusCode = badRequest.StatusCode;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        { Status = badRequest.StatusCode, Title = "Bad Request", Detail = badRequest.Message }, cancellationToken);
        return true;
    }
}
