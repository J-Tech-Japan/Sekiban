using System.Reflection;
using Orleans.Runtime;
using Sekiban.Dcb.Orleans.Grains;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     Friend tests for the production <see cref="CoordinatedGrainStateStore" /> — the sole owner of the grain's raw
///     <see cref="IPersistentState{TState}" /> write capability. They drive the store directly with a recording
///     persistent-state fake that clones state on COMMIT and assigns a monotonic version. They prove: writes are
///     serialized and copy-on-write (a failed write leaves no uncommitted field visible); a checkpoint is suppressed
///     while a fault exists (committed OR live); and the read view is immutable. A reflection/architecture test
///     enforces the ownership structurally.
/// </summary>
public class CoordinatedGrainStateStoreTests
{
    private static CoordinatedGrainStateStore NewStore(RecordingPersistentState fake, Func<bool>? liveFault = null) =>
        new(fake, liveFault ?? (() => false));

    [Fact]
    public async Task Checkpoint_then_fault_writes_serialize_copy_on_write_with_no_overwrite()
    {
        var fake = new RecordingPersistentState();
        fake.ParkFirstWrite();
        var store = NewStore(fake);

        // Op A — a checkpoint write. Its mutation runs on a CLONE inside the gate; the write then parks inside the fake.
        var a = store.ExecuteWriteAsync(GrainStateWriteKind.Checkpoint, s => s.LastSortableUniqueId = "cp");
        await fake.FirstWriteEntered;

        // Op B — a fault-descriptor write, ready while A is parked.
        var b = store.ExecuteWriteAsync(GrainStateWriteKind.FaultDescriptor, s =>
        {
            s.FaultEventId = "e1";
            s.FaultMessage = "fault";
        });

        Assert.False(b.IsCompleted);                 // B waits on the gate
        Assert.Equal(1, fake.MaxConcurrentWrites);
        Assert.Empty(fake.Commits);                  // A parked before committing
        Assert.Null(store.Committed.LastSortableUniqueId); // A's candidate is not published until it commits

        fake.ReleaseFirstWrite();
        Assert.Equal(GrainStateWriteOutcome.Committed, await a);
        Assert.Equal(GrainStateWriteOutcome.Committed, await b);

        Assert.Equal(1, fake.MaxConcurrentWrites);                          // never overlapped
        Assert.Equal(new[] { 1L, 2L }, fake.Commits.Select(c => c.Version).ToArray()); // monotonic version
        // First commit: checkpoint only, no fault (clone-on-commit — B's later mutation is not visible here).
        Assert.Equal("cp", fake.Commits[0].LastSortableUniqueId);
        Assert.Null(fake.Commits[0].FaultMessage);
        // Second commit: fault added while RETAINING the checkpoint (no overwrite).
        Assert.Equal("cp", fake.Commits[1].LastSortableUniqueId);
        Assert.Equal("fault", fake.Commits[1].FaultMessage);
        // Committed view reflects the last successful commit.
        Assert.Equal("cp", store.Committed.LastSortableUniqueId);
        Assert.Equal("fault", store.Committed.FaultMessage);
    }

    [Fact]
    public async Task Failed_checkpoint_write_rolls_back_no_uncommitted_state_leaks_into_reads_or_later_writes()
    {
        var fake = new RecordingPersistentState();
        var store = NewStore(fake);

        // A checkpoint write fails after mutating its candidate.
        fake.FailNextWrite = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ExecuteWriteAsync(GrainStateWriteKind.Checkpoint, s =>
            {
                s.LastSortableUniqueId = "uncommitted";
                s.ProjectorVersion = "v-bad";
            }));

        // The failed candidate is not visible to reads.
        Assert.Null(store.Committed.LastSortableUniqueId);
        Assert.Null(store.Committed.ProjectorVersion);
        Assert.Empty(fake.Commits);

        // A later successful, unrelated write does not carry the failed candidate's fields.
        Assert.Equal(
            GrainStateWriteOutcome.Committed,
            await store.ExecuteWriteAsync(GrainStateWriteKind.Checkpoint, s => s.LastSortableUniqueId = "committed"));

