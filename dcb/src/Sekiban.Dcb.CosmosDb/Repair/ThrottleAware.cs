using Microsoft.Azure.Cosmos;
using System.Net;
namespace Sekiban.Dcb.CosmosDb.Repair;

/// <summary>
///     Waits out Cosmos throttling. A repair scan is a bulk reader competing with live traffic, so being
///     told to slow down is the normal case, not an error.
///     The server's Retry-After is honored in full — retrying earlier than it asked only earns another 429.
///     Only 429s are retried here; any other failure is the caller's to handle.
/// </summary>
internal static class ThrottleAware
{
    private static readonly TimeSpan FallbackDelay = TimeSpan.FromMilliseconds(500);

    public static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        int maxRetries,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? wait = null)
    {
        var attempts = Math.Max(0, maxRetries);
        wait ??= (delay, ct) => Task.Delay(delay, ct);

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests && attempt < attempts)
            {
                var delay = ex.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero
                    ? retryAfter
                    : FallbackDelay;

                await wait(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
