namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Stable, distinguishable failure returned by <see cref="ExternalStoreCoordinator.UpsertAsync" /> when a snapshot
///     upsert is rejected because the projection is faulted. It is surfaced as a failed
///     <see cref="PersistAttempt" /> (NOT a success carrying <c>false</c>) so every caller that only inspects
///     <c>IsSuccess</c> takes the not-persisted branch: no caller may report the snapshot as saved or advance persisted
///     metadata after this rejection. Callers that want to log the rejection as a benign skip (rather than a store
///     failure) can match on this type.
/// </summary>
public sealed class ExternalPersistenceBlockedByFaultException : Exception
{
    public ExternalPersistenceBlockedByFaultException() : base(
        "External derived-snapshot persistence is blocked because the projection is faulted (a live or committed fault exists).")
    {
    }
}

/// <summary>
///     The outcome of a persist step (an external derived-snapshot write, or a whole persist attempt): completed with a
///     value (<c>true</c>, or <c>false</c> when deliberately not written), or failed (<see cref="Failure" />). The persist
///     path logs such a failure and continues from it, so it is a value here; the public grain member that reports it
///     faults its task with the same exception.
/// </summary>
internal readonly record struct PersistAttempt(bool Value, Exception? Failure)
{
    public bool IsSuccess => Failure is null;

    public static PersistAttempt Completed(bool value) => new(value, null);

    public static PersistAttempt Failed(Exception failure) => new(false, failure);
}

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
    ///     Runs <paramref name="upsert" /> under the coordinator, but only when no fault blocks it. When faulted it
    ///     returns a failed outcome carrying <see cref="ExternalPersistenceBlockedByFaultException" /> WITHOUT invoking
    ///     <paramref name="upsert" /> — an explicit failure, never a success carrying <c>false</c>, so a caller
    ///     inspecting only <c>IsSuccess</c> cannot mistake the rejection for a completed save.
    /// </summary>
    public async Task<PersistAttempt> UpsertAsync(Func<Task<PersistAttempt>> upsert)
    {
        ArgumentNullException.ThrowIfNull(upsert);
        await _gate.WaitAsync();
        try
        {
            if (_faultBlocksUpsert())
            {
                return PersistAttempt.Failed(new ExternalPersistenceBlockedByFaultException());
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
