using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;
using DomainTypes = Sekiban.Dcb.Orleans.Tests.ProjectionFaultOrleansTests;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     The first-query barrier must never SWALLOW a head/read/RefreshAsync failure into an empty success. When a fresh
///     activation restored no descriptor, the first query synchronously reads the authoritative event-store head and
///     catches up; if the head read fails, the catch-up read fails, or catch-up cannot reach the head, the query fails
///     CLOSED with the original exception — and the barrier is not marked complete, so a later query does not bypass it
///     into empty/current state. Once the store recovers, a later query catches up and succeeds.
/// </summary>
public class ProjectionFaultFirstQueryBarrierTests : IAsyncLifetime
{
    private static readonly FailableEventStore Store = new();
    private TestCluster _cluster = null!;
    private ISekibanExecutor _executor = null!;
    private IClusterClient Client => _cluster.Client;

    public async Task InitializeAsync()
    {
        Store.Reset();
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var id = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"BarrierCluster-{id}";
        builder.Options.ServiceId = $"BarrierService-{id}";
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
        _executor = new OrleansDcbExecutor(Client, Store, DomainTypes.CreateDomain());
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
        {
            await _cluster.StopAllSilosAsync();
        }
    }

    [Fact]
    public async Task HeadReadFailure_FailsTheFirstQueryClosed_WithTheOriginalException()
    {
        // Durable (healthy) events exist, but the authoritative head read is failing.
        await Store.WriteSerializableEventsAsync(new List<SerializableEvent> { DomainTypes.Event(poison: false, tick: 9_000) });
        Store.FailHeadReads = true;

        var grain = Client.GetGrain<IMultiProjectionGrain>(DomainTypes.FaultTestProjector.MultiProjectorName);

        var state = await grain.GetSnapshotJsonAsync();
        Assert.False(state.IsSuccess); // not an empty success
        var ex = state.GetException();
        Assert.Contains("InvalidOperationException", ex.ToString());   // original exception TYPE preserved
        Assert.Contains("head-read failure", ex.Message);              // original message/context preserved
    }

    [Fact]
    public async Task EventReadFailure_FailsAllFirstQuerySurfacesClosed_AndALaterQueryDoesNotBypass()
    {
        // Head is readable, but catch-up event reads fail: the projection is behind the poison-free head and cannot
        // prove it reached it, so every first query surface must fail closed rather than answer empty.
        await Store.WriteSerializableEventsAsync(new List<SerializableEvent>
        {
            DomainTypes.Event(poison: false, tick: 10_000), DomainTypes.Event(poison: false, tick: 10_001)
        });
        Store.FailReads = true;

        var grain = Client.GetGrain<IMultiProjectionGrain>(DomainTypes.FaultTestProjector.MultiProjectorName);

        var firstState = await grain.GetSnapshotJsonAsync();
        Assert.False(firstState.IsSuccess);
        var stateEx = firstState.GetException();
        Assert.Contains("InvalidOperationException", stateEx.ToString()); // original exception TYPE preserved
        Assert.Contains("read failure", stateEx.Message);                 // original message/context preserved

        var countResult = await _executor.QueryAsync(new DomainTypes.FaultCountQuery());
        Assert.False(countResult.IsSuccess);
        Assert.Contains("read failure", countResult.GetException().ToString());
        var listResult = await _executor.QueryAsync(new DomainTypes.FaultRowListQuery());
        Assert.False(listResult.IsSuccess);
        Assert.Contains("read failure", listResult.GetException().ToString());

        // A later query must NOT bypass the barrier into empty success while the read is still failing: the barrier was
        // not marked complete on the transient failure, so the query re-runs it and fails closed again.
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
        Assert.False((await _executor.QueryAsync(new DomainTypes.FaultCountQuery())).IsSuccess);
        Assert.False((await _executor.QueryAsync(new DomainTypes.FaultRowListQuery())).IsSuccess);
    }

