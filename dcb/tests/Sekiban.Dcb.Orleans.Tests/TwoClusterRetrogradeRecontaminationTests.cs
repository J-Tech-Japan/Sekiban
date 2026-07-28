using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Storage.Checkpoints;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;
using static Sekiban.Dcb.Orleans.Tests.G20Shared;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     SEK-G20 — the deferred-from-G18 RETROGRADE cross-cluster proof, through PRODUCT code on two genuinely independent
///     Orleans clusters sharing ONE authoritative event store and ONE external checkpoint row. Cluster A performs a
///     retrograde full rebuild (a globally-earlier already-safe event that flips the first-event-wins winner). Cluster B,
///     still holding its stale pre-rebuild token, has its parked persist RELEASED — and it is CAS-rejected, so the shared
///     row is NOT re-contaminated with B's stale winner. Both clusters then restart to EXACT convergence on the
///     globally-earliest winner (state, scalar, list, position, IsSafeState). This is the product-path integration the
///     store-level TwoClusterRecontaminationProofTests could not exercise (first-query barrier, grain persist path,
///     rebuilt commit, restart).
/// </summary>
public class TwoClusterRetrogradeRecontaminationTests : IAsyncLifetime
{
    private TestCluster _clusterA = null!;
    private TestCluster _clusterB = null!;

    public async Task InitializeAsync()
    {
        SharedStores.Reset();
        _clusterA = await BuildClusterAsync("A");
        _clusterB = await BuildClusterAsync("B");
    }

    private static async Task<TestCluster> BuildClusterAsync(string name)
    {
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"G20-retro-{name}-{uniqueId}";
        builder.Options.ServiceId = $"G20-retro-{name}-{uniqueId}";
        builder.AddSiloBuilderConfigurator<Configurator>();
        // The silo's ConfigureServices (below) reads this to build a per-cluster gating decorator. Deploys are sequential
        // (A then B, each awaited), so the static reliably identifies the cluster whose silo is being configured.
        SharedStores.CurrentClusterName = name;
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }

    public async Task DisposeAsync()
    {
        await _clusterA.StopAllSilosAsync();
        _clusterA.Dispose();
        await _clusterB.StopAllSilosAsync();
        _clusterB.Dispose();
    }

