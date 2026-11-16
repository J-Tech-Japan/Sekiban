namespace Sekiban.Dcb.Common;

/// <summary>
///     Awaits a task the caller has already started and returns its failure as a value. Used only where a failed read or
///     write is an expected outcome (an unknown position, a skipped save, a retry later) rather than an error to
///     propagate. A call that throws before it returns its task is not captured: it propagates, as it did when these
///     calls returned a result object.
/// </summary>
internal static class TaskFailureCapture
{
    /// <summary>Returns the task's value, or its exception (cancellation included) as <c>Failure</c>.</summary>
    internal static async Task<(T Value, Exception? Failure)> CaptureAsync<T>(Task<T> task)
    {
        try
        {
            return (await task.ConfigureAwait(false), null);
        }
        catch (Exception ex)
        {
            return (default!, ex);
        }
    }

    /// <summary>
    ///     Like <see cref="CaptureAsync{T}" />, but <see cref="OperationCanceledException" /> propagates instead of being
    ///     returned. Used for streaming reads whose contract always propagated cancellation.
    /// </summary>
    internal static async Task<(T Value, Exception? Failure)> CapturePropagatingCancellationAsync<T>(Task<T> task)
    {
        try
        {
            return (await task.ConfigureAwait(false), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (default!, ex);
        }
    }
}