    [Fact]
    public async Task FailFirstHeadRead_ThenRecover_ReEstablishesThePoisonFaultOnALaterQuery_NeverEmpty()
    {
        // Durable POISON sits in the store. The first queries fail closed on a transient read failure; when the store
        // recovers, a later query re-runs the barrier, catches up, re-folds the poison and fails with the FAULT — the
        // projection is never bricked and never answers empty.
        await Store.WriteSerializableEventsAsync(new List<SerializableEvent> { DomainTypes.Event(poison: true, tick: 12_000) });
        Store.FailReads = true;

        var grain = Client.GetGrain<IMultiProjectionGrain>(DomainTypes.FaultTestProjector.MultiProjectorName);

        var whileFailing = await grain.GetSnapshotJsonAsync();
        Assert.False(whileFailing.IsSuccess);
        Assert.Contains("read failure", whileFailing.GetException().ToString());

        // Store recovers: the barrier retries on the next query, folds the poison and fails with the fault (not empty).
        Store.FailReads = false;
        var afterRecovery = await grain.GetSnapshotJsonAsync();
        Assert.False(afterRecovery.IsSuccess);
        Assert.Contains(DomainTypes.FaultTestProjector.MultiProjectorName, afterRecovery.GetException().ToString());
        Assert.False((await _executor.QueryAsync(new DomainTypes.FaultCountQuery())).IsSuccess);
        Assert.False((await _executor.QueryAsync(new DomainTypes.FaultRowListQuery())).IsSuccess);
    }

    [Fact]
    public async Task ConcurrentFirstCallers_AllObserveFailure_NoneBypassesIntoEmptySuccess()
    {
        await Store.WriteSerializableEventsAsync(new List<SerializableEvent> { DomainTypes.Event(poison: false, tick: 11_000) });
        Store.FailReads = true;

        var grain = Client.GetGrain<IMultiProjectionGrain>(DomainTypes.FaultTestProjector.MultiProjectorName);

        var a = grain.GetSnapshotJsonAsync();
        var b = grain.GetSnapshotJsonAsync();
        var results = await Task.WhenAll(a, b);
        Assert.All(results, r => Assert.False(r.IsSuccess));
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton(_ => DomainTypes.CreateDomain());
                    services.AddSingleton<IEventStore>(Store);
                    services.AddSingleton<IMultiProjectionStateStore, InMemoryMultiProjectionStateStore>();
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
                    services.AddSingleton<Sekiban.Dcb.Snapshots.IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions { SafeWindowMs = 1 });
                    services.AddSekibanDcbNativeRuntime();
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("OrleansStorage")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }

    /// <summary>
    ///     Delegates to a real in-memory event store, but injects deterministic read failures. FailHeadReads makes the
    ///     authoritative head read fail; FailReads makes the catch-up event reads fail. Writes always succeed so durable
    ///     events can be present behind a failing read.
    /// </summary>
    private sealed class FailableEventStore : IEventStore
    {
        private readonly InMemoryEventStore _inner = new();
        public volatile bool FailReads;
        public volatile bool FailHeadReads;
        private static readonly InvalidOperationException ReadError = new("injected event-store read failure");
        private static readonly InvalidOperationException HeadError = new("injected event-store head-read failure");

        public void Reset()
        {
            FailReads = false;
            FailHeadReads = false;
            _inner.Clear();
        }

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() =>
            FailHeadReads
                ? Task.FromResult(ResultBox.Error<string>(HeadError))
                : _inner.GetLatestSortableUniqueIdAsync();

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            ReadAllSerializableEventsAsync(since, null);

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount) =>
            FailReads
                ? Task.FromResult(ResultBox.Error<IEnumerable<SerializableEvent>>(ReadError))
                : _inner.ReadAllSerializableEventsAsync(since, maxCount);

        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) =>
            _inner.WriteSerializableEventsAsync(events);

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => _inner.ReadTagsAsync(tag);
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => _inner.GetLatestTagAsync(tag);
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => _inner.TagExistsAsync(tag);
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => _inner.GetEventCountAsync(since);
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => _inner.GetAllTagsAsync(tagGroup);
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) =>
            _inner.ReadSerializableEventAsync(eventId);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null) => _inner.ReadSerializableEventsByTagAsync(tag, since);
    }
}
