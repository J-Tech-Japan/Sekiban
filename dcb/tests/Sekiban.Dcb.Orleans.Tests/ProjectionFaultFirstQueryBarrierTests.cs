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
using Sekiban.Dcb.ServiceId;
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
    public enum InterleaveOrder
    {
        BackgroundFirst,
        InvocationFirst
    }

    public enum FirstQuerySurface
    {
        State,
        Snapshot,
        Scalar,
        List
    }

    private static readonly FailableEventStore Store = new();
    private static readonly InMemoryMultiProjectionStateStore CheckpointStore = new();
    private TestCluster _cluster = null!;
    private ISekibanExecutor _executor = null!;
    private IClusterClient Client => _cluster.Client;

    public async Task InitializeAsync()
    {
        Store.Reset();
        CheckpointStore.Clear();
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

    [Theory]
    [InlineData(FirstQuerySurface.State)]
    [InlineData(FirstQuerySurface.Snapshot)]
    [InlineData(FirstQuerySurface.Scalar)]
    [InlineData(FirstQuerySurface.List)]
    public async Task ColdFirstQuery_ReachesFixedHeadWithoutWaitingForSafeWindow(FirstQuerySurface surface)
    {
        var safeEvent = DomainTypes.Event(poison: false, tick: 1_000);
        await Store.WriteSerializableEventsAsync(new List<SerializableEvent> { safeEvent });

        var grain = Client.GetGrain<IMultiProjectionGrain>(DomainTypes.FaultTestProjector.MultiProjectorName);
        var baseline = await grain.GetStateAsync();
        Assert.True(baseline.IsSuccess);
        Assert.True(baseline.GetValue().IsSafeState);
        Assert.Equal(1, ((DomainTypes.FaultTestProjector)baseline.GetValue().Payload).Count);
        Assert.True((await grain.PersistStateAsync()).IsSuccess);

        await grain.RequestDeactivationAsync();

        var inWindowEvent = DomainTypes.Event(poison: false, tick: DateTime.UtcNow.Ticks);
        await Store.WriteSerializableEventsAsync(new List<SerializableEvent> { inWindowEvent });
        var cold = Client.GetGrain<IMultiProjectionGrain>(DomainTypes.FaultTestProjector.MultiProjectorName);

        var firstQuery = surface switch
        {
            FirstQuerySurface.State => AssertStateAsync(cold),
            FirstQuerySurface.Snapshot => AssertSnapshotAsync(cold),
            FirstQuerySurface.Scalar => AssertScalarAsync(),
            FirstQuerySurface.List => AssertListAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(surface))
        };
        await firstQuery.WaitAsync(TimeSpan.FromSeconds(5));

        var served = await cold.GetStateAsync(canGetUnsafeState: true);
        Assert.True(served.IsSuccess);
        Assert.False(served.GetValue().IsSafeState);
        Assert.Equal(2, ((DomainTypes.FaultTestProjector)served.GetValue().Payload).Count);
        Assert.Equal(inWindowEvent.SortableUniqueIdValue, served.GetValue().LastSortableUniqueId);

        var safe = await cold.GetStateAsync(canGetUnsafeState: false);
        Assert.True(safe.IsSuccess);
        Assert.True(safe.GetValue().IsSafeState);
        Assert.Equal(1, ((DomainTypes.FaultTestProjector)safe.GetValue().Payload).Count);
        Assert.Equal(safeEvent.SortableUniqueIdValue, safe.GetValue().LastSortableUniqueId);

        async Task AssertStateAsync(IMultiProjectionGrain target) =>
            Assert.True((await target.GetStateAsync()).IsSuccess);

        async Task AssertSnapshotAsync(IMultiProjectionGrain target) =>
            Assert.True((await target.GetSnapshotJsonAsync()).IsSuccess);

        async Task AssertScalarAsync()
        {
            var result = await _executor.QueryAsync(new DomainTypes.FaultCountQuery());
            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.GetValue().Count);
        }

        async Task AssertListAsync()
        {
            var result = await _executor.QueryAsync(new DomainTypes.FaultRowListQuery());
            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.GetValue().TotalCount);
        }
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

        var stateResult = await grain.GetStateAsync();
        Assert.False(stateResult.IsSuccess);
        Assert.Contains("read failure", stateResult.GetException().ToString());
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

        // The streaming provider recovers. Because the failed gate attempt was not marked complete, the next call
        // performs another authoritative traversal and all four surfaces become available.
        Store.FailReads = false;
        Assert.True((await grain.GetStateAsync()).IsSuccess);
        Assert.True((await grain.GetSnapshotJsonAsync()).IsSuccess);
        Assert.True((await _executor.QueryAsync(new DomainTypes.FaultCountQuery())).IsSuccess);
        Assert.True((await _executor.QueryAsync(new DomainTypes.FaultRowListQuery())).IsSuccess);
    }

    [Fact]
    public async Task ShortReadWithoutError_FailsAllSurfacesWithStableGuard_ThenRecoversAtHead()
    {
        await Store.WriteSerializableEventsAsync(new List<SerializableEvent>
        {
            DomainTypes.Event(poison: false, tick: 10_100),
            DomainTypes.Event(poison: false, tick: 10_101)
        });
        Store.ShortReads = true;

        var grain = Client.GetGrain<IMultiProjectionGrain>(DomainTypes.FaultTestProjector.MultiProjectorName);

        var state = await grain.GetStateAsync();
        Assert.False(state.IsSuccess);
        Assert.Contains("did not reach the event-store head", state.GetException().ToString());

        var snapshot = await grain.GetSnapshotJsonAsync();
        Assert.False(snapshot.IsSuccess);
        Assert.Contains("did not reach the event-store head", snapshot.GetException().ToString());

        var scalar = await _executor.QueryAsync(new DomainTypes.FaultCountQuery());
        Assert.False(scalar.IsSuccess);
        Assert.Contains("did not reach the event-store head", scalar.GetException().ToString());

        var list = await _executor.QueryAsync(new DomainTypes.FaultRowListQuery());
        Assert.False(list.IsSuccess);
        Assert.Contains("did not reach the event-store head", list.GetException().ToString());

        Store.ShortReads = false;
        Assert.True((await grain.GetStateAsync()).IsSuccess);
        Assert.True((await grain.GetSnapshotJsonAsync()).IsSuccess);
        Assert.True((await _executor.QueryAsync(new DomainTypes.FaultCountQuery())).IsSuccess);
        Assert.True((await _executor.QueryAsync(new DomainTypes.FaultRowListQuery())).IsSuccess);
    }

    [Fact]
    public async Task InWindowPoisonAfterSafeBaseline_FaultsAllSurfaces_AndFreshActivationRestoresTheFault()
    {
        var safeEvent = DomainTypes.Event(poison: false, tick: 2_000);
        await Store.WriteSerializableEventsAsync(new List<SerializableEvent> { safeEvent });
        var seed = Client.GetGrain<IMultiProjectionGrain>(DomainTypes.FaultTestProjector.MultiProjectorName);
        Assert.True((await seed.GetStateAsync()).IsSuccess);
        Assert.True((await seed.PersistStateAsync()).IsSuccess);
        await seed.RequestDeactivationAsync();

        var poison = DomainTypes.Event(poison: true, tick: DateTime.UtcNow.Ticks);
        await Store.WriteSerializableEventsAsync(new List<SerializableEvent> { poison });
        var cold = Client.GetGrain<IMultiProjectionGrain>(DomainTypes.FaultTestProjector.MultiProjectorName);

        Assert.False((await cold.GetStateAsync()).IsSuccess);
        Assert.False((await cold.GetSnapshotJsonAsync()).IsSuccess);
        Assert.False((await _executor.QueryAsync(new DomainTypes.FaultCountQuery())).IsSuccess);
        Assert.False((await _executor.QueryAsync(new DomainTypes.FaultRowListQuery())).IsSuccess);

        await cold.RequestDeactivationAsync();
        var fresh = Client.GetGrain<IMultiProjectionGrain>(DomainTypes.FaultTestProjector.MultiProjectorName);
        var restored = await fresh.GetSnapshotJsonAsync();
        Assert.False(restored.IsSuccess);
        Assert.Contains(
            DomainTypes.FaultTestProjector.MultiProjectorName,
            restored.GetException().ToString());
    }

    [Fact]
    public async Task SafeEmptyUnsafeHead_CannotBypassAuthoritativeTraversalOfEarlierPoison()
    {
        var poison = DomainTypes.Event(poison: true, tick: DateTime.UtcNow.AddMilliseconds(-2).Ticks);
        var later = DomainTypes.Event(poison: false, tick: DateTime.UtcNow.Ticks);
        await Store.WriteSerializableEventsAsync(new List<SerializableEvent> { poison, later });

        // Park the activation's background catch-up behind a provider failure, then deliver only the later event through
        // the production stream entry point. The host is safe-empty but its unsafe max equals the durable store head.
        Store.FailReads = true;
        var grain = Client.GetGrain<IMultiProjectionGrain>(DomainTypes.FaultTestProjector.MultiProjectorName);
        await grain.AddEventsAsync(new[] { later });
        await Store.FailedStreamReadObserved.WaitAsync(TimeSpan.FromSeconds(5));
        Store.FailReads = false;

        // Raw unsafe metadata would return Count=1 here and permanently miss the earlier poison. The first-query barrier
        // must instead start from the safe beginning, traverse the store, and re-establish the projection fault.
        var result = await grain.GetStateAsync();
        Assert.False(result.IsSuccess);
        Assert.Contains(DomainTypes.FaultTestProjector.MultiProjectorName, result.GetException().ToString());
        Assert.True(Store.SuccessfulStreamReadCount > 0);
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

    [Theory]
    [InlineData(InterleaveOrder.BackgroundFirst)]
    [InlineData(InterleaveOrder.InvocationFirst)]
    public async Task RestoredEmptyStart_TimerAndBarrierInterleave_UsesProductionWiringExactlyOnce(
        InterleaveOrder order)
    {
        // Persist a real empty checkpoint record, then reactivate. Its nullable position is authoritative presence:
        // background and barrier must share the restored "beginning" START instead of replacing it with host inference.
        var seed = Client.GetGrain<IMultiProjectionGrain>(DomainTypes.FaultTestProjector.MultiProjectorName);
        Assert.True((await seed.PersistStateAsync()).IsSuccess);
        await seed.RequestDeactivationAsync();

        var durable = DomainTypes.Event(poison: false, tick: 20_000);
        await Store.WriteSerializableEventsAsync(new[] { durable });

        var backgroundBefore = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBackgroundBefore = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backgroundEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBackgroundEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backgroundRejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationBefore = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInvocation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationResults = new List<CatchUpProductionObservation>();
        var observationsSync = new object();

        using var hook = CatchUpProductionTestHooks.Register(
            DefaultServiceIdProvider.DefaultServiceId,
            DomainTypes.FaultTestProjector.MultiProjectorName,
            async (point, observation) =>
            {
                switch (point)
                {
                    case CatchUpProductionHookPoint.BackgroundBeforeGate:
                        backgroundBefore.TrySetResult();
                        if (order == InterleaveOrder.InvocationFirst)
                        {
                            await releaseBackgroundBefore.Task;
                        }
                        break;
                    case CatchUpProductionHookPoint.BackgroundEnteredGate:
                        backgroundEntered.TrySetResult();
                        if (order == InterleaveOrder.BackgroundFirst)
                        {
                            await releaseBackgroundEntered.Task;
                        }
                        break;
                    case CatchUpProductionHookPoint.BackgroundRejectedAsSuperseded:
                        backgroundRejected.TrySetResult();
                        break;
                    case CatchUpProductionHookPoint.InvocationBeforeGate:
                        invocationBefore.TrySetResult();
                        break;
                    case CatchUpProductionHookPoint.InvocationEnteredGate:
                        invocationEntered.TrySetResult();
                        if (order == InterleaveOrder.InvocationFirst)
                        {
                            await releaseInvocation.Task;
                        }
                        break;
                    case CatchUpProductionHookPoint.InvocationCompleted:
                        lock (observationsSync)
                        {
                            invocationResults.Add(observation);
                        }
                        break;
                }
            });

        var cold = Client.GetGrain<IMultiProjectionGrain>(DomainTypes.FaultTestProjector.MultiProjectorName);
        var stateTask = cold.GetStateAsync();
        var snapshotTask = cold.GetSnapshotJsonAsync();

        if (order == InterleaveOrder.BackgroundFirst)
        {
            await backgroundEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await invocationBefore.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(stateTask.IsCompleted);
            releaseBackgroundEntered.TrySetResult();
        }
        else
        {
            await backgroundBefore.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await invocationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(stateTask.IsCompleted);
            releaseBackgroundBefore.TrySetResult();
            releaseInvocation.TrySetResult();
            await backgroundRejected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var state = await stateTask.WaitAsync(TimeSpan.FromSeconds(5));
        var snapshot = await snapshotTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(state.IsSuccess, state.IsSuccess ? "" : state.GetException().ToString());
        Assert.True(snapshot.IsSuccess, snapshot.IsSuccess ? "" : snapshot.GetException().ToString());
        Assert.Equal(durable.SortableUniqueIdValue, state.GetValue().LastSortableUniqueId);

        CatchUpProductionObservation firstInvocation;
        lock (observationsSync)
        {
            firstInvocation = Assert.Single(invocationResults);
        }
        Assert.Equal(CatchUpStartPositionSource.RestoredCheckpoint, firstInvocation.Start?.Source);
        Assert.Null(firstInvocation.Start?.StartPosition);
        Assert.Equal(durable.SortableUniqueIdValue, firstInvocation.Cursor?.Value);

        // The two first callers shared one production _firstQueryGate invocation. Together with the resolver's
        // nullable-presence race proof, this demonstrates that the restored-null START is leased exactly once.
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
                    services.AddSingleton<IMultiProjectionStateStore>(CheckpointStore);
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
                    services.AddSingleton<Sekiban.Dcb.Snapshots.IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions { SafeWindowMs = 20_000 });
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
    private sealed class FailableEventStore : IEventStore, IStreamingSerializableEventStore
    {
        private readonly InMemoryEventStore _inner = new();
        public volatile bool FailReads;
        public volatile bool FailHeadReads;
        public volatile bool ShortReads;
        private int _successfulStreamReadCount;
        private TaskCompletionSource _failedStreamReadObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static readonly InvalidOperationException ReadError = new("injected event-store read failure");
        private static readonly InvalidOperationException HeadError = new("injected event-store head-read failure");
        public Task FailedStreamReadObserved => _failedStreamReadObserved.Task;
        public int SuccessfulStreamReadCount => Volatile.Read(ref _successfulStreamReadCount);

        public void Reset()
        {
            FailReads = false;
            FailHeadReads = false;
            ShortReads = false;
            _successfulStreamReadCount = 0;
            _failedStreamReadObserved =
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _inner.Clear();
        }

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() =>
            FailHeadReads
                ? Task.FromResult(ResultBox.Error<string>(HeadError))
                : _inner.GetLatestSortableUniqueIdAsync();

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            ReadAllSerializableEventsAsync(since, null);

        public async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount)
        {
            if (FailReads)
            {
                return ResultBox.Error<IEnumerable<SerializableEvent>>(ReadError);
            }

            var result = await _inner.ReadAllSerializableEventsAsync(since, maxCount);
            if (!ShortReads || !result.IsSuccess)
            {
                return result;
            }

            // A stable, error-free lag: the first call exposes one row, then the provider keeps returning an empty
            // short read even though the independently-read fixed head is later.
            return ResultBox.FromValue(
                since is null
                    ? result.GetValue().Take(1)
                    : Enumerable.Empty<SerializableEvent>());
        }

        public async Task<ResultBox<SerializableEventStreamReadResult>> StreamAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount,
            Func<SerializableEvent, ValueTask> onEvent,
            CancellationToken cancellationToken = default)
        {
            if (FailReads)
            {
                _failedStreamReadObserved.TrySetResult();
                return ResultBox.Error<SerializableEventStreamReadResult>(ReadError);
            }

            var result = await ReadAllSerializableEventsAsync(since, maxCount);
            if (!result.IsSuccess)
            {
                return ResultBox.Error<SerializableEventStreamReadResult>(result.GetException());
            }

            var events = result.GetValue().ToList();
            foreach (var ev in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await onEvent(ev);
            }

            Interlocked.Increment(ref _successfulStreamReadCount);
            return ResultBox.FromValue(
                new SerializableEventStreamReadResult(
                    events.Count,
                    events.Count == 0 ? since?.Value : events[^1].SortableUniqueIdValue));
        }

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
