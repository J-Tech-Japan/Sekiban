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
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Snapshots;
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
        builder.Options.ClusterId = $"G18-2cluster-{name}-{uniqueId}";
        builder.Options.ServiceId = $"G18-2cluster-{name}-{uniqueId}";
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
            var multiProjectorTypes = new SimpleMultiProjectorTypes();
            multiProjectorTypes.RegisterProjector<FirstWinsProjector>();
            return new DcbDomainTypes(
                eventTypes, new SimpleTagTypes(), new SimpleTagProjectorTypes(), new SimpleTagStatePayloadTypes(),
                multiProjectorTypes, new SimpleQueryTypes(), new JsonSerializerOptions());
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
                    services.AddSingleton<IEventStore>(SharedStores.EventStore);           // SHARED authoritative events
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
}
