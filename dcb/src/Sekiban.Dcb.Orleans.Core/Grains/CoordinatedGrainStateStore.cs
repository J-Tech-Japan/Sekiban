using Orleans.Runtime;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Sole owner of the grain's raw <see cref="IPersistentState{TState}" /> write capability. The grain hands its
///     Orleans-injected persistent state to this store in the constructor and keeps no direct reference of its own, so
///     no call site can reach <c>WriteStateAsync</c> except through here — the single-writer serialization is enforced
///     by TYPE OWNERSHIP, not by convention. A reflection/architecture test asserts the grain holds no
///     <see cref="IPersistentState{TState}" /> field and that this type is the only owner.
///     Every write goes through <see cref="ExecuteWriteAsync" />, which applies the caller's mutation to the persisted
///     payload AND writes it inside the SAME acquired gate — so no caller mutates persisted fields for an operation
///     before entering the gate, and a fault-descriptor retry can never interleave with a normal checkpoint persist.
///     Read access to the payload is exposed read-only for in-memory reads/field updates that are not persisted writes.
/// </summary>
internal sealed class CoordinatedGrainStateStore
{
    private readonly IPersistentState<MultiProjectionGrainState> _state;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CoordinatedGrainStateStore(IPersistentState<MultiProjectionGrainState> state) =>
        _state = state ?? throw new ArgumentNullException(nameof(state));

    /// <summary>The persisted payload. In-memory reads and field updates are allowed; only writes are coordinated.</summary>
    public MultiProjectionGrainState State => _state.State;

    public bool RecordExists => _state.RecordExists;

    /// <summary>
    ///     Applies <paramref name="mutate" /> to the persisted payload and writes it, atomically under the single-writer
    ///     gate. At most one write is in flight at a time; a second caller waits for the first to commit (or fail)
    ///     before its mutation and write run.
    /// </summary>
    public async Task ExecuteWriteAsync(Action<MultiProjectionGrainState> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        await _gate.WaitAsync();
        try
        {
            mutate(_state.State);
            await _state.WriteStateAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Writes the current payload with no additional mutation, still under the gate.</summary>
    public Task WriteAsync() => ExecuteWriteAsync(static _ => { });

    public Task ReadStateAsync() => _state.ReadStateAsync();

    public Task ClearStateAsync() => _state.ClearStateAsync();
}
