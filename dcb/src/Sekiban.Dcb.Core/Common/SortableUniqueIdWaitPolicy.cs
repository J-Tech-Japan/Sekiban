using Sekiban.Dcb.Queries;

namespace Sekiban.Dcb.Common;

/// <summary>Internal result of one shared sortable-unique-id wait.</summary>
internal sealed record SortableUniqueIdWaitResult(
    bool Received,
    TimeSpan Timeout,
    TimeSpan Elapsed,
    string? LastObservedSortableUniqueId)
{
    internal bool TimedOut => !Received;
}

/// <summary>
///     The single wait implementation used by the Orleans query facades and strict InMemory queries. The clock,
///     delay, probe, and diagnostic delegates are deliberately injectable internally so timing tests never sleep.
/// </summary>
internal sealed class SortableUniqueIdWaitPolicy
{
    internal static SortableUniqueIdWaitPolicy System { get; } = new(TimeProvider.System);

    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    internal SortableUniqueIdWaitPolicy(
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _delayAsync = delayAsync ?? ((delay, cancellationToken) =>
            Task.Delay(delay, _timeProvider, cancellationToken));
    }

    internal async Task<SortableUniqueIdWaitResult> WaitAsync(
        string targetSortableUniqueId,
        SortableUniqueIdWaitSurface surface,
        SortableUniqueIdWaitMode mode,
        Func<CancellationToken, Task<bool>> probeAsync,
        Func<CancellationToken, Task<string?>>? lastObservedAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetSortableUniqueId);
        ArgumentNullException.ThrowIfNull(probeAsync);

        var timeout = TimeSpan.FromMilliseconds(
            SortableUniqueIdWaitHelper.CalculateAdaptiveTimeout(targetSortableUniqueId, _timeProvider));
        var startTimestamp = _timeProvider.GetTimestamp();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
            if (elapsed >= timeout)
            {
                break;
            }

            if (await probeAsync(cancellationToken).ConfigureAwait(false))
            {
                elapsed = _timeProvider.GetElapsedTime(startTimestamp);
                SortableUniqueIdWaitTelemetry.RecordWait(
                    surface,
                    mode,
                    SortableUniqueIdWaitOutcome.Arrived,
                    elapsed);
                return new SortableUniqueIdWaitResult(true, timeout, elapsed, null);
            }

            elapsed = _timeProvider.GetElapsedTime(startTimestamp);
            var remaining = timeout - elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await _delayAsync(
                    remaining < TimeSpan.FromMilliseconds(SortableUniqueIdWaitHelper.DefaultPollingIntervalMs)
                        ? remaining
                        : TimeSpan.FromMilliseconds(SortableUniqueIdWaitHelper.DefaultPollingIntervalMs),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var timeoutElapsed = _timeProvider.GetElapsedTime(startTimestamp);
        string? lastObserved = null;
        if (mode == SortableUniqueIdWaitMode.Strict && lastObservedAsync is not null)
        {
            try
            {
                lastObserved = await lastObservedAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // The diagnostic read is best effort. The timeout is the real outcome and must not be masked.
            }
        }

        SortableUniqueIdWaitTelemetry.RecordWait(
            surface,
            mode,
            SortableUniqueIdWaitOutcome.TimedOut,
            timeoutElapsed);
        return new SortableUniqueIdWaitResult(false, timeout, timeoutElapsed, lastObserved);
    }
}
