using System.Collections.Concurrent;
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
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Storage.Checkpoints;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

public class PermanentlyTombstonedCheckpointRunbookTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private IClusterClient _client => _cluster.Client;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"G97-runbook-{uniqueId}";
        builder.Options.ServiceId = $"G97-runbook-{uniqueId}";
        Env.Reset(builder.Options.ServiceId);
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
    public async Task Provider_row_delete_then_deactivation_recovers_permanently_tombstoned_checkpoint()
    {
        // Pin all timestamps outside SafeWindow and insert in position order: all 50 events are already safe.
        var t0 = DateTime.UtcNow.AddMinutes(-5);
        var events = Enumerable.Range(0, 50)
            .Select(i => ToSerializable(CreateEvent(new Counted($"e{i}"), t0.AddSeconds(i)))).ToArray();
        Assert.True((await Env.EventStore.WriteSerializableEventsAsync(events)).IsSuccess);
        var grain = _client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);
        await grain.RefreshAsync();
        await AssertCountAndIdleAsync(grain);
        Assert.True((await grain.PersistStateAsync()).IsSuccess);
        var active = await ReadSlotAsync();
        Assert.True(active.IsActive);
        var record = (await Env.StateStore.GetLatestForVersionAsync(
            CountProjector.MultiProjectorName, CountProjector.MultiProjectorVersion)).GetValue().GetValue();

        // Model an inflated legacy count / retained projection store after an event-store reset.
        await using var input = (await Env.StateStore.OpenStateDataReadStreamAsync(record)).GetValue();
        await using var payload = new MemoryStream();
        await input.CopyToAsync(payload);
        payload.Position = 0;
        var request = new MultiProjectionStateWriteRequest(record.ProjectorName, record.ProjectorVersion,
            record.PayloadType, record.LastSortableUniqueId, 100, false, null, null, payload.Length,
            payload.Length, record.SafeWindowThreshold ?? "", record.CreatedAt, DateTime.UtcNow,
            "G97_RETAINED_COUNT", "peer");
        var inflated = await Env.StateStore.ConditionalUpsertAsync(
            request, payload, CheckpointExpectation.FromSlot(active), 2_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, inflated.Status);
        var tombstone = await Env.StateStore.InvalidateWithTombstoneAsync(
            record.ProjectorName, record.ProjectorVersion, CheckpointExpectation.FromSlot(inflated.ResultingSlot!));
        Assert.Equal(CheckpointCasStatus.Committed, tombstone.Status);

        // Each fresh activation fully replays, serves the correct count, but cannot replace the tombstone.
        for (var activation = 0; activation < 2; activation++)
        {
            var fullReads = Env.Wrapped.FullReads;
            await ReactivateAsync(grain);
            await AssertCountAndIdleAsync(grain);
            Assert.True(Env.Wrapped.FullReads > fullReads);
            Assert.True(Env.Log.CountWarningOnCurrentActivation(1030) > 0);
            Assert.Contains("External store has newer safe state (100) than local (50)",
                (await grain.GetStatusAsync()).LastError);
            var stuck = await ReadSlotAsync();
            Assert.True(stuck.IsTombstoned);
            Assert.Equal(tombstone.ResultingSlot!.Generation, stuck.Generation);
        }

        Assert.True(await grain.DeleteExternalStateAsync());
        var unchanged = await ReadSlotAsync();
        Assert.True(unchanged.IsTombstoned);
        Assert.Equal(tombstone.ResultingSlot!.Generation, unchanged.Generation);
        Assert.Equal(100, (await Env.StateStore.GetLatestForVersionAsync(
            record.ProjectorName, record.ProjectorVersion)).GetValue().GetValue().EventsProcessed);

        // The idle grain has no new writes and a one-hour persist interval: only its deactivation persist
        // can race this provider delete. That old-token commit is rejected; it must not recreate the row.
        var rejections = Env.StateStore.RejectedRebuilds;
        Assert.True((await Env.StateStore.DeleteAsync(record.ProjectorName, record.ProjectorVersion)).GetValue());
        Assert.False((await ReadSlotAsync()).Exists);
        var activations = Env.Log.CountEvent(1001);
        var replayReads = Env.Wrapped.FullReads;
        await grain.RequestDeactivationAsync();
        await PollUntilAsync(() => Task.FromResult(Env.StateStore.RejectedRebuilds > rejections),
            10_000, "expected old-token deactivation persist rejection");
        await Task.Delay(1000);
        Assert.False((await ReadSlotAsync()).Exists);
        await AssertCountAndIdleAsync(grain);
        Assert.Equal(activations + 1, Env.Log.CountEvent(1001));
        Assert.True(Env.Wrapped.FullReads > replayReads);
        await PollUntilAsync(async () => (await ReadSlotAsync()).IsActive, 5_000, "fresh Active checkpoint");
        Assert.Equal(activations + 1, Env.StateStore.LastUpsertActivation);
        Assert.Equal(50, (await Env.StateStore.GetLatestForVersionAsync(
            record.ProjectorName, record.ProjectorVersion)).GetValue().GetValue().EventsProcessed);
        Assert.Null((await grain.GetStatusAsync()).LastError);

        // Another actual activation restores that checkpoint and performs only incremental reads.
        replayReads = Env.Wrapped.FullReads;
        var restores = Env.Log.CountEvent(1002);
        await ReactivateAsync(grain);
        await AssertCountAndIdleAsync(grain);
        Assert.Equal(restores + 1, Env.Log.CountEvent(1002));
        Assert.Equal(replayReads, Env.Wrapped.FullReads);
        Assert.True((await ReadSlotAsync()).IsActive);
    }

    private async Task ReactivateAsync(IMultiProjectionGrain grain)
    {
        var activations = Env.Log.CountEvent(1001);
        await grain.RequestDeactivationAsync();
        await Task.Delay(1000);
        _ = await grain.GetStatusAsync();
        Assert.Equal(activations + 1, Env.Log.CountEvent(1001));
    }

    private static async Task AssertCountAndIdleAsync(IMultiProjectionGrain grain)
    {
        // Status reads do not kick progress. Fail-closed queries restart/settle the catch-up gate.
        await PollUntilAsync(async () =>
        {
            var state = await grain.GetStateAsync(canGetUnsafeState: false, waitForCatchUp: false);
            if (!state.IsSuccess) return false;
            Assert.Equal(50, ((CountProjector)state.GetValue().Payload).Count);
            var status = await grain.GetStatusAsync();
            return !status.IsCatchUpActive && !status.TombstoneFailClosedPending && !status.FirstQueryCatchUpPending;
        }, 30_000, "correct query and completed catch-up");
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
        public static string MultiProjectorName => "g97-runbook-count";
        public static string MultiProjectorVersion => "1.0.0";
        public static CountProjector GenerateInitialPayload() => new();
        public static ResultBox<CountProjector> Project(
            CountProjector payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) =>
            ResultBox.FromValue(payload with { Count = payload.Count + 1 });
    }

    public sealed class CountingEventStore(IEventStore inner) : IEventStore
    {
        private int _fullReads;
        public int FullReads => Volatile.Read(ref _fullReads);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            ReadAllSerializableEventsAsync(since, null);
        public async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount)
        {
            if (since is null) Interlocked.Increment(ref _fullReads);
            var all = await inner.ReadAllSerializableEventsAsync(since, null);
            if (!all.IsSuccess) return all;
            var sorted = all.GetValue().OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal);
            var page = (maxCount is { } m ? sorted.Take(m) : sorted).ToList();
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

    internal sealed class RunbookCheckpointStore(IGenerationAwareCheckpointStore inner)
        : DelegatingCheckpointStore(inner)
    {
        private int _rejectedRebuilds;
        public int RejectedRebuilds => Volatile.Read(ref _rejectedRebuilds);
        private int _lastUpsertActivation;
        public int LastUpsertActivation => Volatile.Read(ref _lastUpsertActivation);

        public override async Task<CheckpointCasOutcome> ConditionalUpsertAsync(
            MultiProjectionStateWriteRequest request, Stream stream, CheckpointExpectation expectation,
            int offloadThreshold, CancellationToken cancellationToken = default)
        {
            var activation = Env.Log.CountEvent(1001);
            var outcome = await base.ConditionalUpsertAsync(
                request, stream, expectation, offloadThreshold, cancellationToken);
            if (outcome.Status == CheckpointCasStatus.Committed)
                Volatile.Write(ref _lastUpsertActivation, activation);
            return outcome;
        }

        public override async Task<CheckpointCasOutcome> CommitRebuiltAsync(
            MultiProjectionStateWriteRequest request, Stream stream, CheckpointExpectation expectation,
            int offloadThreshold, CancellationToken cancellationToken = default)
        {
            var outcome = await base.CommitRebuiltAsync(
                request, stream, expectation, offloadThreshold, cancellationToken);
            if (outcome.Status == CheckpointCasStatus.ConditionRejected)
                Interlocked.Increment(ref _rejectedRebuilds);
            return outcome;
        }
    }

    internal sealed class RunbookLogger : ILogger, ILoggerProvider
    {
        private readonly ConcurrentQueue<(int Id, LogLevel Level, int Activation)> _entries = new();
        private int _activation;
        public int CountEvent(int id) => _entries.Count(entry => entry.Id == id);
        public int CountWarningOnCurrentActivation(int id) => _entries.Count(entry =>
            entry.Id == id && entry.Level == LogLevel.Warning && entry.Activation == Volatile.Read(ref _activation));
        public ILogger CreateLogger(string categoryName) =>
            categoryName == typeof(MultiProjectionGrain).FullName
                ? this
                : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Dispose() { }
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId == MultiProjectionLogEvents.ActivationStarted)
                Interlocked.Increment(ref _activation);
            _entries.Enqueue((eventId.Id, logLevel, Volatile.Read(ref _activation)));
        }
    }

    internal static class Env
    {
        public static RunbookLogger Log { get; private set; } = new();
        public static DcbDomainTypes Domain { get; private set; } = BuildDomain();
        public static InMemoryEventStore EventStore { get; private set; } = new(Domain.EventTypes);
        public static CountingEventStore Wrapped { get; private set; } = new(EventStore);
        public static RunbookCheckpointStore StateStore { get; private set; } = new(new InMemoryMultiProjectionStateStore());

        public static void Reset(string serviceId)
        {
            Log = new RunbookLogger();
            Domain = BuildDomain();
            EventStore = new InMemoryEventStore(Domain.EventTypes);
            Wrapped = new CountingEventStore(EventStore);
            StateStore = new RunbookCheckpointStore(new InMemoryMultiProjectionStateStore(new FixedServiceIdProvider(serviceId)));
        }

        private static DcbDomainTypes BuildDomain()
        {
            var eventTypes = new SimpleEventTypes();
            eventTypes.RegisterEventType<Counted>("Counted");
            var mp = new SimpleMultiProjectorTypes();
            mp.RegisterProjector<CountProjector>();
            return new DcbDomainTypes(eventTypes, new SimpleTagTypes(), new SimpleTagProjectorTypes(),
                new SimpleTagStatePayloadTypes(), mp, new SimpleQueryTypes(), new JsonSerializerOptions());
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
                    services.AddSingleton<IMultiProjectionStateStore>(Env.StateStore);
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
