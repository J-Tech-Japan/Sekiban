using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Runtime.Hosting;
using Orleans.Storage;
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

/// <summary>SEK-G95: marker clearing, stuck-marker repair, and the failed-reset live-host guard.</summary>
public class DurableRebuildMarkerPersistTests : IAsyncLifetime
{
    private TestCluster? _cluster;
    private IClusterClient Client => _cluster!.Client;
    public Task InitializeAsync() => Task.CompletedTask;

    private async Task StartAsync(bool streaming, int persistIntervalSeconds = 3600, bool skipUnchanged = true)
    {
        Env.Reset();
        Env.Streaming = streaming;
        Env.PersistIntervalSeconds = persistIntervalSeconds;
        Env.SkipUnchanged = skipUnchanged;
        MarkerGrainStorage.Reset();
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var id = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"G95-marker-{id}";
        builder.Options.ServiceId = $"G95-marker-{id}";
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rebuild_ClearsMarker_ReactivationRestoresWithoutFullReplay(bool streaming)
    {
        await StartAsync(streaming);
        var t0 = DateTime.UtcNow;
        var grain = await SeedAsync(t0);
        await AddEarlierAsync(grain, t0);
        await AssertCountAndIdleAsync(grain, 2); // Fail-closed queries drive the full rebuild.
        Assert.True((await grain.PersistStateAsync()).IsSuccess);
        AssertMarkerCleared();
        Assert.True((await SlotAsync()).IsActive);
        await AssertUnchangedPersistDoesNotWriteAsync(grain);
        await AssertReactivationWithoutFullReplayAsync(grain, 2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StuckMarker_UnchangedCleanHost_PersistRepairsOnce_ThenSkips(bool streaming)
    {
        await StartAsync(streaming);
        // Model the old streaming persist: an Active snapshot is saved, but its metadata write retains the marker.
        // The provider mutates the candidate before commit, so the grain's committed view has the same stuck marker.
        MarkerGrainStorage.RetainMarkerOnCheckpointWrites = true;
        var grain = await SeedAsync(DateTime.UtcNow);
        Assert.True(MarkerGrainStorage.Read().RebuildRequired);
        Assert.True((await SlotAsync()).IsActive);
        var safePosition = MarkerGrainStorage.Read().LastSortableUniqueId;
        var safeVersion = MarkerGrainStorage.Read().LastGoodSafeVersion;
        MarkerGrainStorage.RetainMarkerOnCheckpointWrites = false;
        var externalBefore = Env.StateStore.WriteCount;
        var metadataBefore = MarkerGrainStorage.WriteCount;
        Assert.True((await grain.PersistStateAsync()).IsSuccess);
        Assert.Equal(externalBefore + 1, Env.StateStore.WriteCount);
        Assert.Equal(metadataBefore + 1, MarkerGrainStorage.WriteCount);
        AssertMarkerCleared();
        Assert.Equal(safePosition, MarkerGrainStorage.Read().LastSortableUniqueId);
        Assert.Equal(safeVersion, MarkerGrainStorage.Read().LastGoodSafeVersion);
        await AssertUnchangedPersistDoesNotWriteAsync(grain);
        await AssertReactivationWithoutFullReplayAsync(grain, 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MarkerFalse_UnchangedCheckpoint_StillSkips(bool streaming)
    {
        await StartAsync(streaming);
        var grain = await SeedAsync(DateTime.UtcNow);
        AssertMarkerCleared();
        await AssertUnchangedPersistDoesNotWriteAsync(grain);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResetWriteFailsAfterTombstone_ContaminatedPersistKeepsMarker_ReactivationFullyReplays(bool streaming)
    {
        await StartAsync(streaming);
        // The contaminated host retains its old safe checkpoint. Disable the optional skip only in this test
        // to exercise the actual snapshot-save path after the failed reset (also used by timer/deactivation).
        Env.SkipUnchanged = false;
        var t0 = DateTime.UtcNow;
        var grain = await SeedAsync(t0);
        MarkerGrainStorage.FailResetWrites = true;
        await AddEarlierAsync(grain, t0);
        // The reset failure leaves the old host alive; status reads alone cannot advance its rebuild.
        Assert.False((await grain.GetStateAsync(canGetUnsafeState: false, waitForCatchUp: false)).IsSuccess);
        Assert.True(MarkerGrainStorage.ResetFailures > 0);
        Assert.True(Env.StateStore.InvalidateCount > 0);
        Assert.True((await SlotAsync()).IsTombstoned);
        var marker = MarkerGrainStorage.Read();
        Assert.True(marker.RebuildRequired);
        Assert.NotNull(marker.RebuildOffendingEventId);
        Assert.NotNull(marker.RebuildOffendingPosition);
        var writes = Env.StateStore.WriteCount;
        Assert.True((await grain.PersistStateAsync()).IsSuccess);
        // Ensure we exercised a successful external save of the contaminated host, rather than an early skip.
        Assert.Equal(writes + 1, Env.StateStore.WriteCount);
        Assert.True((await SlotAsync()).IsActive);
        var afterPersist = MarkerGrainStorage.Read();
        Assert.True(afterPersist.RebuildRequired);
        Assert.Equal(marker.RebuildOffendingEventId, afterPersist.RebuildOffendingEventId);
        Assert.Equal(marker.RebuildOffendingPosition, afterPersist.RebuildOffendingPosition);
        var readsBeforeDeactivation = MarkerGrainStorage.ReadCount;
        await grain.RequestDeactivationAsync();
        await Task.Delay(1000);
        // Deactivation also persists the contaminated host. Recover the provider only after it is gone.
        Assert.True(MarkerGrainStorage.Read().RebuildRequired);
        MarkerGrainStorage.FailResetWrites = false;
        var before = Env.Counting.FullReads;
        grain = Client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);
        await AssertCountAndIdleAsync(grain, 2);
        Assert.True(MarkerGrainStorage.ReadCount > readsBeforeDeactivation, "Expected a fresh activation.");
        Assert.True(Env.Counting.FullReads > before);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task TimerPersist_HostSwappedDuringExternalSave_KeepsMarker_ReactivationFullyReplays(
        bool streaming, bool failedReset)
    {
        await StartAsync(streaming, persistIntervalSeconds: 1, skipUnchanged: false);
        var t0 = DateTime.UtcNow;
        var grain = await SeedAsync(t0);
        if (failedReset)
        {
            MarkerGrainStorage.FailResetWrites = true;
            await AddEarlierAsync(grain, t0);
            Assert.False((await grain.GetStateAsync(canGetUnsafeState: false, waitForCatchUp: false)).IsSuccess);
            Assert.True(MarkerGrainStorage.ResetFailures > 0);
        }

        var recreated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var hook = CatchUpProductionTestHooks.Register(
            Sekiban.Dcb.ServiceId.DefaultServiceIdProvider.DefaultServiceId, CountProjector.MultiProjectorName,
            (point, _) =>
            {
                if (point == CatchUpProductionHookPoint.HostRecreated) recreated.TrySetResult();
                return Task.CompletedTask;
            });
        var saveGate = Env.StateStore.ParkNextSave();
        try
        {
            // The Interleave=true persist timer has captured A's snapshot, but has not acquired the mutation gate.
            await saveGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            MarkerGrainStorage.FailResetWrites = false;
            if (!failedReset) await AddEarlierAsync(grain, t0);
            await grain.GetStateAsync(canGetUnsafeState: false, waitForCatchUp: false);
            await recreated.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(MarkerGrainStorage.Read().RebuildRequired);
            var writes = Env.StateStore.WriteCount;
            saveGate.Release.TrySetResult();
            await FirstQueryBoundedWaitClusterTests.PollUntilAsync(() =>
                Task.FromResult(Env.StateStore.WriteCount > writes && MarkerGrainStorage.Read().LastGoodSafeVersion == 1));
            var staleSlot = await SlotAsync();
            Assert.True(staleSlot.IsActive); // A's old snapshot really committed over B's tombstone.
            Assert.Equal(1L, staleSlot.Record!.EventsProcessed);
            Assert.True(MarkerGrainStorage.Read().RebuildRequired);
            await WaitForIdleAsync(grain);
            var readsBeforeDeactivation = MarkerGrainStorage.ReadCount;
            await grain.RequestDeactivationAsync();
            await Task.Delay(1000);
            Assert.True(MarkerGrainStorage.Read().RebuildRequired);
            Env.StateStore.RejectOtherSaves = false;
            var fullReads = Env.Counting.FullReads;
            grain = Client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);
            await AssertCountAndIdleAsync(grain, 2);
            Assert.True(MarkerGrainStorage.ReadCount > readsBeforeDeactivation);
            Assert.True(Env.Counting.FullReads > fullReads);
        }
        finally
        {
            saveGate.Release.TrySetResult();
            MarkerGrainStorage.FailResetWrites = false;
            Env.StateStore.RejectOtherSaves = false;
        }
    }

    private async Task<IMultiProjectionGrain> SeedAsync(DateTime t0)
    {
        var grain = Client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);
        var later = ToSerializable(CreateEvent(new Counted("later"), t0.AddSeconds(-30)));
        await Env.EventStore.WriteSerializableEventsAsync(new[] { later });
        // Activation/query can start catch-up. Refresh is a no-op until that catch-up finishes.
        await AssertCountAndIdleAsync(grain, 1);
        await grain.RefreshAsync();
        await WaitForIdleAsync(grain);
        Assert.True((await grain.PersistStateAsync()).IsSuccess);
        return grain;
    }

    private static async Task AddEarlierAsync(IMultiProjectionGrain grain, DateTime t0)
    {
        await WaitForIdleAsync(grain);
        var earlier = ToSerializable(CreateEvent(new Counted("earlier"), t0.AddSeconds(-31)));
        await Env.EventStore.WriteSerializableEventsAsync(new[] { earlier });
        await grain.AddEventsAsync(new[] { earlier });
    }

    private static Task WaitForIdleAsync(IMultiProjectionGrain grain) =>
        FirstQueryBoundedWaitClusterTests.PollUntilAsync(async () => !(await grain.GetStatusAsync()).IsCatchUpActive);

    private static async Task AssertCountAndIdleAsync(IMultiProjectionGrain grain, int count)
    {
        await FirstQueryBoundedWaitClusterTests.PollUntilAsync(async () =>
        {
            var result = await grain.GetStateAsync(canGetUnsafeState: false, waitForCatchUp: false);
            return result.IsSuccess && ((CountProjector)result.GetValue().Payload).Count == count;
        });
        await WaitForIdleAsync(grain);
        var state = await grain.GetStateAsync(canGetUnsafeState: false, waitForCatchUp: false);
        Assert.True(state.IsSuccess);
        Assert.Equal(count, ((CountProjector)state.GetValue().Payload).Count);
        await WaitForIdleAsync(grain);
    }

    private static void AssertMarkerCleared()
    {
        var state = MarkerGrainStorage.Read();
        Assert.False(state.RebuildRequired);
        Assert.Null(state.RebuildOffendingEventId);
        Assert.Null(state.RebuildOffendingPosition);
    }

    private static async Task AssertUnchangedPersistDoesNotWriteAsync(IMultiProjectionGrain grain)
    {
        await WaitForIdleAsync(grain);
        var external = Env.StateStore.WriteCount;
        var metadata = MarkerGrainStorage.WriteCount;
        Assert.True((await grain.PersistStateAsync()).IsSuccess);
        Assert.Equal(external, Env.StateStore.WriteCount);
        Assert.Equal(metadata, MarkerGrainStorage.WriteCount);
    }

    private async Task AssertReactivationWithoutFullReplayAsync(IMultiProjectionGrain grain, int count)
    {
        var readsBeforeDeactivation = MarkerGrainStorage.ReadCount;
        await grain.RequestDeactivationAsync();
        await Task.Delay(1000);
        var before = Env.Counting.FullReads;
        grain = Client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);
        await AssertCountAndIdleAsync(grain, count);
        Assert.True(MarkerGrainStorage.ReadCount > readsBeforeDeactivation, "Expected a fresh activation.");
        Assert.Equal(before, Env.Counting.FullReads);
    }

    private static async Task<CheckpointSlot> SlotAsync() =>
        (await Env.StateStore.ReadCheckpointSlotAsync(CountProjector.MultiProjectorName, "1.0.0")).GetValue();

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
        public static string MultiProjectorName => "g95-marker-count";
        public static string MultiProjectorVersion => "1.0.0";
        public static CountProjector GenerateInitialPayload() => new();
        public static ResultBox<CountProjector> Project(
            CountProjector payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) =>
            ResultBox.FromValue(payload with { Count = payload.Count + 1 });
    }

    private sealed class MarkerGrainStorage : IGrainStorage
    {
        private static readonly Dictionary<string, MultiProjectionGrainState> Store = new();
        private static readonly object Gate = new();
        public static bool FailResetWrites;
        public static bool RetainMarkerOnCheckpointWrites;
        public static int WriteCount;
        public static int ReadCount;
        public static int ResetFailures;

        public static void Reset()
        {
            lock (Gate) Store.Clear();
            FailResetWrites = false;
            RetainMarkerOnCheckpointWrites = false;
            WriteCount = 0;
            ReadCount = 0;
            ResetFailures = 0;
        }

        public static MultiProjectionGrainState Read()
        {
            lock (Gate) return Assert.Single(Store.Values).Clone();
        }

        public Task ReadStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            Interlocked.Increment(ref ReadCount);
            lock (Gate)
            {
                if (Store.TryGetValue(grainId.ToString(), out var state))
                {
                    grainState.State = (T)(object)state.Clone();
                    grainState.RecordExists = true;
                }
                else grainState.RecordExists = false;
            }
            return Task.CompletedTask;
        }

        public Task WriteStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            var state = Assert.IsType<MultiProjectionGrainState>(grainState.State);
            lock (Gate)
            {
                // Only the derived-state reset has this shape. The preceding marker write retains its checkpoint.
                if (FailResetWrites && state.RebuildRequired && state.LastSortableUniqueId is null &&
                    state.LastGoodSafeVersion == 0 && Env.StateStore.InvalidateCount > 0)
                {
                    Interlocked.Increment(ref ResetFailures);
                    throw new InvalidOperationException("injected: derived-state reset write after tombstone");
                }
                if (RetainMarkerOnCheckpointWrites && state.LastGoodSafeVersion > 0)
                {
                    state.RebuildRequired = true;
                    state.RebuildOffendingEventId = "old-marker";
                    state.RebuildOffendingPosition = state.LastSortableUniqueId;
                }
                Store[grainId.ToString()] = state.Clone();
                Interlocked.Increment(ref WriteCount);
            }
            return Task.CompletedTask;
        }

        public Task ClearStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            lock (Gate) Store.Remove(grainId.ToString());
            return Task.CompletedTask;
        }
    }

    internal sealed class CountingEventStore(IEventStore inner) : IEventStore
    {
        private int _fullReads;
        public int FullReads => Volatile.Read(ref _fullReads);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null)
        {
            if (since is null) Interlocked.Increment(ref _fullReads);
            return inner.ReadAllSerializableEventsAsync(since);
        }
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount)
        {
            if (since is null) Interlocked.Increment(ref _fullReads);
            return inner.ReadAllSerializableEventsAsync(since, maxCount);
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

    internal sealed class CountingStateStore : IMultiProjectionStateStore, IGenerationAwareCheckpointStore
    {
        private readonly InMemoryMultiProjectionStateStore _inner = new();
        private int _writeCount;
        private int _invalidateCount;
        public int WriteCount => Volatile.Read(ref _writeCount);
        public int InvalidateCount => Volatile.Read(ref _invalidateCount);

        private SaveGate? _nextSave;
        public volatile bool RejectOtherSaves;
        internal sealed class SaveGate
        {
            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        public SaveGate ParkNextSave()
        {
            var gate = new SaveGate();
            Interlocked.Exchange(ref _nextSave, gate);
            return gate;
        }
        public async Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(string p, string v, CancellationToken ct = default)
        {
            var gate = Interlocked.Exchange(ref _nextSave, null);
            if (gate is not null)
            {
                var captured = await _inner.GetLatestForVersionAsync(p, v, ct);
                // Prevent later clean persists (including deactivation) from repairing the marker under test.
                RejectOtherSaves = true;
                gate.Entered.TrySetResult();
                await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
                return captured;
            }
            if (RejectOtherSaves)
                return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(
                    new InvalidOperationException("injected: prevent subsequent marker repair"));
            return await _inner.GetLatestForVersionAsync(p, v, ct);
        }
        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(string p, CancellationToken ct = default) => _inner.GetLatestAnyVersionAsync(p, ct);
        public Task<ResultBox<bool>> UpsertAsync(MultiProjectionStateRecord r, int off = 1_000_000, CancellationToken ct = default) { Interlocked.Increment(ref _writeCount); return _inner.UpsertAsync(r, off, ct); }
        public Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(CancellationToken ct = default) => _inner.ListAllAsync(ct);
        public Task<ResultBox<bool>> DeleteAsync(string p, string v, CancellationToken ct = default) => _inner.DeleteAsync(p, v, ct);
        public Task<ResultBox<int>> DeleteAllAsync(string? p = null, CancellationToken ct = default) => _inner.DeleteAllAsync(p, ct);
        public Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(MultiProjectionStateRecord r, CancellationToken ct = default) => _inner.OpenStateDataReadStreamAsync(r, ct);
        public Task<ResultBox<bool>> UpsertFromStreamAsync(MultiProjectionStateWriteRequest req, Stream s, int off, CancellationToken ct = default) { Interlocked.Increment(ref _writeCount); return _inner.UpsertFromStreamAsync(req, s, off, ct); }

        public CheckpointStoreCapabilityDescriptor DescribeCheckpointCapability() => _inner.DescribeCheckpointCapability();
        public Task<ResultBox<CheckpointSlot>> ReadCheckpointSlotAsync(string p, string v, CancellationToken ct = default) => _inner.ReadCheckpointSlotAsync(p, v, ct);
        public Task<CheckpointCasOutcome> ConditionalUpsertAsync(MultiProjectionStateWriteRequest req, Stream s, CheckpointExpectation e, int off, CancellationToken ct = default) { Interlocked.Increment(ref _writeCount); return _inner.ConditionalUpsertAsync(req, s, e, off, ct); }
        public async Task<CheckpointCasOutcome> InvalidateWithTombstoneAsync(string p, string v, CheckpointExpectation e, CancellationToken ct = default)
        {
            var outcome = await _inner.InvalidateWithTombstoneAsync(p, v, e, ct);
            if (outcome.Status == CheckpointCasStatus.Committed) Interlocked.Increment(ref _invalidateCount);
            return outcome;
        }
        public Task<CheckpointCasOutcome> CommitRebuiltAsync(MultiProjectionStateWriteRequest req, Stream s, CheckpointExpectation e, int off, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _writeCount);
            return _inner.CommitRebuiltAsync(req, s, e, off, ct);
        }
    }

    internal static class Env
    {
        public static bool Streaming { get; set; }
        public static int PersistIntervalSeconds { get; set; } = 3600;
        public static bool SkipUnchanged { get; set; } = true;
        public static DcbDomainTypes Domain { get; private set; } = BuildDomain();
        public static InMemoryEventStore EventStore { get; private set; } = new(Domain.EventTypes);
        public static CountingEventStore Counting { get; private set; } = new(EventStore);
        public static CountingStateStore StateStore { get; private set; } = new();

        public static void Reset()
        {
            SkipUnchanged = true;
            Domain = BuildDomain();
            EventStore = new InMemoryEventStore(Domain.EventTypes);
            StateStore = new CountingStateStore();
            Counting = new CountingEventStore(EventStore);
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
                    services.AddSingleton<IEventStore>(Env.Counting);
                    services.AddSingleton<IMultiProjectionStateStore>(Env.StateStore);
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions { SafeWindowMs = 3000, FirstQueryCatchUpMaxWaitMs = 0, UseStreamingSnapshotIO = Env.Streaming, SkipPersistWhenSafeCheckpointUnchanged = Env.SkipUnchanged, PersistIntervalSeconds = Env.PersistIntervalSeconds });
                    services.AddSekibanDcbNativeRuntime();
                    services.AddGrainStorage("OrleansStorage", (sp, name) => new MarkerGrainStorage());
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }
}
