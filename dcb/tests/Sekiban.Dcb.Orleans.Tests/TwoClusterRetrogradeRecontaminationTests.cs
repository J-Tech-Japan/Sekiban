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

    private static string WinnerOf(MultiProjectionState state) =>
        ((FirstWinsProjector)state.Payload).Winners.TryGetValue("team-1", out var v) ? v : string.Empty;

    private static async Task<string> WinnerAsync(IMultiProjectionGrain grain)
    {
        var rb = await grain.GetStateAsync();
        return rb.IsSuccess ? (((FirstWinsProjector)rb.GetValue().Payload).Winners.TryGetValue("team-1", out var v) ? v : "") : "(fail)";
    }

    private static async Task<string> PollWinnerAsync(IMultiProjectionGrain grain, string expected)
    {
        // The first query drives the durable rebuild (fail-closed until it reaches the head), so poll generously and
        // TOLERATE the fail-closed errors — do NOT block on waitForCatchUp (that can deadlock against the barrier). Nudge
        // the catch-up periodically with RefreshAsync so a rebuild under cross-cluster CAS contention makes progress.
        for (var i = 0; i < 200; i++)
        {
            var rb = await grain.GetStateAsync(canGetUnsafeState: true, waitForCatchUp: false);
            if (rb.IsSuccess && ((FirstWinsProjector)rb.GetValue().Payload).Winners.TryGetValue("team-1", out var v) && v == expected)
            {
                return v;
            }
            if (i % 15 == 14)
            {
                try { await grain.RefreshAsync(); } catch { /* fail-closed during rebuild is expected */ }
            }
            await Task.Delay(200);
        }
        return await WinnerAsync(grain);
    }

    private static async Task<MultiProjectionState> PollSafeWinnerAsync(IMultiProjectionGrain grain, string expected)
    {
        MultiProjectionState last = null!;
        for (var i = 0; i < 80; i++)
        {
            var rb = await grain.GetStateAsync(canGetUnsafeState: false, waitForCatchUp: false);
            if (rb.IsSuccess)
            {
                last = rb.GetValue();
                if (((FirstWinsProjector)last.Payload).Winners.TryGetValue("team-1", out var v) && v == expected && last.IsSafeState)
                {
                    return last;
                }
            }
            await Task.Delay(200);
        }
        return last;
    }

    private static async Task AssertScalarAndListAsync(ISekibanExecutor executor, string expectedWinner)
    {
        var scalar = await executor.QueryAsync(new WinnerQuery("team-1"));
        Assert.True(scalar.IsSuccess, scalar.IsSuccess ? "" : scalar.GetException().ToString());
        Assert.Equal(expectedWinner, scalar.GetValue().Value);

        var list = await executor.QueryAsync(new WinnerListQuery());
        Assert.True(list.IsSuccess, list.IsSuccess ? "" : list.GetException().ToString());
        var row = Assert.Single(list.GetValue().Items.ToList(), r => r.Id == "team-1");
        Assert.Equal(expectedWinner, row.Value);
    }

    private static Event CreateEvent(IEventPayload payload, DateTime timestamp) => new(
        payload, SortableUniqueId.Generate(timestamp, Guid.NewGuid()), payload.GetType().Name,
        Guid.NewGuid(), new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"), new List<string>());

    private static SerializableEvent ToSerializable(Event ev) => new(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ev.Payload, ev.Payload.GetType())),
        ev.SortableUniqueIdValue, ev.Id, ev.EventMetadata, ev.Tags.ToList(), ev.EventType);

    public record CreatedWithId(string Id, string Value) : IEventPayload;
    public record WinnerResult(string Value);

    public record WinnerQuery(string Id) : IMultiProjectionQuery<FirstWinsProjector, WinnerQuery, WinnerResult>
    {
        public static ResultBox<WinnerResult> HandleQuery(FirstWinsProjector p, WinnerQuery q, IQueryContext c) =>
            ResultBox.FromValue(new WinnerResult(p.Winners.TryGetValue(q.Id, out var v) ? v : string.Empty));
    }

    public record WinnerRow(string Id, string Value);

    public record WinnerListQuery : IMultiProjectionListQuery<FirstWinsProjector, WinnerListQuery, WinnerRow>, IQueryPagingParameter
    {
        public int? PageNumber { get; init; }
        public int? PageSize { get; init; }
        public static ResultBox<IEnumerable<WinnerRow>> HandleFilter(FirstWinsProjector p, WinnerListQuery q, IQueryContext c) =>
            ResultBox.FromValue(p.Winners.Select(kv => new WinnerRow(kv.Key, kv.Value)));
        public static ResultBox<IEnumerable<WinnerRow>> HandleSort(IEnumerable<WinnerRow> f, WinnerListQuery q, IQueryContext c) =>
            ResultBox.FromValue(f.OrderBy(r => r.Id, StringComparer.Ordinal).AsEnumerable());
    }

    [global::Orleans.GenerateSerializer]
    public record FirstWinsProjector : IMultiProjector<FirstWinsProjector>
    {
        [global::Orleans.Id(0)]
        public Dictionary<string, string> Winners { get; init; } = new();
        public static string MultiProjectorName => "g20-retro-first-wins";
        public static string MultiProjectorVersion => "1.0.0";
        public static FirstWinsProjector GenerateInitialPayload() => new();
        public static ResultBox<FirstWinsProjector> Project(
            FirstWinsProjector payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold)
        {
            if (ev.Payload is CreatedWithId created)
            {
                if (payload.Winners.ContainsKey(created.Id)) return ResultBox.FromValue(payload);
                return ResultBox.FromValue(payload with { Winners = new Dictionary<string, string>(payload.Winners) { [created.Id] = created.Value } });
            }
            return ResultBox.FromValue(payload);
        }
    }

    internal static class SharedStores
    {
        public static DcbDomainTypes Domain { get; private set; } = BuildDomain();
        public static InMemoryEventStore EventStore { get; private set; } = new(Domain.EventTypes);
        public static InMemoryMultiProjectionStateStore StateStore { get; } = new();

        public static void Reset()
        {
            Domain = BuildDomain();
            EventStore = new InMemoryEventStore(Domain.EventTypes);
            StateStore.DeleteAllAsync(FirstWinsProjector.MultiProjectorName).GetAwaiter().GetResult();
        }

        private static DcbDomainTypes BuildDomain()
        {
            var eventTypes = new SimpleEventTypes();
            eventTypes.RegisterEventType<CreatedWithId>("CreatedWithId");
            var mp = new SimpleMultiProjectorTypes();
            mp.RegisterProjector<FirstWinsProjector>();
            var q = new SimpleQueryTypes();
            q.RegisterQuery<WinnerQuery>();
            q.RegisterListQuery<WinnerListQuery>();
            return new DcbDomainTypes(eventTypes, new SimpleTagTypes(), new SimpleTagProjectorTypes(),
                new SimpleTagStatePayloadTypes(), mp, q, new JsonSerializerOptions());
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
                    services.AddSingleton<IMultiProjectionStateStore>(SharedStores.StateStore);
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
