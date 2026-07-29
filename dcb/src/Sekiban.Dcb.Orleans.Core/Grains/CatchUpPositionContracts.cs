using Sekiban.Dcb.Common;

namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     The start checkpoint for one catch-up run. A restored checkpoint is leased once and is never replaced by
///     host-payload inference while it is pending.
/// </summary>
internal sealed record CatchUpStartPositionLease(
    SortableUniqueId? StartPosition,
    CatchUpStartPositionSource Source);

internal enum CatchUpStartPositionSource
{
    InferredCheckpoint,
    RestoredCheckpoint,
    FullReplay
}

/// <summary>
///     Single owner of the pending restored checkpoint position. Both timer and in-call catch-up paths acquire their
///     start through this resolver so the restored record remains authoritative and is consumed exactly once.
/// </summary>
internal sealed class CatchUpStartPositionLeaseResolver
{
    private readonly object _sync = new();
    private SortableUniqueId? _pendingRestoredPosition;

    internal void Restore(SortableUniqueId? position)
    {
        lock (_sync)
        {
            _pendingRestoredPosition = position;
        }
    }

    internal void Clear()
    {
        lock (_sync)
        {
            _pendingRestoredPosition = null;
        }
    }

    internal async Task<CatchUpStartPositionLease> AcquireAsync(
        bool forceFullReplay,
        Func<Task<SortableUniqueId?>> inferCheckpointAsync)
    {
        if (forceFullReplay)
        {
            Clear();
            return new CatchUpStartPositionLease(null, CatchUpStartPositionSource.FullReplay);
        }

        lock (_sync)
        {
            if (_pendingRestoredPosition is { } restored)
            {
                _pendingRestoredPosition = null;
                return new CatchUpStartPositionLease(restored, CatchUpStartPositionSource.RestoredCheckpoint);
            }
        }

        return new CatchUpStartPositionLease(
            await inferCheckpointAsync(),
            CatchUpStartPositionSource.InferredCheckpoint);
    }
}

/// <summary>
///     Invocation-owned evidence returned by one in-call refresh. The reached cursor is derived only from reads
///     performed by that invocation; it is never inferred from the shared timer progress object.
/// </summary>
internal sealed record CatchUpInvocationResult(
    CatchUpStartPositionLease Start,
    SortableUniqueId? AuthoritativeReachedPosition);

internal readonly record struct CatchUpBatchResult(
    int ProcessedCount,
    SortableUniqueId? AuthoritativeReadCursor);

/// <summary>
///     Activation-local single-writer gate for timer and in-call runs.
/// </summary>
internal sealed class CatchUpRunExecutionGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal async ValueTask<IAsyncDisposable> EnterAsync()
    {
        await _gate.WaitAsync();
        return new Lease(_gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IAsyncDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _gate, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