        Assert.Equal("committed", store.Committed.LastSortableUniqueId);
        Assert.Null(store.Committed.ProjectorVersion);          // v-bad never leaked
        Assert.Single(fake.Commits);
        Assert.Equal("committed", fake.Commits[0].LastSortableUniqueId);
    }

    [Fact]
    public async Task Fault_first_then_checkpoint_is_rejected_no_second_write_checkpoint_unchanged()
    {
        var fake = new RecordingPersistentState();
        var store = NewStore(fake);

        // Commit a fault first.
        Assert.Equal(
            GrainStateWriteOutcome.Committed,
            await store.ExecuteWriteAsync(GrainStateWriteKind.FaultDescriptor, s =>
            {
                s.FaultEventId = "e1";
                s.FaultMessage = "fault";
            }));

        // A normal checkpoint is now rejected: a faulted projection makes no checkpoint progress.
        var outcome = await store.ExecuteWriteAsync(GrainStateWriteKind.Checkpoint, s => s.LastSortableUniqueId = "cp");

        Assert.Equal(GrainStateWriteOutcome.RejectedFaulted, outcome);
        Assert.Single(fake.Commits);                      // no second provider write / version increment
        Assert.Null(store.Committed.LastSortableUniqueId); // checkpoint unchanged
        Assert.Equal("fault", store.Committed.FaultMessage); // fault retained

        // A contracted fault/reset write is still allowed while faulted.
        Assert.Equal(
            GrainStateWriteOutcome.Committed,
            await store.ExecuteWriteAsync(GrainStateWriteKind.OperatorReset, s =>
            {
                s.FaultEventId = null;
                s.FaultMessage = null;
                s.ProjectorName = "rebuilt";
            }));
    }

    [Fact]
    public async Task Live_actor_fault_suppresses_checkpoint_even_without_a_committed_descriptor()
    {
        var fake = new RecordingPersistentState();
        var live = true;
        var store = NewStore(fake, () => live);

        // No committed fault descriptor, but the live actor fault is active (fault persistence may still be retrying).
        var outcome = await store.ExecuteWriteAsync(GrainStateWriteKind.Checkpoint, s => s.LastSortableUniqueId = "cp");
        Assert.Equal(GrainStateWriteOutcome.RejectedFaulted, outcome);
        Assert.Empty(fake.Commits);

        // Once the live fault clears, the checkpoint proceeds.
        live = false;
        Assert.Equal(
            GrainStateWriteOutcome.Committed,
            await store.ExecuteWriteAsync(GrainStateWriteKind.Checkpoint, s => s.LastSortableUniqueId = "cp"));
        Assert.Equal("cp", store.Committed.LastSortableUniqueId);
    }

    [Fact]
    public async Task Committed_is_a_true_immutable_snapshot_not_the_mutable_payload()
    {
        var fake = new RecordingPersistentState();
        var store = NewStore(fake);
        await store.ExecuteWriteAsync(GrainStateWriteKind.Checkpoint, s => s.LastSortableUniqueId = "cp");

        var view = store.Committed;

        // Runtime type, not just the declared return type: the view is a distinct snapshot, not the persisted payload,
        // and cannot be downcast to a mutable reference.
        Assert.IsNotType<MultiProjectionGrainState>(view);
        Assert.Null(view as MultiProjectionGrainState);
        Assert.False(typeof(MultiProjectionGrainState).IsAssignableFrom(view.GetType()));
        Assert.Equal("cp", view.LastSortableUniqueId); // still a faithful read
    }

    [Fact]
    public async Task Failed_operator_reset_write_does_not_advance_committed_state_so_a_caller_skips_its_live_clear()
    {
        var fake = new RecordingPersistentState();
        var store = NewStore(fake);

        // Seed a committed fault (as a faulted grain would have).
        await store.ExecuteWriteAsync(GrainStateWriteKind.FaultDescriptor, s =>
        {
            s.FaultEventId = "e1";
            s.FaultMessage = "fault";
        });

        // A reset whose durable write fails: it THROWS, so a caller awaiting it never reaches its post-await live clear.
        fake.FailNextWrite = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ExecuteWriteAsync(GrainStateWriteKind.OperatorReset, s =>
            {
                s.FaultEventId = null;
                s.FaultMessage = null;
            }));

        // The committed descriptor is unchanged — the failed reset did not clear it.
        Assert.Equal("e1", store.Committed.FaultEventId);
        Assert.Equal("fault", store.Committed.FaultMessage);

        // A later successful reset does clear the committed descriptor (and only then would a caller clear live state).
        Assert.Equal(
            GrainStateWriteOutcome.Committed,
            await store.ExecuteWriteAsync(GrainStateWriteKind.OperatorReset, s =>
            {
                s.FaultEventId = null;
                s.FaultMessage = null;
            }));
        Assert.Null(store.Committed.FaultEventId);
        Assert.Null(store.Committed.FaultMessage);
    }

    [Fact]
    public void Grain_holds_no_persisted_state_reference_and_the_store_exposes_no_mutable_state()
    {
        // The grain must reach persisted state only through the store, so it holds neither the raw IPersistentState nor
        // the mutable MultiProjectionGrainState.
        var grainFields = InstanceFields(typeof(MultiProjectionGrain)).ToList();
        Assert.DoesNotContain(grainFields, f => IsPersistentState(f.FieldType));
        Assert.DoesNotContain(grainFields, f => f.FieldType == typeof(MultiProjectionGrainState));

        // The store owns exactly one IPersistentState field, and exposes NO member returning the mutable state — reads
        // escape only as the read-only view.
        var storeType = typeof(CoordinatedGrainStateStore);
        Assert.Single(InstanceFields(storeType), f => IsPersistentState(f.FieldType));

        var members = storeType
            .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m is PropertyInfo or MethodInfo)
            .Where(m => !(m is MethodInfo mi && (mi.IsSpecialName || mi.Name.StartsWith("<"))));
        foreach (var m in members)
        {
            var returnType = m switch
            {
                PropertyInfo p => p.PropertyType,
                MethodInfo mi => mi.ReturnType,
                _ => typeof(void)
            };
            Assert.NotEqual(typeof(MultiProjectionGrainState), returnType);
        }

        // The committed view is the read-only interface, not the mutable type.
        Assert.Equal(typeof(IReadOnlyMultiProjectionGrainState), storeType.GetProperty("Committed")!.PropertyType);
    }

    [Fact]
    public void ResetProjectionFault_is_admin_plane_only_and_not_on_ISekibanExecutor()
    {
        // The operator-only reset is on the grain admin interface...
        Assert.NotNull(typeof(IMultiProjectionGrain).GetMethod(nameof(IMultiProjectionGrain.ResetProjectionFaultAsync)));

        // ...and is NOT exposed through the product query/command executor surface.
        var executorType = typeof(Sekiban.Dcb.ISekibanExecutor);
        Assert.DoesNotContain(
            executorType.GetMethods(),
            m => m.Name.Contains("ResetProjectionFault", StringComparison.Ordinal));
        Assert.Null(executorType.GetMethod("ResetProjectionFaultAsync"));

        // No production type auto-invokes it: the only references are the grain interface/implementation and tests.
        Assert.Null(typeof(CoordinatedGrainStateStore).GetMethod("ResetProjectionFaultAsync"));
    }

    private static IEnumerable<FieldInfo> InstanceFields(Type t) =>
        t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static bool IsPersistentState(Type t) =>
        t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IPersistentState<>);

    /// <summary>
    ///     A persistent-state fake that records max concurrent writes, parks the FIRST write so an overlapping second
    ///     write would be observed, CLONES the state on commit with a monotonic version, and can fail the next write.
    /// </summary>
    private sealed class RecordingPersistentState : IPersistentState<MultiProjectionGrainState>
    {
        private readonly object _sync = new();
        private int _current;
        private long _version;
        private readonly TaskCompletionSource _firstEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _parkArmed;

        public MultiProjectionGrainState State { get; set; } = new();
        public string Etag => _version.ToString();
        public bool RecordExists { get; private set; }
        public bool FailNextWrite { get; set; }

        /// <summary>Opt-in: park the first write so a concurrency test can observe an overlapping second write.</summary>
        public void ParkFirstWrite() => _parkArmed = true;
        public int MaxConcurrentWrites { get; private set; }
        public List<CommittedSnapshot> Commits { get; } = new();

        public Task FirstWriteEntered => _firstEntered.Task;
        public void ReleaseFirstWrite() => _release.TrySetResult();

        public async Task WriteStateAsync()
        {
            if (FailNextWrite)
            {
                FailNextWrite = false;
                throw new InvalidOperationException("injected write failure");
            }

            bool park;
            lock (_sync)
            {
                var n = ++_current;
                if (n > MaxConcurrentWrites)
                {
                    MaxConcurrentWrites = n;
                }

                park = n == 1 && _parkArmed;
                if (park)
                {
                    _parkArmed = false;
                }
            }

            if (park)
            {
                _firstEntered.TrySetResult();
                await _release.Task; // park so a bypassed/overlapping second write would be observed here
            }

            lock (_sync)
            {
                _version++;
                RecordExists = true;
                Commits.Add(new CommittedSnapshot(_version, State.LastSortableUniqueId, State.FaultMessage, State.ProjectorVersion));
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

    private sealed record CommittedSnapshot(long Version, string? LastSortableUniqueId, string? FaultMessage, string? ProjectorVersion);
}
