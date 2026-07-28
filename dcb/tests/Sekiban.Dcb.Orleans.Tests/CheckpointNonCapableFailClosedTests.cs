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
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     SEK-G20 fail-closed fallback: when the external checkpoint store does NOT advertise the generation/tombstone CAS
///     capability, a retrograde full rebuild cannot be made cross-cluster safe. Rather than silently rebuild (unsafe when
///     the store is shared), the grain enters the G14 persisted-fault path — every query fails closed until an operator
///     reset. This test drives a retrograde rebuild against a NON-capable store and asserts all query surfaces fail.
/// </summary>
public class CheckpointNonCapableFailClosedTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private IClusterClient _client => _cluster.Client;

    public async Task InitializeAsync()
    {
        Shared.Reset();
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var id = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"G20-noncap-{id}";
        builder.Options.ServiceId = $"G20-noncap-{id}";
        builder.AddSiloBuilderConfigurator<Configurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }

    [Fact]
    public async Task NonCapableStore_RetrogradeRebuild_FailsClosedToG14Fault()
    {
        var grain = _client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);
        var executor = new OrleansDcbExecutor(_client, Shared.EventStore, Shared.Domain);

        var safe = ToSerializable(CreateEvent(new Counted("safe"), DateTime.UtcNow.AddSeconds(-30)));
        await Shared.EventStore.WriteSerializableEventsAsync(new[] { safe });
        await grain.RefreshAsync();
        Assert.True((await grain.PersistStateAsync()).IsSuccess);

        // Deliver a globally-earlier already-safe event out of order -> RebuildRequired -> retrograde full rebuild. On a
        // NON-capable store this fails closed to the G14 fault path.
        var earlier = ToSerializable(CreateEvent(new Counted("earlier"), DateTime.UtcNow.AddSeconds(-31)));
        await Shared.EventStore.WriteSerializableEventsAsync(new[] { earlier });
        await grain.AddEventsAsync(new[] { earlier });

        // All query surfaces fail closed (a persisted fault requiring operator reset); never a silent rebuild that could
        // re-contaminate a shared row.
        Assert.False((await grain.GetStateAsync()).IsSuccess);
        Assert.False((await executor.QueryAsync(new CountQuery())).IsSuccess);
        Assert.False((await executor.QueryAsync(new CountListQuery())).IsSuccess);

        // The fault is durable: a fresh activation stays faulted (fails closed) rather than serving a stale success.
        await grain.RequestDeactivationAsync();
        await Task.Delay(1000);
        var grain2 = _client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);
        Assert.False((await grain2.GetStateAsync()).IsSuccess);
    }

    private static Event CreateEvent(IEventPayload payload, DateTime timestamp) => new(
        payload, SortableUniqueId.Generate(timestamp, Guid.NewGuid()), payload.GetType().Name,
        Guid.NewGuid(), new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"), new List<string>());

    private static SerializableEvent ToSerializable(Event ev) => new(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ev.Payload, ev.Payload.GetType())),
        ev.SortableUniqueIdValue, ev.Id, ev.EventMetadata, ev.Tags.ToList(), ev.EventType);

    public record Counted(string Tag) : IEventPayload;
    public record CountResult(int Count);
    public record CountRow(int Index);

    public record CountQuery : IMultiProjectionQuery<CountProjector, CountQuery, CountResult>
    {
        public static ResultBox<CountResult> HandleQuery(CountProjector p, CountQuery q, IQueryContext c) =>
            ResultBox.FromValue(new CountResult(p.Count));
    }

    public record CountListQuery : IMultiProjectionListQuery<CountProjector, CountListQuery, CountRow>, IQueryPagingParameter
    {
        public int? PageNumber { get; init; }
        public int? PageSize { get; init; }
        public static ResultBox<IEnumerable<CountRow>> HandleFilter(CountProjector p, CountListQuery q, IQueryContext c) =>
            ResultBox.FromValue(Enumerable.Range(0, p.Count).Select(i => new CountRow(i)));
        public static ResultBox<IEnumerable<CountRow>> HandleSort(IEnumerable<CountRow> f, CountListQuery q, IQueryContext c) =>
            ResultBox.FromValue(f);
    }

    [global::Orleans.GenerateSerializer]
    public record CountProjector : IMultiProjector<CountProjector>
    {
        [global::Orleans.Id(0)]
        public int Count { get; init; }
        public static string MultiProjectorName => "g20-noncap-count";
        public static string MultiProjectorVersion => "1.0.0";
        public static CountProjector GenerateInitialPayload() => new();
        public static ResultBox<CountProjector> Project(
            CountProjector payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) =>
            ResultBox.FromValue(payload with { Count = payload.Count + 1 });
    }

    // A NON-capable external store: implements only IMultiProjectionStateStore (delegates to InMemory) and deliberately
    // does NOT implement IGenerationAwareCheckpointStore, so CheckpointCapabilityResolver reports it uncapable.
    internal sealed class NonCapableStore : IMultiProjectionStateStore
    {
        private readonly InMemoryMultiProjectionStateStore _inner = new();
        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(string p, string v, CancellationToken ct = default) => _inner.GetLatestForVersionAsync(p, v, ct);
        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(string p, CancellationToken ct = default) => _inner.GetLatestAnyVersionAsync(p, ct);
        public Task<ResultBox<bool>> UpsertAsync(MultiProjectionStateRecord r, int off = 1_000_000, CancellationToken ct = default) => _inner.UpsertAsync(r, off, ct);
        public Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(CancellationToken ct = default) => _inner.ListAllAsync(ct);
        public Task<ResultBox<bool>> DeleteAsync(string p, string v, CancellationToken ct = default) => _inner.DeleteAsync(p, v, ct);
        public Task<ResultBox<int>> DeleteAllAsync(string? p = null, CancellationToken ct = default) => _inner.DeleteAllAsync(p, ct);
        public Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(MultiProjectionStateRecord r, CancellationToken ct = default) => _inner.OpenStateDataReadStreamAsync(r, ct);
        public Task<ResultBox<bool>> UpsertFromStreamAsync(MultiProjectionStateWriteRequest req, Stream s, int off, CancellationToken ct = default) => _inner.UpsertFromStreamAsync(req, s, off, ct);
    }

    internal static class Shared
    {
        public static DcbDomainTypes Domain { get; private set; } = BuildDomain();
        public static InMemoryEventStore EventStore { get; private set; } = new(Domain.EventTypes);
        public static NonCapableStore StateStore { get; private set; } = new();

        public static void Reset()
        {
            Domain = BuildDomain();
            EventStore = new InMemoryEventStore(Domain.EventTypes);
            StateStore = new NonCapableStore();
        }

        private static DcbDomainTypes BuildDomain()
        {
            var eventTypes = new SimpleEventTypes();
            eventTypes.RegisterEventType<Counted>("Counted");
            var mp = new SimpleMultiProjectorTypes();
            mp.RegisterProjector<CountProjector>();
            var q = new SimpleQueryTypes();
            q.RegisterQuery<CountQuery>();
            q.RegisterListQuery<CountListQuery>();
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
                    services.AddSingleton<DcbDomainTypes>(Shared.Domain);
                    services.AddSingleton<IEventStore>(Shared.EventStore);
                    services.AddSingleton<IMultiProjectionStateStore>(Shared.StateStore);
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
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