    [Fact]
    public async Task RetrogradeRebuild_ConcurrentClusters_NoRecontamination_OneGenerationBump_BothRestartConverge()
    {
        var grainA = _clusterA.Client.GetGrain<IMultiProjectionGrain>(FirstWinsProjector.MultiProjectorName);
        var grainB = _clusterB.Client.GetGrain<IMultiProjectionGrain>(FirstWinsProjector.MultiProjectorName);
        var execA = new OrleansDcbExecutor(_clusterA.Client, SharedStores.EventStore, SharedStores.Domain);
        var execB = new OrleansDcbExecutor(_clusterB.Client, SharedStores.EventStore, SharedStores.Domain);

        // (1) One already-safe event ("later", -30s). Both clusters catch up + persist through the CAS -> the shared
        //     checkpoint is created Active(gen0), winner "later"; both adopt it. (The exact deterministic "stale writer
        //     CAS-rejected, row byte-for-byte unchanged" sequence is proven in TwoClusterRecontaminationProofTests; here
        //     we prove the PRODUCT-code integration converges under concurrent retrograde rebuild.)
        var later = CreateEvent(new CreatedWithId("team-1", "later"), DateTime.UtcNow.AddSeconds(-30));
        await SharedStores.EventStore.WriteSerializableEventsAsync(new[] { ToSerializable(later) });
        await grainA.RefreshAsync();
        Assert.True((await grainA.PersistStateAsync()).IsSuccess);
        await grainB.RefreshAsync();
        _ = await grainB.PersistStateAsync();   // CAS-contends with A's create; whoever loses adopts the winner's token
        var slot0 = (await SharedStores.StateStore.ReadCheckpointSlotAsync(FirstWinsProjector.MultiProjectorName, "1.0.0")).GetValue();
        Assert.True(slot0.IsActive);
        Assert.Equal(0, slot0.Generation);
        Assert.Equal("later", await WinnerAsync(grainA));
        Assert.Equal("later", await WinnerAsync(grainB));

        // (2) A globally-EARLIER already-safe event ("earlier", -31s) — a RETROGRADE arrival that flips the
        //     first-event-wins winner — is written to the shared store and delivered to BOTH clusters out of order. Both
        //     independently trigger a durable bump+tombstone rebuild; the CAS admits EXACTLY ONE generation bump.
        var earlier = CreateEvent(new CreatedWithId("team-1", "earlier"), DateTime.UtcNow.AddSeconds(-31));
        await SharedStores.EventStore.WriteSerializableEventsAsync(new[] { ToSerializable(earlier) });
        await grainA.AddEventsAsync(new[] { ToSerializable(earlier) });
        await grainB.AddEventsAsync(new[] { ToSerializable(earlier) });

        // Both clusters rebuild to the globally-earliest winner (fail-closed until rebuilt).
        Assert.Equal("earlier", await PollWinnerAsync(grainA, "earlier"));
        Assert.Equal("earlier", await PollWinnerAsync(grainB, "earlier"));
        _ = await grainA.PersistStateAsync();
        _ = await grainB.PersistStateAsync();

        // The shared row is Active at the bumped generation with the rebuilt 2-event checkpoint — NEVER dropped back to a
        // stale 1-event state, and the generation advanced EXACTLY ONCE (one retrograde epoch, one winner), never twice.
        var slotAfter = (await SharedStores.StateStore.ReadCheckpointSlotAsync(FirstWinsProjector.MultiProjectorName, "1.0.0")).GetValue();
        Assert.True(slotAfter.IsActive);
        Assert.Equal(1, slotAfter.Generation);
        Assert.Equal(2, slotAfter.Record!.EventsProcessed);

        await AssertScalarAndListAsync(execA, "earlier");
        await AssertScalarAndListAsync(execB, "earlier");

        // (3) Restart BOTH clusters. A fresh activation reads the control plane before binding any payload, so neither
        //     serves a stale value. Both converge to the exact globally-earliest winner + safe state + position.
        await grainA.RequestDeactivationAsync();
        await grainB.RequestDeactivationAsync();
        await Task.Delay(1500);
        var grainA2 = _clusterA.Client.GetGrain<IMultiProjectionGrain>(FirstWinsProjector.MultiProjectorName);
        var grainB2 = _clusterB.Client.GetGrain<IMultiProjectionGrain>(FirstWinsProjector.MultiProjectorName);

        var safeA = await PollSafeWinnerAsync(grainA2, "earlier");
        var safeB = await PollSafeWinnerAsync(grainB2, "earlier");
        Assert.Equal("earlier", WinnerOf(safeA));
        Assert.Equal("earlier", WinnerOf(safeB));
        Assert.Equal(safeA.LastSortableUniqueId, safeB.LastSortableUniqueId);   // same converged position on both clusters
        // The safe POSITION is the LAST folded event (the max SortableUniqueId = "later"); the WINNER is the globally-
        // EARLIEST ("earlier", first-event-wins). Both clusters agree on the same position and winner.
        Assert.Equal(later.SortableUniqueIdValue, safeA.LastSortableUniqueId);

        Assert.True((await grainA2.GetStateAsync()).GetValue().IsSafeState);
        Assert.True((await grainB2.GetStateAsync()).GetValue().IsSafeState);
        await AssertScalarAndListAsync(new OrleansDcbExecutor(_clusterA.Client, SharedStores.EventStore, SharedStores.Domain), "earlier");
        await AssertScalarAndListAsync(new OrleansDcbExecutor(_clusterB.Client, SharedStores.EventStore, SharedStores.Domain), "earlier");
    }

