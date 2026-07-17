using System.Reflection;
using Orleans.Runtime;
using Sekiban.Dcb.Orleans.Grains;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     Friend tests for the production <see cref="CoordinatedGrainStateStore" /> — the sole owner of the grain's raw
///     <see cref="IPersistentState{TState}" /> write capability. They drive the store directly with a recording
///     persistent-state fake that clones state on COMMIT and assigns a monotonic version, competing two DIFFERENT
///     representative operations (a checkpoint write and a fault-descriptor retry write). The first is parked inside the
///     write so the second becomes ready while it is in flight, proving the store — not the harness — serializes them
///     AND that each operation's mutation happens under the same gate as its write (so a bypassed or before-gate
///     mutation is caught). A reflection/architecture test enforces the ownership structurally: the grain holds no
///     IPersistentState field; only the store does.
/// </summary>
public class CoordinatedGrainStateStoreTests
{
    [Fact]
    public async Task Checkpoint_then_fault_writes_serialize_mutate_under_gate_with_no_overwrite()
    {
        var fake = new RecordingPersistentState();
        var store = new CoordinatedGrainStateStore(fake);

        // Op A — a checkpoint write. Its mutation runs INSIDE the gate (the ExecuteWriteAsync delegate) and the write
        // then parks inside the fake, holding the gate.
        var a = store.ExecuteWriteAsync(s => s.LastSortableUniqueId = "cp");
        await fake.FirstWriteEntered;

        // Op B — a fault-descriptor retry write, ready while A is parked.
        var b = store.ExecuteWriteAsync(s => s.FaultMessage = "fault");

        Assert.False(b.IsCompleted);                 // B waits on the gate; its mutation has not run
        Assert.Equal(1, fake.MaxConcurrentWrites);
        Assert.Empty(fake.Commits);                  // A parked before committing
        Assert.Null(fake.State.FaultMessage);        // B's mutation is gated, not applied early

        fake.ReleaseFirstWrite();
        await Task.WhenAll(a, b);

        Assert.Equal(1, fake.MaxConcurrentWrites);                          // never overlapped
        Assert.Equal(new[] { 1L, 2L }, fake.Commits.Select(c => c.Version).ToArray()); // monotonic version
        // First commit: checkpoint only, no fault (clone-on-commit — B's later mutation is not visible here).
        Assert.Equal("cp", fake.Commits[0].LastSortableUniqueId);
        Assert.Null(fake.Commits[0].FaultMessage);
        // Second commit: fault added while RETAINING the checkpoint (no overwrite).
        Assert.Equal("cp", fake.Commits[1].LastSortableUniqueId);
        Assert.Equal("fault", fake.Commits[1].FaultMessage);
    }

    [Fact]
    public void Grain_holds_no_IPersistentState_field_only_the_coordinated_store_owns_it()
    {
        // Structural enforcement: after construction the grain must reach persisted state ONLY through the coordinated
        // store, so it must not retain any IPersistentState field. The store must own exactly that field.
        var grainPersistentStateFields = InstanceFields(typeof(MultiProjectionGrain))
            .Where(f => IsPersistentState(f.FieldType))
            .ToList();
        Assert.Empty(grainPersistentStateFields);

        var storePersistentStateFields = InstanceFields(typeof(CoordinatedGrainStateStore))
            .Where(f => IsPersistentState(f.FieldType))
            .ToList();
        Assert.Single(storePersistentStateFields);
    }

    private static IEnumerable<FieldInfo> InstanceFields(Type t) =>
        t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static bool IsPersistentState(Type t) =>
        t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IPersistentState<>);

    /// <summary>
    ///     A persistent-state fake that records the maximum concurrent writes, parks the FIRST write so a second write
    ///     (if it could overlap) would be observed, and CLONES the state on each commit while assigning a monotonic
    ///     version — so assertions read committed snapshots, not the live shared object.
    /// </summary>
    private sealed class RecordingPersistentState : IPersistentState<MultiProjectionGrainState>
    {
        private readonly object _sync = new();
        private int _current;
        private long _version;
        private readonly TaskCompletionSource _firstEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MultiProjectionGrainState State { get; set; } = new();
        public string Etag => _version.ToString();
        public bool RecordExists { get; private set; }
        public int MaxConcurrentWrites { get; private set; }
        public List<CommittedSnapshot> Commits { get; } = new();

        public Task FirstWriteEntered => _firstEntered.Task;
        public void ReleaseFirstWrite() => _release.TrySetResult();

        public async Task WriteStateAsync()
        {
            int n;
            lock (_sync)
            {
                n = ++_current;
                if (n > MaxConcurrentWrites)
                {
                    MaxConcurrentWrites = n;
                }
            }

            if (n == 1)
            {
                _firstEntered.TrySetResult();
                await _release.Task; // park so a bypassed/overlapping second write would be observed here
            }

            lock (_sync)
            {
                _version++;
                RecordExists = true;
                // Clone on COMMIT: capture the state as it is now, not the live object inspected later.
                Commits.Add(new CommittedSnapshot(_version, State.LastSortableUniqueId, State.FaultMessage));
                _current--;
            }
        }

        public Task WriteStateAsync(CancellationToken cancellationToken) => WriteStateAsync();

        public Task ReadStateAsync() => Task.CompletedTask;
        public Task ReadStateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ClearStateAsync()
        {
            lock (_sync)
            {
                State = new MultiProjectionGrainState();
                RecordExists = false;
            }

            return Task.CompletedTask;
        }

        public Task ClearStateAsync(CancellationToken cancellationToken) => ClearStateAsync();
    }

    private sealed record CommittedSnapshot(long Version, string? LastSortableUniqueId, string? FaultMessage);
}
