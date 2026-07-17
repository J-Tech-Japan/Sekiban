using ResultBoxes;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Activation-local coordinator that serialises EVERY external derived-snapshot mutation
///     (<c>IMultiProjectionStateStore</c> upsert and the reset's delete) through one gate, and rejects a snapshot
///     upsert while the projection is faulted.
///     It exists because catch-up runs on an <c>Interleave = true</c> grain timer, so a snapshot upsert can be issued
///     concurrently with a reset. Routing both through this gate makes the reset's delete WAIT for any parked/in-flight
///     upsert to finish before it deletes, and stops a later upsert from running concurrently with (or racing past) the
///     delete — so no stale upsert can recreate the data a reset invalidates. The fault predicate additionally blocks
///     any upsert while a live or committed fault exists, so a faulted projection persists no derived state at all.
///     It is framework-free so a friend test can drive the ordering directly (parked upsert vs delete) without Orleans.
/// </summary>
internal sealed class ExternalStoreCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<bool> _faultBlocksUpsert;

    public ExternalStoreCoordinator(Func<bool> faultBlocksUpsert) =>
        _faultBlocksUpsert = faultBlocksUpsert ?? throw new ArgumentNullException(nameof(faultBlocksUpsert));

    /// <summary>
    ///     Runs <paramref name="upsert" /> under the coordinator, but only when no fault blocks it. Returns a
    ///     not-persisted result (<c>false</c>) without invoking <paramref name="upsert" /> while faulted — a skip, not
    ///     an error.
    /// </summary>
    public async Task<ResultBox<bool>> UpsertAsync(Func<Task<ResultBox<bool>>> upsert)
    {
        ArgumentNullException.ThrowIfNull(upsert);
        await _gate.WaitAsync();
        try
        {
            if (_faultBlocksUpsert())
            {
                return ResultBox.FromValue(false);
            }

            return await upsert();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Runs a snapshot invalidation/delete under the coordinator. It blocks until any parked/in-flight upsert has
    ///     released the gate, so the delete always runs AFTER an outstanding upsert commits, never concurrently.
    /// </summary>
    public async Task InvalidateAsync(Func<Task> delete)
    {
        ArgumentNullException.ThrowIfNull(delete);
        await _gate.WaitAsync();
        try
        {
            await delete();
        }
        finally
        {
            _gate.Release();
        }
    }
}
