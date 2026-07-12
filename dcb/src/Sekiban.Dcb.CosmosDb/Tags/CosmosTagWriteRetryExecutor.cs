using Microsoft.Azure.Cosmos;
using Sekiban.Dcb.Tags;
using System.Net;
namespace Sekiban.Dcb.CosmosDb.Tags;

/// <summary>
///     Clock, delay and jitter, behind a seam so retry behavior can be asserted without real waiting.
/// </summary>
internal interface ICosmosRetryScheduler
{
    DateTimeOffset UtcNow { get; }

    /// <summary>Waits for <paramref name="delay" />.</summary>
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);

    /// <summary>Returns a value in [0, 1) used to jitter a backoff.</summary>
    double NextJitter();
}

/// <summary>
///     The production scheduler: real clock, real delay, real randomness.
/// </summary>
internal sealed class SystemCosmosRetryScheduler : ICosmosRetryScheduler
{
    public static readonly SystemCosmosRetryScheduler Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);

    // Jitter only spreads retries apart; it guards nothing, so a fast PRNG is the right tool.
#pragma warning disable CA5394
    public double NextJitter() => Random.Shared.NextDouble();
#pragma warning restore CA5394
}

/// <summary>
///     Retries the tag-write stage until it succeeds, the attempts run out, or the deadline passes.
///     Retrying is only safe because the stage is idempotent: tag rows derive deterministically from the
///     events, so a re-execution re-derives the identical rows, accepts the ones a partial write already
///     created, and fills in the ones it did not (see <see cref="CosmosTagWriteStage" />). Events are never
///     deleted — a failure that outlives the retries surfaces as
///     <see cref="CosmosTagWriteExhaustedException" /> naming the events left with an incomplete index.
///     A <see cref="CosmosTagIndexCorruptionException" /> is never retried: the same content is derived on
///     every attempt, so it would fail identically forever.
/// </summary>
internal static class CosmosTagWriteRetryExecutor
{
    public static async Task<List<TagWriteResult>> ExecuteAsync(
        Func<Task<List<TagWriteResult>>> writeTags,
        IReadOnlyList<Guid> eventIds,
        CosmosTagWriteRetryOptions retryOptions,
        ICosmosRetryScheduler scheduler,
        Action<int, TimeSpan, Exception>? onRetry,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, retryOptions.MaxAttempts);
        var startedAt = scheduler.UtcNow;
        var attempt = 0;
        Exception lastException;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            try
            {
                var results = await writeTags().ConfigureAwait(false);

                if (attempt > 1)
                {
                    CosmosDbTelemetry.RecordTagWriteRetryOutcome(TagWriteRetryOutcome.Recovered);
                }

                return results;
            }
            catch (CosmosTagIndexCorruptionException)
            {
                // Not retryable: every attempt derives the same content and hits the same mismatch.
                CosmosDbTelemetry.RecordTagWriteFailure(TagWriteFailureReason.Corruption);
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is CosmosException or InvalidOperationException or TimeoutException or IOException)
            {
                CosmosDbTelemetry.RecordTagWriteFailure(TagWriteFailureReason.Transient);
                lastException = ex;
            }

            var delay = ComputeDelay(attempt, retryOptions, scheduler, lastException);
            var elapsed = scheduler.UtcNow - startedAt;
            var deadlineExceeded = retryOptions.MaxTotalDuration > TimeSpan.Zero &&
                elapsed + delay >= retryOptions.MaxTotalDuration;

            if (attempt >= maxAttempts || deadlineExceeded)
            {
                CosmosDbTelemetry.RecordTagWriteRetryOutcome(TagWriteRetryOutcome.Exhausted);
                throw new CosmosTagWriteExhaustedException(eventIds, attempt, lastException);
            }

            CosmosDbTelemetry.RecordTagWriteRetry();
            onRetry?.Invoke(attempt, delay, lastException);

            await scheduler.WaitAsync(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Exponential backoff with jitter. When Cosmos throttles and tells us how long to wait, that hint
    ///     wins — it knows better than our curve does.
    /// </summary>
    private static TimeSpan ComputeDelay(
        int attempt,
        CosmosTagWriteRetryOptions retryOptions,
        ICosmosRetryScheduler scheduler,
        Exception exception)
    {
        if (exception is CosmosException { StatusCode: HttpStatusCode.TooManyRequests } throttled &&
            throttled.RetryAfter is { } retryAfter &&
            retryAfter > TimeSpan.Zero)
        {
            return Cap(retryAfter, retryOptions.MaxBackoff);
        }

        var initial = retryOptions.InitialBackoff > TimeSpan.Zero
            ? retryOptions.InitialBackoff
            : TimeSpan.Zero;
        if (initial == TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        // attempt is 1-based: the wait before the first retry is InitialBackoff itself.
        var exponent = Math.Min(attempt - 1, 30);
        var backoffTicks = initial.Ticks * Math.Pow(2, exponent);
        var capped = Cap(TimeSpan.FromTicks((long)Math.Min(backoffTicks, TimeSpan.MaxValue.Ticks)), retryOptions.MaxBackoff);

        var jitterRatio = Math.Clamp(retryOptions.JitterRatio, 0, 1);
        if (jitterRatio == 0)
        {
            return capped;
        }

        // Draw from [(1 - jitterRatio) * backoff, backoff], so concurrent writers spread out.
        var factor = 1 - (jitterRatio * scheduler.NextJitter());
        return TimeSpan.FromTicks((long)(capped.Ticks * factor));
    }

    private static TimeSpan Cap(TimeSpan value, TimeSpan max) =>
        max > TimeSpan.Zero && value > max ? max : value;
}