    [Fact]
    public async Task DeterministicParkedStaleWriter_RejectedAgainstTombstone_ArmsBarrier_NoRecontamination()
    {
        // The packet-critical race, executed DETERMINISTICALLY through product code: cluster B captures the old Active
        // token and PARKS its real grain persist at the store boundary; cluster A retrograde-rebuilds and its tombstone is
        // held OPEN; B is released and its stale CAS is ConditionRejected against the tombstone — which arms B's query
        // barrier — so the shared row is NEVER re-contaminated with B's stale winner. A then commits its rebuild and both
        // converge on the globally-earliest winner. No polling for the race window: both critical writes are gated.
        var grainA = _clusterA.Client.GetGrain<IMultiProjectionGrain>(FirstWinsProjector.MultiProjectorName);
        var grainB = _clusterB.Client.GetGrain<IMultiProjectionGrain>(FirstWinsProjector.MultiProjectorName);
        var gateA = SharedStores.GatesByCluster["A"];
        var gateB = SharedStores.GatesByCluster["B"];
        var execB = new OrleansDcbExecutor(_clusterB.Client, SharedStores.EventStore, SharedStores.Domain);

        // (1) One already-safe event; both clusters catch up + persist through the CAS -> shared row Active(gen0), winner
        //     "later"; both adopt the gen0 token.
        var later = CreateEvent(new CreatedWithId("team-1", "later"), DateTime.UtcNow.AddSeconds(-30));
        await SharedStores.EventStore.WriteSerializableEventsAsync(new[] { ToSerializable(later) });
        await grainA.RefreshAsync();
        Assert.True((await grainA.PersistStateAsync()).IsSuccess);
        await grainB.RefreshAsync();
        _ = await grainB.PersistStateAsync();   // whoever loses the create adopts the gen0 token
        var slot0 = await ReadSlotAsync();
        Assert.True(slot0.IsActive);
        Assert.Equal(0, slot0.Generation);

        // (2) Deliver a B-ONLY safe event (not in the shared store) so B has a genuine pending persist on the gen0 token
        //     whose stale winner ("team-2") would be detectable if it ever contaminated the row.
        var bLocal = CreateEvent(new CreatedWithId("team-2", "B-stale"), DateTime.UtcNow.AddSeconds(-20));
        await grainB.AddEventsAsync(new[] { ToSerializable(bLocal) });
        await PollUnsafeContainsAsync(grainB, "team-2");

        // (3) Arm B's upsert gate and launch B's REAL grain persist; it parks at the store boundary holding the gen0 token.
        var bUpsert = new GatingCheckpointStore.Gate { Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        gateB.UpsertGate = bUpsert;
        var bPersist = grainB.PersistStateAsync();
        await bUpsert.Arrived.Task;

        // (4) Arm A's rebuilt-commit gate, then deliver the globally-earlier retrograde event to A and drive its rebuild
        //     FIRE-AND-FORGET (never awaited — the driving call itself parks at the commit gate, so awaiting it would
        //     deadlock). A invalidates the shared row (tombstone gen1) and then parks at its rebuilt commit, holding the
        //     tombstone OPEN. B's stale writer is now guaranteed to meet a tombstone, not a fresh Active row.
        var aCommit = new GatingCheckpointStore.Gate { Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        gateA.CommitRebuiltGate = aCommit;
        var earlier = CreateEvent(new CreatedWithId("team-1", "earlier"), DateTime.UtcNow.AddSeconds(-31));
        await SharedStores.EventStore.WriteSerializableEventsAsync(new[] { ToSerializable(earlier) });
        await grainA.AddEventsAsync(new[] { ToSerializable(earlier) });
        var aDrive = grainA.GetStateAsync(canGetUnsafeState: true, waitForCatchUp: false);   // drives invalidate -> parks at commit
        await aCommit.Arrived.Task;   // A reached the rebuilt-commit boundary => the invalidate (tombstone) already committed
        var tomb = await PollUntilTombstoneAsync();
        Assert.True(tomb.IsTombstoned);
        Assert.Equal(1, tomb.Generation);

        // (5) Release B's parked stale persist. Its external-checkpoint CAS on the OLD gen0 token meets the tombstone ->
        //     ConditionRejected, so the grain arms its query barrier and the shared row is byte-for-byte unchanged. (The
        //     PersistStateAsync bool reflects the grain-state snapshot write, which is a SEPARATE store from the external
        //     checkpoint row — the anti-recontamination guarantee is on the row itself, asserted here.)
        bUpsert.Release!.SetResult();
        _ = await bPersist;
        var afterB = await ReadSlotAsync();
        Assert.True(afterB.IsTombstoned, $"the shared row was re-contaminated by the stale writer: {afterB.Lifecycle} gen{afterB.Generation}");
        Assert.Equal(1, afterB.Generation);
        Assert.Equal(tomb.Revision, afterB.Revision);   // exact same token — B did not touch the row

        // (6) Release A's rebuilt commit -> Active(gen1) with the rebuilt 2-event checkpoint, winner "earlier".
        aCommit.Release!.SetResult();
        _ = await aDrive;
        var final = await ReadSlotAsync();
        Assert.True(final.IsActive);
        Assert.Equal(1, final.Generation);
        Assert.Equal(2, final.Record!.EventsProcessed);

        // (7) QUERY-BARRIER OBSERVATION: the ConditionRejected armed B's first-query barrier, so B CANNOT serve a normal
        //     success carrying its stale local winner ("team-2"). The retrograde "earlier" is delivered to B; its barrier
        //     drives a from-scratch rebuild over the authoritative history (which has NO team-2, and folds "earlier" first)
        //     so B converges on the globally-earliest winner. team-2 never re-contaminated the shared row and is gone.
        await grainB.AddEventsAsync(new[] { ToSerializable(earlier) });
        var safeB = await PollSafeWinnerAsync(grainB, "earlier");
        Assert.Equal("earlier", WinnerOf(safeB));                 // the barrier-rebuilt SAFE winner
        Assert.True(safeB.IsSafeState);                            // a genuine safe state, not a stale unsafe success
        Assert.False(((FirstWinsProjector)safeB.Payload).Winners.ContainsKey("team-2"));   // stale local winner discarded
        var listB = await execB.QueryAsync(new WinnerListQuery());
        Assert.True(listB.IsSuccess, listB.IsSuccess ? "" : listB.GetException().ToString());
        Assert.DoesNotContain(listB.GetValue().Items, r => r.Id == "team-2");
        Assert.Contains(listB.GetValue().Items, r => r.Id == "team-1" && r.Value == "earlier");

        // (8) Restart BOTH clusters. A fresh activation reads the control plane before binding any payload, so neither
        //     serves a stale value; both converge to the EXACT globally-earliest winner + safe state + position + scalar +
        //     list, byte-identical across clusters.
        await grainA.RequestDeactivationAsync();
        await grainB.RequestDeactivationAsync();
        await Task.Delay(1500);
        var grainA2 = _clusterA.Client.GetGrain<IMultiProjectionGrain>(FirstWinsProjector.MultiProjectorName);
        var grainB2 = _clusterB.Client.GetGrain<IMultiProjectionGrain>(FirstWinsProjector.MultiProjectorName);

        var restartA = await PollSafeWinnerAsync(grainA2, "earlier");
        var restartB = await PollSafeWinnerAsync(grainB2, "earlier");
        Assert.Equal("earlier", WinnerOf(restartA));
        Assert.Equal("earlier", WinnerOf(restartB));
        Assert.True(restartA.IsSafeState);
        Assert.True(restartB.IsSafeState);
        Assert.Equal(restartA.LastSortableUniqueId, restartB.LastSortableUniqueId);   // same converged position on both
        // The safe POSITION is the LAST folded event (max SortableUniqueId = "later"); the WINNER is the globally-earliest.
        Assert.Equal(later.SortableUniqueIdValue, restartA.LastSortableUniqueId);
        await AssertScalarAndListAsync(new OrleansDcbExecutor(_clusterA.Client, SharedStores.EventStore, SharedStores.Domain), "earlier");
        await AssertScalarAndListAsync(new OrleansDcbExecutor(_clusterB.Client, SharedStores.EventStore, SharedStores.Domain), "earlier");
    }

    private static async Task<CheckpointSlot> ReadSlotAsync() =>
        (await SharedStores.StateStore.ReadCheckpointSlotAsync(FirstWinsProjector.MultiProjectorName, "1.0.0")).GetValue();

    private static async Task<CheckpointSlot> PollUntilTombstoneAsync()
    {
        for (var i = 0; i < 200; i++)
        {
            var slot = await ReadSlotAsync();
            if (slot.IsTombstoned)
            {
                return slot;
            }
            await Task.Delay(50);
        }
        return await ReadSlotAsync();
    }

    private static async Task PollUnsafeContainsAsync(IMultiProjectionGrain grain, string id)
    {
        for (var i = 0; i < 100; i++)
        {
            var rb = await grain.GetStateAsync(canGetUnsafeState: true, waitForCatchUp: false);
            if (rb.IsSuccess && ((FirstWinsProjector)rb.GetValue().Payload).Winners.ContainsKey(id))
            {
                return;
            }
            await Task.Delay(50);
        }
    }

    internal static class SharedStores
    {
        public static DcbDomainTypes Domain { get; private set; } = G20Shared.BuildDomain();
        public static InMemoryEventStore EventStore { get; private set; } = new(Domain.EventTypes);
        public static InMemoryMultiProjectionStateStore StateStore { get; } = new();

        // SEK-G20 deterministic parked-writer harness: each cluster's silo gets its OWN gating decorator over the ONE shared
        // row, registered here by cluster name so the test can park exactly one cluster's real grain persist.
        public static string? CurrentClusterName;
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, GatingCheckpointStore> GatesByCluster = new();

        public static void Reset()
        {
            Domain = G20Shared.BuildDomain();
            EventStore = new InMemoryEventStore(Domain.EventTypes);
            GatesByCluster.Clear();
            CurrentClusterName = null;
            StateStore.DeleteAllAsync(FirstWinsProjector.MultiProjectorName).GetAwaiter().GetResult();
        }
    }

    private class Configurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton<DcbDomainTypes>(SharedStores.Domain);
                    services.AddSingleton<IEventStore>(SharedStores.EventStore);
                    // Each cluster gets its OWN gating decorator over the ONE shared checkpoint row (transparent unless the
                    // test arms its gate), so a single cluster's real grain persist can be parked deterministically.
                    var clusterName = SharedStores.CurrentClusterName ?? Guid.NewGuid().ToString("N");
                    var gating = SharedStores.GatesByCluster.GetOrAdd(clusterName, _ => new GatingCheckpointStore(SharedStores.StateStore));
                    services.AddSingleton<IMultiProjectionStateStore>(gating);
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions { SafeWindowMs = 3000 });
                    services.AddSekibanDcbNativeRuntime();
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("OrleansStorage")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }
}
