using Microsoft.AspNetCore.Diagnostics;
using Sekiban.Dcb.Orleans.Grains;

namespace SekibanDcbOrleans.ApiService;

public static class ProjectionErrors
{
    public static bool IsRetryable(Exception? error) => error != null &&
        (error.Message.Contains(MultiProjectionQueryFailClosedMessages.CatchUpInProgressPrefix, StringComparison.Ordinal) ||
         error.Message.Contains(MultiProjectionQueryFailClosedMessages.RebuildPendingPrefix, StringComparison.Ordinal));

    // Preserve the caller's existing response for every unrelated failure.
    public static IResult Map(Exception? error, IResult fallback) =>
        IsRetryable(error) ? new RetryableResult(error!) : fallback;

    private sealed class RetryableResult(Exception error) : IResult
    {
        public Task ExecuteAsync(HttpContext context)
        {
            context.Response.Headers["Retry-After"] = "2";
            return Results.Problem(statusCode: 503, detail: error.Message).ExecuteAsync(context);
        }
    }
}

public sealed class RetryableProjectionExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception error, CancellationToken cancellationToken)
    {
        if (!ProjectionErrors.IsRetryable(error)) return false;
        await ProjectionErrors.Map(error, Results.Empty).ExecuteAsync(context);
        return true;
    }
}
