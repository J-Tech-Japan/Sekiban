using Orleans.Runtime;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>The category of a coordinated state write, which decides whether it is authorized while the projection is faulted.</summary>
internal enum GrainStateWriteKind
{
    /// <summary>A normal/streaming snapshot checkpoint. Rejected while a fault exists — a faulted projection makes no checkpoint progress.</summary>
    Checkpoint,

    /// <summary>Persisting (or retrying) the projection fault descriptor. Always allowed.</summary>
    FaultDescriptor,

    /// <summary>An operator rebuild/reset that clears the fault and re-establishes a clean baseline. Always allowed.</summary>
    OperatorReset,

    /// <summary>Maintenance of auxiliary/monitoring metadata (version, integrity-guard). Always allowed.</summary>
    MetadataMaintenance
}

/// <summary>The result of a coordinated write.</summary>
internal enum GrainStateWriteOutcome
{
    /// <summary>The write mutated a candidate and committed it.</summary>
    Committed,

    /// <summary>A checkpoint was suppressed because the projection is faulted; nothing was written.</summary>
    RejectedFaulted
}

/// <summary>
///     A true immutable snapshot of the committed grain state — a copy of every scalar/string field, NOT the mutable
///     <see cref="MultiProjectionGrainState" />. Handed out by <see cref="CoordinatedGrainStateStore.Committed" /> so a
///     consumer cannot downcast to the persisted payload or obtain any mutable reference.
/// </summary>
internal sealed record MultiProjectionGrainStateSnapshot(
    string ProjectorName,
    string? ProjectorVersion,
    string? LastSortableUniqueId,
    long EventsProcessed,
    DateTime LastPersistTime,
    string? SerializedState,
    long StateSize,
    string? SafeLastPosition,
    string? LastPosition,
    int LastGoodSafeVersion,
    long LastGoodPayloadBytes,
    long LastGoodOriginalSizeBytes,
    long LastGoodEventsProcessed,
    string? FaultEventId,
    string? FaultEventType,
    string? FaultPosition,
    string? FaultMessage,
    long FaultedAtUtcTicks) : IReadOnlyMultiProjectionGrainState
{
    public static MultiProjectionGrainStateSnapshot From(MultiProjectionGrainState s) =>
        new(
            s.ProjectorName,
            s.ProjectorVersion,
            s.LastSortableUniqueId,
            s.EventsProcessed,
            s.LastPersistTime,
            s.SerializedState,
            s.StateSize,
            s.SafeLastPosition,
            s.LastPosition,
            s.LastGoodSafeVersion,
            s.LastGoodPayloadBytes,
            s.LastGoodOriginalSizeBytes,
            s.LastGoodEventsProcessed,
            s.FaultEventId,
            s.FaultEventType,
            s.FaultPosition,
            s.FaultMessage,
            s.FaultedAtUtcTicks);
}

/// <summary>
///     Sole owner of the grain's raw <see cref="IPersistentState{TState}" />. The grain hands its Orleans-injected
///     persistent state here in the constructor and keeps no direct reference, so nothing but this store can call
///     <c>WriteStateAsync</c> — enforced by TYPE OWNERSHIP. Reads are exposed only as an immutable snapshot
///     (<see cref="MultiProjectionGrainStateSnapshot" />) of the last committed state; the mutable payload never
///     escapes and cannot be reached by a downcast.
///     Writes are copy-on-write under a single-writer gate: a write clones the last committed state, applies its
///     mutation to the CANDIDATE, writes the candidate, and publishes it as the committed state only after a successful
///     <c>WriteStateAsync</c>. An optional <c>onCommitted</c> continuation runs only after that publish, so a caller's
///     live side effects (e.g. clearing the live actor fault) never happen before the persisted state commits. If the
///     write fails, the candidate is discarded, the committed state (and provider payload) is restored, and
///     <c>onCommitted</c> is NOT invoked. A <see cref="GrainStateWriteKind.Checkpoint" /> is rejected when a fault
///     exists on the committed state OR live on the actor (fault persistence may be retrying).
/// </summary>
internal sealed class CoordinatedGrainStateStore
{
    private readonly IPersistentState<MultiProjectionGrainState> _state;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<bool> _liveFaultActive;
    private MultiProjectionGrainState _committed;
    private MultiProjectionGrainStateSnapshot _committedSnapshot;

    public CoordinatedGrainStateStore(
        IPersistentState<MultiProjectionGrainState> state,
        Func<bool> liveFaultActive)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _liveFaultActive = liveFaultActive ?? throw new ArgumentNullException(nameof(liveFaultActive));
        _committed = _state.State ?? new MultiProjectionGrainState();
        _committedSnapshot = MultiProjectionGrainStateSnapshot.From(_committed);
    }

    /// <summary>The last committed persisted state, as an immutable snapshot. Never the mutable payload or the candidate.</summary>
    public IReadOnlyMultiProjectionGrainState Committed => _committedSnapshot;

    public bool RecordExists => _state.RecordExists;

    /// <summary>
    ///     Adopts the provider's current payload as the committed baseline. Called after Orleans has populated the
    ///     persistent state on activation (and after a re-read), so committed reads reflect what storage holds.
    /// </summary>
    public void AdoptProviderStateAsCommitted()
    {
        _committed = _state.State ?? new MultiProjectionGrainState();
        _committedSnapshot = MultiProjectionGrainStateSnapshot.From(_committed);
    }

    /// <summary>
    ///     Applies <paramref name="mutate" /> to a CLONE of the committed state and writes it under the single-writer
    ///     gate, publishing the clone as committed only on success and then running <paramref name="onCommitted" />. A
    ///     <see cref="GrainStateWriteKind.Checkpoint" /> is rejected (no write) when the projection is faulted.
    /// </summary>
    public async Task<GrainStateWriteOutcome> ExecuteWriteAsync(
        GrainStateWriteKind kind,
        Action<MultiProjectionGrainState> mutate,
        Action? onCommitted = null)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        await _gate.WaitAsync();
        try
        {
            if (kind == GrainStateWriteKind.Checkpoint && (CommittedFaultExists() || _liveFaultActive()))
            {
                // A faulted projection makes no checkpoint progress: no candidate, no write, no version increment.
                return GrainStateWriteOutcome.RejectedFaulted;
            }

            var candidate = _committed.Clone();
            mutate(candidate);

            _state.State = candidate; // stage into the provider buffer for the write
            try
            {
                await _state.WriteStateAsync();
            }
            catch
            {
                // Roll back: the failed candidate must never be visible, and onCommitted must not run. Restore the
                // provider buffer to the committed state; _committed/_committedSnapshot are left untouched.
                _state.State = _committed;
                throw;
            }

            _committed = candidate; // publish only after a successful commit
            _committedSnapshot = MultiProjectionGrainStateSnapshot.From(candidate);
            onCommitted?.Invoke(); // after-success continuation: safe to apply live side effects now
            return GrainStateWriteOutcome.Committed;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Re-reads the provider payload (e.g. to refresh the ETag after a conflict) and adopts it as committed.</summary>
    public async Task ReadStateAsync()
    {
        await _state.ReadStateAsync();
        AdoptProviderStateAsCommitted();
    }

    /// <summary>Clears the provider payload and adopts the cleared state as committed.</summary>
    public async Task ClearStateAsync()
    {
        await _state.ClearStateAsync();
        AdoptProviderStateAsCommitted();
    }

    private bool CommittedFaultExists() => _committed.FaultEventId is not null;
}
