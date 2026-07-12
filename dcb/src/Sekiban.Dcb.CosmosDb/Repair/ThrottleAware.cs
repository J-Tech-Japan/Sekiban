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
        var retries = Math.Max(0, maxRetries);
        wait ??= (delay, ct) => Task.Delay(delay, ct);

        // The first call is not a retry, so the bound is retries + 1 attempts. The final attempt does not
        // catch a 429 — it lets it out, which is what ends the loop.
        for (var attempt = 0; attempt <= retries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests && attempt < retries)
            {
                var delay = ex.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero
                    ? retryAfter
                    : FallbackDelay;

                await wait(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException(
            "Unreachable: the final attempt either returns its result or lets its exception out.");
    }
}
