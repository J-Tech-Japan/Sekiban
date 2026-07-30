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
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     SEK-G18 item (3): a REAL two-independent-cluster integration proof of the graduation-reconcile NORMAL path (no
///     retrograde rebuild / no shared-row invalidation — those are G20). Two genuinely independent Orleans clusters (each
///     with its own cluster-local grain state) share ONE authoritative event store and ONE external checkpoint row
///     (serviceId=default). Two racing creates for the same id are written to the shared store; each cluster's
///     MultiProjectionGrain independently catches up and, via the served-state reconcile, converges to the globally-earliest
///     (by SortableUniqueId) winner. After graduation, both clusters' state, IsSafeState, position and payload equal a
///     from-scratch global ordered replay. First-event-wins is order-sensitive, so a commutative fold could not catch this.
/// </summary>
public class TwoClusterGraduationReconcileConvergenceTests : IAsyncLifetime
{
    private const string ReplicaServiceA = "replica-a";
    private const string ReplicaServiceB = "replica-b";
    private TestCluster _clusterA = null!;
    private TestCluster _clusterB = null!;

    public async Task InitializeAsync()
    {
        SharedStores.Reset();
        (_clusterA, _) = await BuildClusterAsync("A");
        (_clusterB, _) = await BuildClusterAsync("B");
    }

    private static async Task<(TestCluster Cluster, string ServiceId)> BuildClusterAsync(string name)
    {
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var serviceId = $"G18-2cluster-{name}-{uniqueId}";
        builder.Options.ClusterId = serviceId;
        builder.Options.ServiceId = serviceId;
        if (name == "A")
        {
            builder.AddSiloBuilderConfigurator<ConfiguratorA>();
        }
        else
        {
            builder.AddSiloBuilderConfigurator<ConfiguratorB>();
        }
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return (cluster, serviceId);
    }

    public async Task DisposeAsync()
    {
        await _clusterA.StopAllSilosAsync();
        _clusterA.Dispose();
        await _clusterB.StopAllSilosAsync();
        _clusterB.Dispose();
    }

    [Fact]
    public async Task TwoIndependentClusters_SharedStore_ConvergeToGloballyEarliest_ViaGraduationReconcile()
    {
        // Two racing creates for the same id, both RECENT (inside the safe window), written to the SHARED event store.
        // "earlier" has the globally-earliest SortableUniqueId and must win on both clusters (first-event-wins).
        var earlier = CreateEvent(new CreatedWithId("team-1", "earlier"), DateTime.UtcNow.AddSeconds(-2.0));
        var later = CreateEvent(new CreatedWithId("team-1", "later"), DateTime.UtcNow.AddSeconds(-1.0));
        // Persist "later" first, then "earlier" — the arrival order that produced the #1092 permanent divergence.
        await SharedStores.EventStore.WriteSerializableEventsAsync(new[] { ToSerializable(later), ToSerializable(earlier) });

        var grainA = _clusterA.Client.GetGrain<IMultiProjectionGrain>(FirstWinsProjector.MultiProjectorName);
        var grainB = _clusterB.Client.GetGrain<IMultiProjectionGrain>(FirstWinsProjector.MultiProjectorName);

        // While the events are still buffered (inside the window), the SERVED state on BOTH clusters must already be
        // reconciled to the globally-earliest winner, and IsSafeState must be FALSE (not yet reconciled-identical-to-safe).
        var servedA = await PollUntilAsync(grainA, p => p.Winners.TryGetValue("team-1", out var v) && v == "earlier");
        var servedB = await PollUntilAsync(grainB, p => p.Winners.TryGetValue("team-1", out var v) && v == "earlier");
        Assert.Equal("earlier", servedA.State.Winners["team-1"]);
        Assert.Equal("earlier", servedB.State.Winners["team-1"]);
        Assert.False(servedA.IsSafeState);
        Assert.False(servedB.IsSafeState);

        // SCALAR + LIST query surfaces are SEPARATE code paths from GetStateAsync — assert them explicitly on BOTH clusters.
        // Before graduation the reconcile already makes them return the globally-earliest value (not the arrival-order one).
        var execA = new OrleansDcbExecutor(_clusterA.Client, SharedStores.EventStore, SharedStores.Domain);
        var execB = new OrleansDcbExecutor(_clusterB.Client, SharedStores.EventStore, SharedStores.Domain);
        await AssertScalarAndListAsync(execA, "earlier");
        await AssertScalarAndListAsync(execB, "earlier");

        // After the window passes and both events graduate, the SAFE state converges identically on both clusters, and the
        // served state is now truthfully safe. Both must equal a from-scratch global ordered replay.
        await Task.Delay(4000);
        var expected = GlobalReplay(new[] { earlier, later });

        var safeA = (await grainA.GetStateAsync(canGetUnsafeState: false)).GetValue();
        var safeB = (await grainB.GetStateAsync(canGetUnsafeState: false)).GetValue();
        Assert.Equal("earlier", ((FirstWinsProjector)safeA.Payload).Winners["team-1"]);
        Assert.Equal("earlier", ((FirstWinsProjector)safeB.Payload).Winners["team-1"]);
        Assert.Equal(expected.Winners["team-1"], ((FirstWinsProjector)safeA.Payload).Winners["team-1"]);
        Assert.Equal(safeA.LastSortableUniqueId, safeB.LastSortableUniqueId); // same converged safe position on both clusters

        var servedAfterA = (await grainA.GetStateAsync()).GetValue();
        var servedAfterB = (await grainB.GetStateAsync()).GetValue();
        Assert.True(servedAfterA.IsSafeState);
        Assert.True(servedAfterB.IsSafeState);
        Assert.Equal("earlier", ((FirstWinsProjector)servedAfterA.Payload).Winners["team-1"]);
        Assert.Equal("earlier", ((FirstWinsProjector)servedAfterB.Payload).Winners["team-1"]);

        // After graduation: state + scalar + list agree on the same globally-earliest value on BOTH clusters.
        await AssertScalarAndListAsync(execA, "earlier");
        await AssertScalarAndListAsync(execB, "earlier");
    }

    [Fact]
    public async Task TwoColdReplicas_WithSafeBaseline_ReachRecentHeadBeforeSafeWindow()
    {
        var baseline = CreateEvent(new CreatedWithId("baseline", "safe"), DateTime.UtcNow.AddMinutes(-1));
        await SharedStores.EventStore.WriteSerializableEventsAsync(new[] { ToSerializable(baseline) });

        var seed = _clusterA.Client.GetGrain<IMultiProjectionGrain>(FirstWinsProjector.MultiProjectorName);
        var seeded = await seed.GetStateAsync();
        Assert.True(seeded.IsSuccess);
        Assert.True(seeded.GetValue().IsSafeState);
        Assert.True((await seed.PersistStateAsync()).IsSuccess);
        await seed.RequestDeactivationAsync();

        var recent = CreateEvent(new CreatedWithId("recent", "head"), DateTime.UtcNow);
        await SharedStores.EventStore.WriteSerializableEventsAsync(new[] { ToSerializable(recent) });

        var grainA = _clusterA.Client.GetGrain<IMultiProjectionGrain>(
            ServiceIdGrainKey.Build(ReplicaServiceA, FirstWinsProjector.MultiProjectorName));
        var grainB = _clusterB.Client.GetGrain<IMultiProjectionGrain>(
            ServiceIdGrainKey.Build(ReplicaServiceB, FirstWinsProjector.MultiProjectorName));
        var readGateA = SharedStores.ReplicaA.ParkNextRead();
        var readGateB = SharedStores.ReplicaB.ParkNextRead();
        var invocationA = new TaskCompletionSource<CatchUpProductionObservation>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationB = new TaskCompletionSource<CatchUpProductionObservation>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var hookA = CatchUpProductionTestHooks.Register(
            ReplicaServiceA,
            FirstWinsProjector.MultiProjectorName,
            (point, observation) =>
            {
                if (point == CatchUpProductionHookPoint.InvocationCompleted &&
                    observation.Cursor?.Value == recent.SortableUniqueIdValue)
                {
                    invocationA.TrySetResult(observation);
                }
                return Task.CompletedTask;
            });
        using var hookB = CatchUpProductionTestHooks.Register(
            ReplicaServiceB,
            FirstWinsProjector.MultiProjectorName,
            (point, observation) =>
            {
                if (point == CatchUpProductionHookPoint.InvocationCompleted &&
                    observation.Cursor?.Value == recent.SortableUniqueIdValue)
                {
                    invocationB.TrySetResult(observation);
                }
                return Task.CompletedTask;
            });

        var execA = new OrleansDcbExecutor(
            _clusterA.Client,
            SharedStores.ReplicaA,
            SharedStores.Domain,
            serviceIdProvider: new FixedServiceIdProvider(ReplicaServiceA));
        var execB = new OrleansDcbExecutor(
            _clusterB.Client,
            SharedStores.ReplicaB,
            SharedStores.Domain,
            serviceIdProvider: new FixedServiceIdProvider(ReplicaServiceB));
        var stateA = grainA.GetStateAsync();
        var stateB = grainB.GetStateAsync();
        var scalarA = execA.QueryAsync(new WinnerQuery("recent"));
        var scalarB = execB.QueryAsync(new WinnerQuery("recent"));
        var listA = execA.QueryAsync(new WinnerListQuery());
        var listB = execB.QueryAsync(new WinnerListQuery());

        await Task.WhenAll(readGateA.Entered, readGateB.Entered).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stateA.IsCompleted);
        Assert.False(stateB.IsCompleted);

        // Release the replicas independently. Each first-query surface remains parked until that replica's own
        // invocation-owned traversal returns the fixed head; no SafeWindow delay or polling is part of this proof.
        readGateB.Release();
        var observedB = await invocationB.Task.WaitAsync(TimeSpan.FromSeconds(5));
        readGateA.Release();
        var observedA = await invocationA.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var states = await Task.WhenAll(stateA, stateB).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(states, state => Assert.True(state.IsSuccess, state.IsSuccess ? "" : state.GetException().ToString()));
        Assert.All(
            states,
            state =>
            {
                Assert.False(state.GetValue().IsSafeState);
                var payload = (FirstWinsProjector)state.GetValue().Payload;
                Assert.Equal("safe", payload.Winners["baseline"]);
                Assert.Equal("head", payload.Winners["recent"]);
                Assert.Equal(recent.SortableUniqueIdValue, state.GetValue().LastSortableUniqueId);
            });
        Assert.Equal(recent.SortableUniqueIdValue, observedA.Cursor?.Value);
        Assert.Equal(recent.SortableUniqueIdValue, observedB.Cursor?.Value);

        var scalars = await Task.WhenAll(scalarA, scalarB).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(scalars, scalar => Assert.True(scalar.IsSuccess, scalar.IsSuccess ? "" : scalar.GetException().ToString()));
        Assert.All(scalars, scalar => Assert.Equal("head", scalar.GetValue().Value));

        var lists = await Task.WhenAll(listA, listB).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(lists, list => Assert.True(list.IsSuccess, list.IsSuccess ? "" : list.GetException().ToString()));
        Assert.All(
            lists,
            list =>
            {
                var rows = list.GetValue().Items.ToList();
                Assert.Equal("safe", Assert.Single(rows, row => row.Id == "baseline").Value);
                Assert.Equal("head", Assert.Single(rows, row => row.Id == "recent").Value);
            });

        var safeA = (await grainA.GetStateAsync(canGetUnsafeState: false)).GetValue();
        var safeB = (await grainB.GetStateAsync(canGetUnsafeState: false)).GetValue();
        Assert.DoesNotContain("recent", ((FirstWinsProjector)safeA.Payload).Winners.Keys);
        Assert.DoesNotContain("recent", ((FirstWinsProjector)safeB.Payload).Winners.Keys);
    }

    private static async Task AssertScalarAndListAsync(ISekibanExecutor executor, string expectedWinner)
    {
        var scalar = await executor.QueryAsync(new WinnerQuery("team-1"));
        Assert.True(scalar.IsSuccess, scalar.IsSuccess ? "" : scalar.GetException().ToString());
        Assert.Equal(expectedWinner, scalar.GetValue().Value);

        var list = await executor.QueryAsync(new WinnerListQuery());
        Assert.True(list.IsSuccess, list.IsSuccess ? "" : list.GetException().ToString());
        var rows = list.GetValue().Items.ToList();
        var row = Assert.Single(rows, r => r.Id == "team-1");
        Assert.Equal(expectedWinner, row.Value);
    }

    private static async Task<(FirstWinsProjector State, bool IsSafeState)> PollUntilAsync(
        IMultiProjectionGrain grain, Func<FirstWinsProjector, bool> predicate)
    {
        for (var i = 0; i < 40; i++)
        {
            var rb = await grain.GetStateAsync();
            if (rb.IsSuccess)
            {
                var s = (FirstWinsProjector)rb.GetValue().Payload;
                if (predicate(s))
                {
                    return (s, rb.GetValue().IsSafeState);
                }
            }
            await Task.Delay(150);
        }
        var final = await grain.GetStateAsync();
        if (!final.IsSuccess)
        {
            throw new Xunit.Sdk.XunitException("grain.GetStateAsync failed: " + final.GetException());
        }
        return ((FirstWinsProjector)final.GetValue().Payload, final.GetValue().IsSafeState);
    }

    private static FirstWinsProjector GlobalReplay(IEnumerable<Event> events)
    {
        var payload = FirstWinsProjector.GenerateInitialPayload();
        foreach (var ev in events.OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal))
        {
            payload = FirstWinsProjector.Project(payload, ev, new List<ITag>(), SharedStores.Domain, new SortableUniqueId("0")).GetValue();
        }
        return payload;
    }

    private static Event CreateEvent(IEventPayload payload, DateTime timestamp) => new(
        payload,
        SortableUniqueId.Generate(timestamp, Guid.NewGuid()),
        payload.GetType().Name,
        Guid.NewGuid(),
        new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
        new List<string>());

    private static SerializableEvent ToSerializable(Event ev) => new(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ev.Payload, ev.Payload.GetType())),
        ev.SortableUniqueIdValue,
        ev.Id,
        ev.EventMetadata,
        ev.Tags.ToList(),
        ev.EventType);

    public record CreatedWithId(string Id, string Value) : IEventPayload;

    // Scalar + list query surfaces (separate code paths from GetStateAsync) over the same projector.
    public record WinnerResult(string Value);

    public record WinnerQuery(string Id) : IMultiProjectionQuery<FirstWinsProjector, WinnerQuery, WinnerResult>
    {
        public static ResultBox<WinnerResult> HandleQuery(
            FirstWinsProjector projector, WinnerQuery query, IQueryContext context) =>
            ResultBox.FromValue(new WinnerResult(projector.Winners.TryGetValue(query.Id, out var v) ? v : string.Empty));
    }

    public record WinnerRow(string Id, string Value);

    public record WinnerListQuery : IMultiProjectionListQuery<FirstWinsProjector, WinnerListQuery, WinnerRow>,
        Sekiban.Dcb.Queries.IQueryPagingParameter
    {
        public int? PageNumber { get; init; }
        public int? PageSize { get; init; }

        public static ResultBox<IEnumerable<WinnerRow>> HandleFilter(
            FirstWinsProjector projector, WinnerListQuery query, IQueryContext context) =>
            ResultBox.FromValue(projector.Winners.Select(kv => new WinnerRow(kv.Key, kv.Value)));

        public static ResultBox<IEnumerable<WinnerRow>> HandleSort(
            IEnumerable<WinnerRow> filtered, WinnerListQuery query, IQueryContext context) =>
            ResultBox.FromValue(filtered.OrderBy(r => r.Id, StringComparer.Ordinal).AsEnumerable());
    }

    [global::Orleans.GenerateSerializer]
    public record FirstWinsProjector : IMultiProjector<FirstWinsProjector>
    {
        [global::Orleans.Id(0)]
        public Dictionary<string, string> Winners { get; init; } = new();
        public static string MultiProjectorName => "g18-2cluster-first-wins";
        public static string MultiProjectorVersion => "1.0.0";
        public static FirstWinsProjector GenerateInitialPayload() => new();

        public static ResultBox<FirstWinsProjector> Project(
            FirstWinsProjector payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold)
        {
            if (ev.Payload is CreatedWithId created)
            {
                if (payload.Winners.ContainsKey(created.Id))
                {
                    return ResultBox.FromValue(payload);
                }
                return ResultBox.FromValue(payload with { Winners = new Dictionary<string, string>(payload.Winners) { [created.Id] = created.Value } });
            }
            return ResultBox.FromValue(payload);
        }
    }

    // Static shared authoritative event store + external checkpoint (serviceId=default) — the two clusters connect to the
    // SAME instances, while each keeps its own cluster-local grain state (per-silo memory storage).
    internal static class SharedStores
    {
        public static DcbDomainTypes Domain { get; private set; } = BuildDomain();
        public static InMemoryEventStore EventStore { get; private set; } = new(Domain.EventTypes);
        public static ReplicaEventStore ReplicaA { get; private set; } = new(EventStore);
        public static ReplicaEventStore ReplicaB { get; private set; } = new(EventStore);
        public static InMemoryMultiProjectionStateStore StateStore { get; } = new();

        public static void Reset()
        {
            Domain = BuildDomain();
            EventStore = new InMemoryEventStore(Domain.EventTypes);
            ReplicaA = new ReplicaEventStore(EventStore);
            ReplicaB = new ReplicaEventStore(EventStore);
            StateStore.DeleteAllAsync(FirstWinsProjector.MultiProjectorName).GetAwaiter().GetResult();
        }

        private static DcbDomainTypes BuildDomain()
        {
            var eventTypes = new SimpleEventTypes();
            eventTypes.RegisterEventType<CreatedWithId>("CreatedWithId");
            var multiProjectorTypes = new SimpleMultiProjectorTypes();
            multiProjectorTypes.RegisterProjector<FirstWinsProjector>();
            var queryTypes = new SimpleQueryTypes();
            queryTypes.RegisterQuery<WinnerQuery>();
            queryTypes.RegisterListQuery<WinnerListQuery>();
            return new DcbDomainTypes(
                eventTypes, new SimpleTagTypes(), new SimpleTagProjectorTypes(), new SimpleTagStatePayloadTypes(),
                multiProjectorTypes, queryTypes, new JsonSerializerOptions());
        }
    }

    internal sealed class ReplicaEventStore(InMemoryEventStore inner) : IEventStore
    {
        private ReadGate? _nextReadGate;

        internal ReadGate ParkNextRead()
        {
            var gate = new ReadGate();
            Assert.Null(Interlocked.CompareExchange(ref _nextReadGate, gate, null));
            return gate;
        }

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() =>
            inner.GetLatestSortableUniqueIdAsync();

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since = null) =>
            ReadAllSerializableEventsAsync(since, null);

        public async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount)
        {
            var gate = Interlocked.Exchange(ref _nextReadGate, null);
            if (gate is not null)
            {
                gate.SignalEntered();
                await gate.WaitForReleaseAsync();
            }

            return await inner.ReadAllSerializableEventsAsync(since, maxCount);
        }

        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) =>
            inner.WriteSerializableEventsAsync(events);

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => inner.ReadTagsAsync(tag);
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => inner.GetLatestTagAsync(tag);
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => inner.TagExistsAsync(tag);
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => inner.GetEventCountAsync(since);
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => inner.GetAllTagsAsync(tagGroup);
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) =>
            inner.ReadSerializableEventAsync(eventId);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null) => inner.ReadSerializableEventsByTagAsync(tag, since);

        internal sealed class ReadGate
        {
            private readonly TaskCompletionSource _entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource _release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            internal Task Entered => _entered.Task;
            internal void SignalEntered() => _entered.TrySetResult();
            internal void Release() => _release.TrySetResult();
            internal Task WaitForReleaseAsync() => _release.Task;
        }
    }

    private abstract class Configurator(ReplicaEventStore eventStore) : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton<DcbDomainTypes>(SharedStores.Domain);
                    services.AddSingleton<IEventStore>(eventStore); // replica proxy over SHARED authoritative events
                    services.AddSingleton<IMultiProjectionStateStore>(SharedStores.StateStore); // SHARED external checkpoint
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    services.AddTransient<GeneralMultiProjectionActorOptions>(_ =>
                        new GeneralMultiProjectionActorOptions { SafeWindowMs = 3000 });
                    services.AddSekibanDcbNativeRuntime();
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("OrleansStorage")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }

    private sealed class ConfiguratorA() : Configurator(SharedStores.ReplicaA);
    private sealed class ConfiguratorB() : Configurator(SharedStores.ReplicaB);
}
