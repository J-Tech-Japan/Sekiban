using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

public class TombstoneCompletionPersistTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private IClusterClient _client => _cluster.Client;

    public async Task InitializeAsync()
    {
        Env.Reset();
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"G94-tcp-{uniqueId}";
        builder.Options.ServiceId = $"G94-tcp-{uniqueId}";
        builder.AddSiloBuilderConfigurator<Configurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tombstone_rebuild_commits_promptly_after_gate_clears_without_deactivation(bool injectFailure)
    {
        var grain = await PrepareFreshActivationWithOpenTombstoneAsync();
        Env.Wrapped.ThrowOnceOnEmpty = injectFailure;
        // Status reads never kick progress; after CatchUpMaxConsecutiveFailures stops the run, only a fail-closed
        // query (TryTombstoneFailClosedBlock -> KickTombstoneGateProgressIfNeeded) restarts it, as a real reader would.
        await PollUntilAsync(async () =>
        {
            if (!(await grain.GetStatusAsync()).TombstoneFailClosedPending) return true;
            _ = await grain.GetStateAsync(canGetUnsafeState: false, waitForCatchUp: false);
            return false;
        }, 20_000, "tombstone gate clearing");

        // No deactivation or explicit persist after gate settlement: completion itself must commit within seconds.
        await PollUntilAsync(async () => (await ReadSlotAsync()).IsActive,
            3_000, "rebuilt checkpoint becoming Active after gate settlement");
        var state = await grain.GetStateAsync(canGetUnsafeState: false, waitForCatchUp: false);
        Assert.True(state.IsSuccess, state.IsSuccess ? "" : state.GetException().ToString());
        Assert.Equal(50, ((CountProjector)state.GetValue().Payload).Count);
        Assert.Equal(injectFailure ? 1 : 0, Env.Wrapped.Thrown);

        // Once committed, an empty completion must not even attempt another final persist.
        // The post-gate query above can start a background catch-up; RefreshAsync is a no-op while one is active.
        await PollUntilAsync(async () => !(await grain.GetStatusAsync()).IsCatchUpActive,
            15_000, "background catch-up to finish before the empty refresh");
        var completionsBefore = Env.Log.Reasons.Count;
        await grain.RefreshAsync();
        Assert.Equal(new[] { "none" }, Env.Log.Reasons.Skip(completionsBefore));
        Assert.True((await ReadSlotAsync()).IsActive);
    }

    [Fact]
    public async Task Normal_empty_completion_does_not_attempt_persist()
    {
        var grain = _client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);
        await grain.RefreshAsync();
        await PollUntilAsync(async () => !(await grain.GetStatusAsync()).IsCatchUpActive,
            30_000, "initial empty catch-up completion");
        var completionsBefore = Env.Log.Reasons.Count;
        await grain.RefreshAsync();
        Assert.Equal(new[] { "none" }, Env.Log.Reasons.Skip(completionsBefore));
    }

    private async Task<IMultiProjectionGrain> PrepareFreshActivationWithOpenTombstoneAsync()
    {
        var t0 = DateTime.UtcNow;
        const int persistedEvents = 30;
        const int tailEvents = 20;
        var grain = _client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);

        var baseline = Enumerable.Range(0, persistedEvents)
            .Select(i => ToSerializable(CreateEvent(new Counted($"e{i}"), t0.AddSeconds(-60 + i))))
            .ToArray();
        await Env.EventStore.WriteSerializableEventsAsync(baseline);
        await grain.RefreshAsync();
        Assert.True((await grain.PersistStateAsync()).IsSuccess);
        Assert.Equal(persistedEvents, ((CountProjector)(await grain.GetStateAsync()).GetValue().Payload).Count);

        var active = await ReadSlotAsync();
        Assert.True(active.IsActive);
        var tombstone = await Env.GatingStore.InvalidateWithTombstoneAsync(
            CountProjector.MultiProjectorName,
            CountProjector.MultiProjectorVersion,
            CheckpointExpectation.FromSlot(active));
        Assert.Equal(CheckpointCasStatus.Committed, tombstone.Status);

        var tail = Enumerable.Range(persistedEvents, tailEvents)
            .Select(i => ToSerializable(CreateEvent(new Counted($"e{i}"), t0.AddSeconds(-60 + i))))
            .ToArray();
        await Env.EventStore.WriteSerializableEventsAsync(tail);

        await grain.RequestDeactivationAsync();
        await Task.Delay(1000);

        Assert.True((await ReadSlotAsync()).IsTombstoned);
        return _client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);
    }

    private static async Task PollUntilAsync(Func<Task<bool>> predicate, int timeoutMs, string label)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < timeoutMs)
        {
            if (await predicate())
            {
                return;
            }
            await Task.Delay(100);
        }
        Assert.Fail($"timed out waiting for {label}");
    }

    private static async Task<CheckpointSlot> ReadSlotAsync() =>
        (await Env.GatingStore.ReadCheckpointSlotAsync(CountProjector.MultiProjectorName, "1.0.0")).GetValue();

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
        public static string MultiProjectorName => "g94-completion-count";
        public static string MultiProjectorVersion => "1.0.0";
        public static CountProjector GenerateInitialPayload() => new();
        public static ResultBox<CountProjector> Project(
            CountProjector payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) =>
            ResultBox.FromValue(payload with { Count = payload.Count + 1 });
    }

    public sealed class ThrowOnEmptyReadStore(IEventStore inner) : IEventStore
    {
        public bool ThrowOnceOnEmpty { get; set; }
        private int _thrown;
        public int Thrown => Volatile.Read(ref _thrown);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            ReadAllSerializableEventsAsync(since, null);
        public async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount)
        {
            var all = await inner.ReadAllSerializableEventsAsync(since, null);
            if (!all.IsSuccess) return all;
            var sorted = all.GetValue().OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal);
            var page = (maxCount is { } m ? sorted.Take(m) : sorted).ToList();
            if (page.Count == 0 && ThrowOnceOnEmpty && Interlocked.CompareExchange(ref _thrown, 1, 0) == 0)
                throw new TimeoutException("Transient failure on final empty tombstone rebuild read");
            return ResultBox.FromValue<IEnumerable<SerializableEvent>>(page);
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
        public static CatchUpCompletionLogger Log { get; private set; } = new();
        public static DcbDomainTypes Domain { get; private set; } = BuildDomain();
        public static InMemoryEventStore EventStore { get; private set; } = new(Domain.EventTypes);
        public static ThrowOnEmptyReadStore Wrapped { get; private set; } = new(EventStore);
        public static GatingCheckpointStore GatingStore { get; private set; } = new(new InMemoryMultiProjectionStateStore());

        public static void Reset()
        {
            Log = new CatchUpCompletionLogger();
            Domain = BuildDomain();
            EventStore = new InMemoryEventStore(Domain.EventTypes);
            Wrapped = new ThrowOnEmptyReadStore(EventStore);
            GatingStore = new GatingCheckpointStore(new InMemoryMultiProjectionStateStore());
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
            siloBuilder.ConfigureLogging(logging => logging.AddProvider(Env.Log))
                .ConfigureServices(services =>
                {
                    services.AddSingleton<DcbDomainTypes>(Env.Domain);
                    services.AddSingleton<IEventStore>(Env.Wrapped);
                    services.AddSingleton<IMultiProjectionStateStore>(Env.GatingStore);
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions { SafeWindowMs = 3000, FirstQueryCatchUpMaxWaitMs = 0, CatchUpMaxConsecutiveFailures = 1, PersistIntervalSeconds = 3600 });
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
