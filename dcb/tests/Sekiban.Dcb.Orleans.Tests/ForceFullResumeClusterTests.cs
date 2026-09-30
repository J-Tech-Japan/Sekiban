using System.Diagnostics;
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

/// <summary>SEK-G92: forced restart and first-query handoff resume at SAFE on the same native host.</summary>
public class ForceFullResumeClusterTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private IClusterClient Client => _cluster.Client;
    public Task InitializeAsync() { Env.Reset(); return Task.CompletedTask; }
    private async Task StartAsync()
    {
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.Options.ClusterId = builder.Options.ServiceId = "G92-" + Guid.NewGuid().ToString("N");
        builder.AddSiloBuilderConfigurator<Configurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }
    public async Task DisposeAsync()
    {
        if (_cluster is null) return;
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }
    private IResumeTransitionGrain Grain => Client.GetGrain<IResumeTransitionGrain>(CountProjector.MultiProjectorName);
    private static SerializableEvent CountEvent(string name, DateTime timestamp) =>
        ToSerializable(CreateEvent(new Counted(name), timestamp));
    private static IDisposable Observe(Func<CatchUpProductionHookPoint, CatchUpProductionObservation, Task> observer) =>
        CatchUpProductionTestHooks.Register(Sekiban.Dcb.ServiceId.DefaultServiceIdProvider.DefaultServiceId,
            CountProjector.MultiProjectorName, observer);

    private async Task PrepareTombstoneAsync(int tailEvents)
    {
        var t0 = DateTime.UtcNow.AddHours(-1);
        await Env.EventStore.WriteSerializableEventsAsync(Enumerable.Range(0, 30).Select(i => CountEvent($"base{i}", t0.AddMilliseconds(i))));
        await Grain.RefreshAsync();
        Assert.True((await Grain.PersistStateAsync()).IsSuccess);
        Assert.Equal(30, ((CountProjector)(await Grain.GetStateAsync()).GetValue().Payload).Count);
        var slot = (await Env.GatingStore.ReadCheckpointSlotAsync(CountProjector.MultiProjectorName, "1.0.0")).GetValue();
        var tombstone = await Env.GatingStore.InvalidateWithTombstoneAsync(CountProjector.MultiProjectorName, "1.0.0", CheckpointExpectation.FromSlot(slot));
        Assert.Equal(CheckpointCasStatus.Committed, tombstone.Status);
        await Env.EventStore.WriteSerializableEventsAsync(Enumerable.Range(30, tailEvents).Select(i => CountEvent($"tail{i}", t0.AddMilliseconds(i))));
        await Grain.RequestDeactivationAsync();
        await Task.Delay(1000);
        Assert.True((await Env.GatingStore.ReadCheckpointSlotAsync(CountProjector.MultiProjectorName, "1.0.0")).GetValue().IsTombstoned);
        Env.Store.ResetReads();
    }

    [Theory]
    [InlineData(200000, 5000, "8")]
    [InlineData(1000, 5000, "8")]
    [InlineData(1000, 5000, "8,25")]
    [InlineData(1000, 5000, "20")]
    [InlineData(1000, 500, "20")]
    [InlineData(200000, 500, "20")]
    public async Task PartialForcedRestart_CountsDistinctEventsWithoutRebuilding(int cacheSize, int persist, string failAt)
    {
        Env.CacheSize = cacheSize;
        Env.HotPersist = persist;
        await StartAsync();
        await PrepareTombstoneAsync(3000);
        Env.Store.FailOnReads = failAt.Split(',').Select(int.Parse).ToHashSet();
        var rebuilds = 0;
        var recreates = 0;
        var starts = 0;
        using var hook = Observe((point, _) =>
        {
            if (point.ToString() == "RebuildRequiredDetected") Interlocked.Increment(ref rebuilds);
            if (point.ToString() == "HostRecreated") Interlocked.Increment(ref recreates);
            if (point == CatchUpProductionHookPoint.BackgroundStarted) Interlocked.Increment(ref starts);
            return Task.CompletedTask;
        });
        MultiProjectionState? state = null;
        await PollUntilAsync(async () =>
        {
            var result = await Grain.GetStateAsync(canGetUnsafeState: false, waitForCatchUp: false);
            if (result.IsSuccess) state = result.GetValue();
            return state is not null;
        });
        Assert.Equal(failAt.Split(',').Length, Env.Store.Thrown);
        Assert.True(starts >= 2, "Injected read failure must abort and restart the timer run.");
        Assert.Equal(3030, ((CountProjector)state!.Payload).Count);
        Assert.Equal(3030, ((CountProjector)(await Grain.GetStateAsync(canGetUnsafeState: true)).GetValue().Payload).Count);
        Assert.Equal(3030, (await Grain.GetStatusAsync()).EventsProcessed);
        Assert.True((await Grain.PersistStateAsync()).IsSuccess);
        var record = (await Env.GatingStore.GetLatestForVersionAsync(CountProjector.MultiProjectorName, "1.0.0")).GetValue();
        Assert.True(record.HasValue);
        Assert.Equal(3030, record.GetValue().EventsProcessed);
        Assert.Equal(0, rebuilds);
        Assert.Equal(0, recreates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureInheritsNullTimerLease_ResumesSafeAndRetainsUnsafeHandoff(bool recentTail)
    {
        // Large safe window keeps the handoff events recent throughout a slow cluster run; fixture is < cache 1000.
        if (recentTail) Env.SafeWindowMs = 300_000;
        await StartAsync();
        var t0 = DateTime.UtcNow;
        var safe = CountEvent("safe", t0.AddHours(-1));
        var tailTime = recentTail ? t0.AddSeconds(-10) : t0.AddMinutes(-10);
        var tail = Enumerable.Range(0, 300).Select(i => CountEvent($"tail{i}", tailTime.AddMilliseconds(i))).ToArray();
        await Env.EventStore.WriteSerializableEventsAsync(new[] { safe }.Concat(tail));
        var parked = new TaskCompletionSource<CatchUpProductionObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationBefore = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationRead = new TaskCompletionSource<CatchUpProductionObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rebuilds = 0;
        var recreates = 0;
        var enters = 0;
        using var hook = Observe(async (point, observation) =>
        {
            if (point.ToString() == "RebuildRequiredDetected") Interlocked.Increment(ref rebuilds);
            if (point.ToString() == "HostRecreated") Interlocked.Increment(ref recreates);
            if (point == CatchUpProductionHookPoint.BackgroundEnteredGate && Interlocked.Increment(ref enters) == 3)
            {
                parked.TrySetResult(observation);
                await release.Task;
            }
            if (point == CatchUpProductionHookPoint.InvocationBeforeGate) invocationBefore.TrySetResult();
            if (point == CatchUpProductionHookPoint.InvocationEnteredGate) invocationEntered.TrySetResult();
            if (point == CatchUpProductionHookPoint.InvocationBeforeRead) invocationRead.TrySetResult(observation);
        });
        try
        {
            // Status activates without initiating a first-query Ensure. Timer has applied two batches at the hold.
            await Grain.GetStatusAsync();
            var timer = await parked.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Null(timer.Start!.StartPosition);
            Assert.NotNull(timer.Cursor);
            SerializableEvent? late = null;
            if (recentTail)
            {
                late = CountEvent("late-earlier", tailTime.AddMilliseconds(50.5));
                Assert.True(new SortableUniqueId(late.SortableUniqueIdValue).IsEarlierThanOrEqual(timer.Cursor!));
                Assert.True(new SortableUniqueId(late.SortableUniqueIdValue).IsLaterThan(new SortableUniqueId(safe.SortableUniqueIdValue)));
                await Env.EventStore.WriteSerializableEventsAsync(new[] { late });
            }
            var query = Grain.GetStateAsync(canGetUnsafeState: true);
            await invocationBefore.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Both paths share a gate: release timer BEFORE awaiting invocation gate entry.
            release.TrySetResult();
            await invocationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var start = (await invocationRead.Task.WaitAsync(TimeSpan.FromSeconds(10))).Start!;
            Assert.Equal(CatchUpStartPositionSource.InferredCheckpoint, start.Source);
            Assert.Equal(recentTail ? safe.SortableUniqueIdValue : tail[298].SortableUniqueIdValue, start.StartPosition?.Value);
            var result = await query.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
            var expected = late is null ? 301 : 302;
            Assert.Equal(expected, ((CountProjector)result.GetValue().Payload).Count);
            Assert.Equal(expected, (await Grain.GetStatusAsync()).EventsProcessed);
            Assert.Equal(0, rebuilds);
            Assert.Equal(0, recreates);
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData("restore")]
    [InlineData("rearm")]
    [InlineData("fresh-tombstone")]
    public async Task HostLifetime_ForcedLeaseMatchesPristineTransition(string transition)
    {
        await StartAsync();
        var safe = CountEvent("safe", DateTime.UtcNow.AddHours(-1));
        if (transition == "fresh-tombstone") await PrepareTombstoneAsync(0);
        else
        {
            await Env.EventStore.WriteSerializableEventsAsync(new[] { safe });
            await Grain.RefreshAsync();
            Assert.True((await Grain.PersistStateAsync()).IsSuccess);
            if (transition == "restore")
            {
                await Grain.RequestDeactivationAsync();
                await Task.Delay(1000);
                // Wait for activation and its normal restored-checkpoint timer to complete before forced seam.
                await Grain.GetStateAsync();
                Assert.Equal(StateRestoreSource.ExternalStore, (await Grain.GetHealthStatusAsync()).StateRestoreSource);
            }
        }
        var started = new TaskCompletionSource<CatchUpProductionObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var hook = Observe((point, observation) =>
        {
            if (point == CatchUpProductionHookPoint.BackgroundStarted) started.TrySetResult(observation);
            return Task.CompletedTask;
        });
        if (transition == "fresh-tombstone") await Grain.GetStatusAsync();
        else await Grain.ForceRestartForTestAsync(rearm: transition == "rearm");
        var lease = (await started.Task.WaitAsync(TimeSpan.FromSeconds(10))).Start!;
        if (transition == "fresh-tombstone")
        {
            Assert.Null(lease.StartPosition);
            Assert.Equal(CatchUpStartPositionSource.FullReplay, lease.Source);
        }
        else
        {
            Assert.Equal(safe.SortableUniqueIdValue, lease.StartPosition?.Value);
            Assert.Equal(CatchUpStartPositionSource.InferredCheckpoint, lease.Source);
        }
    }

    internal static async Task PollUntilAsync(Func<Task<bool>> predicate)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(60))
        {
            if (await predicate()) return;
            await Task.Delay(100);
        }
        Assert.Fail("Timed out waiting for background settlement/fault");
    }

    private static Event CreateEvent(IEventPayload payload, DateTime timestamp) => new(
        payload, SortableUniqueId.Generate(timestamp, Guid.NewGuid()), payload.GetType().Name,
        Guid.NewGuid(), new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"), new List<string>());

    private static SerializableEvent ToSerializable(Event ev) => new(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ev.Payload, ev.Payload.GetType())),
        ev.SortableUniqueIdValue, ev.Id, ev.EventMetadata, ev.Tags.ToList(), ev.EventType);

    public record Counted(string Tag) : IEventPayload;
    [global::Orleans.GenerateSerializer]
    public record CountProjector : IMultiProjector<CountProjector>
    {
        [global::Orleans.Id(0)]
        public int Count { get; init; }
        public static string MultiProjectorName => "g92-resume-count";
        public static string MultiProjectorVersion => "1.0.0";
        public static CountProjector GenerateInitialPayload() => new();
        public static ResultBox<CountProjector> Project(
            CountProjector payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) =>
            ev.Payload is Counted { Tag: "poison" }
                ? ResultBox.Error<CountProjector>(new InvalidOperationException("tail poison"))
                : ResultBox.FromValue(payload with { Count = payload.Count + 1 });
    }

    public sealed class FlakyStore(IEventStore inner) : IEventStore
    {
        public HashSet<int> FailOnReads { get; set; } = [];
        private int _reads;
        private int _thrown;
        public int Thrown => Volatile.Read(ref _thrown);
        public void ResetReads() { Interlocked.Exchange(ref _reads, 0); Interlocked.Exchange(ref _thrown, 0); }
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            ReadAllSerializableEventsAsync(since, null);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount)
        {
            var read = Interlocked.Increment(ref _reads);
            if (FailOnReads.Contains(read))
            {
                Interlocked.Increment(ref _thrown);
                throw new TimeoutException($"SEK-G92 transient read #{read}");
            }
            return ReadSortedAsync(since, maxCount);
        }
        private async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSortedAsync(SortableUniqueId? since, int? maxCount)
        {
            var all = await inner.ReadAllSerializableEventsAsync(since, null);
            if (!all.IsSuccess) return all;
            // Real stores sort globally before limiting, so late commits must not be skipped by insertion-order batches.
            var sorted = all.GetValue().OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal);
            return ResultBox.FromValue<IEnumerable<SerializableEvent>>((maxCount is { } m ? sorted.Take(m) : sorted).ToList());
        }
        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => inner.ReadTagsAsync(tag);
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => inner.GetLatestTagAsync(tag);
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => inner.TagExistsAsync(tag);
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => inner.GetEventCountAsync(since);
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => inner.GetAllTagsAsync(tagGroup);
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid id) => inner.ReadSerializableEventAsync(id);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) => inner.ReadSerializableEventsByTagAsync(tag, since);
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) => inner.WriteSerializableEventsAsync(events);
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => inner.GetLatestSortableUniqueIdAsync();
    }

    internal static class Env
    {
        public static int WaitMs { get; set; } = 0;
        public static int CacheSize { get; set; } = 200000;
        public static int HotPersist { get; set; } = 5000;
        public static int SafeWindowMs { get; set; } = 3000;
        public static DcbDomainTypes Domain { get; private set; } = BuildDomain();
        public static InMemoryEventStore EventStore { get; private set; } = new(Domain.EventTypes);
        public static FlakyStore Store { get; private set; } = new(EventStore);
        public static GatingCheckpointStore GatingStore { get; private set; } = new(new InMemoryMultiProjectionStateStore());

        public static void Reset()
        {
            WaitMs = 0;
            CacheSize = 200000;
            HotPersist = 5000;
            SafeWindowMs = 3000;
            Domain = BuildDomain();
            EventStore = new InMemoryEventStore(Domain.EventTypes);
            Store = new FlakyStore(EventStore);
            GatingStore = new GatingCheckpointStore(new InMemoryMultiProjectionStateStore());
        }

        private static DcbDomainTypes BuildDomain()
        {
            var eventTypes = new SimpleEventTypes();
            eventTypes.RegisterEventType<Counted>("Counted");
            var mp = new SimpleMultiProjectorTypes();
            mp.RegisterProjector<CountProjector>();
            var q = new SimpleQueryTypes();
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
                    services.AddSingleton<DcbDomainTypes>(Env.Domain);
                    services.AddSingleton<IEventStore>(Env.Store);
                    services.AddSingleton<IMultiProjectionStateStore>(Env.GatingStore);
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions { SafeWindowMs = Env.SafeWindowMs, FirstQueryCatchUpMaxWaitMs = Env.WaitMs, CatchUpMaxConsecutiveFailures = 1, CatchUpBatchSize = 100, ProcessedEventIdCacheSize = Env.CacheSize, HotCatchUpPersistMaxFetchedEvents = Env.HotPersist });
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

// Friend-test seam for lifetime transitions that have no public production trigger. All reflected methods and
// fields predate SEK-G92, so these tests compile on f7bd9779 and the non-pristine rows fail on observed START.
public interface IResumeTransitionGrain : IMultiProjectionGrain
{
    Task ForceRestartForTestAsync(bool rearm);
}

public sealed class ResumeTransitionGrain : MultiProjectionGrain, IResumeTransitionGrain
{
    public ResumeTransitionGrain(
        [PersistentState("multiProjection", "OrleansStorage")] IPersistentState<MultiProjectionGrainState> state,
        Sekiban.Dcb.Runtime.IProjectionActorHostFactory factory,
        IEventStore store,
        IEventSubscriptionResolver resolver,
        IMultiProjectionStateStore checkpoints,
        IMultiProjectionEventStatistics statistics,
        GeneralMultiProjectionActorOptions options)
        : base(state, factory, store, resolver, checkpoints, statistics, options, projectionStatusStore: null) { }

    public async Task ForceRestartForTestAsync(bool rearm)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(MultiProjectionGrain);
        ((IDisposable?)type.GetField("_catchUpTimer", flags)!.GetValue(this))?.Dispose();
        type.GetField("_catchUpTimer", flags)!.SetValue(this, null);
        var progress = type.GetField("_catchUpProgress", flags)!.GetValue(this)!;
        progress.GetType().GetProperty("IsActive")!.SetValue(progress, false);
        if (rearm)
        {
            var gate = type.GetField("_firstQueryGate", flags)!.GetValue(this)!;
            gate.GetType().GetMethod("Arm")!.Invoke(gate, null);
        }
        await (Task)type.GetMethod("CatchUpFromEventStoreAsync", flags)!.Invoke(this, new object[] { true })!;
    }
}
