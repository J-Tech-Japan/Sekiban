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
///     SEK-G20 no-bypass architecture guard (behavioral): on a CAPABLE store, EVERY product checkpoint mutation the grain
///     makes goes through the generation/tombstone CAS surface — there is ZERO unconditional legacy write
///     (UpsertFromStreamAsync) or delete (DeleteAsync). A full persist + retrograde-rebuild (invalidate + rebuilt commit)
///     cycle must record only CAS calls.
/// </summary>
public class CheckpointCasNoBypassTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private IClusterClient _client => _cluster.Client;

    public async Task InitializeAsync()
    {
        Shared.Reset();
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var id = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"G20-nobypass-{id}";
        builder.Options.ServiceId = $"G20-nobypass-{id}";
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
    public async Task CapableStore_AllCheckpointMutations_GoThroughCas_ZeroLegacyWriteOrDelete()
    {
        var grain = _client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);

        // Persist an already-safe checkpoint, then persist again after progress.
        var safe = ToSerializable(CreateEvent(new Counted("safe"), DateTime.UtcNow.AddSeconds(-30)));
        await Shared.EventStore.WriteSerializableEventsAsync(new[] { safe });
        await grain.RefreshAsync();
        Assert.True((await grain.PersistStateAsync()).IsSuccess);

        // Trigger a retrograde full rebuild: a globally-earlier already-safe event delivered out of order after a compacted
        // baseline sets RebuildRequired, which drives invalidate (bump+tombstone) + rebuilt commit.
        var earlier = ToSerializable(CreateEvent(new Counted("earlier"), DateTime.UtcNow.AddSeconds(-31)));
        await Shared.EventStore.WriteSerializableEventsAsync(new[] { earlier });
        await grain.AddEventsAsync(new[] { earlier });

        // Drive the rebuild to completion via queries (fail-closed until rebuilt), then persist the rebuilt checkpoint.
        for (var i = 0; i < 40; i++)
        {
            var s = await grain.GetStateAsync();
            if (s.IsSuccess && ((CountProjector)s.GetValue().Payload).Count == 2) break;
            await Task.Delay(150);
        }
        _ = await grain.PersistStateAsync();

        // The CAS surface handled writes/invalidation; the legacy unconditional paths were NEVER used.
        Assert.Equal(0, Shared.StateStore.LegacyUpsertCount);
        Assert.Equal(0, Shared.StateStore.LegacyDeleteCount);
        Assert.True(Shared.StateStore.ConditionalUpsertCount + Shared.StateStore.CommitRebuiltCount > 0,
            "expected at least one CAS upsert/commit");
        Assert.True(Shared.StateStore.InvalidateCount > 0, "expected the retrograde rebuild to bump+tombstone via CAS");
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
        public static string MultiProjectorName => "g20-nobypass-count";
        public static string MultiProjectorVersion => "1.0.0";
        public static CountProjector GenerateInitialPayload() => new();
        public static ResultBox<CountProjector> Project(
            CountProjector payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) =>
            ResultBox.FromValue(payload with { Count = payload.Count + 1 });
    }

    // Counts legacy vs CAS calls over a capable InMemory store.
    internal sealed class CountingCasStore : IMultiProjectionStateStore, IGenerationAwareCheckpointStore
    {
        private readonly InMemoryMultiProjectionStateStore _inner = new();
        public int LegacyUpsertCount;
        public int LegacyDeleteCount;
        public int ConditionalUpsertCount;
        public int InvalidateCount;
        public int CommitRebuiltCount;

        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(string p, string v, CancellationToken ct = default) => _inner.GetLatestForVersionAsync(p, v, ct);
        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(string p, CancellationToken ct = default) => _inner.GetLatestAnyVersionAsync(p, ct);
        public Task<ResultBox<bool>> UpsertAsync(MultiProjectionStateRecord r, int off = 1_000_000, CancellationToken ct = default) { Interlocked.Increment(ref LegacyUpsertCount); return _inner.UpsertAsync(r, off, ct); }
        public Task<ResultBox<bool>> UpsertFromStreamAsync(MultiProjectionStateWriteRequest req, Stream s, int off, CancellationToken ct = default) { Interlocked.Increment(ref LegacyUpsertCount); return _inner.UpsertFromStreamAsync(req, s, off, ct); }
        public Task<ResultBox<bool>> DeleteAsync(string p, string v, CancellationToken ct = default) { Interlocked.Increment(ref LegacyDeleteCount); return _inner.DeleteAsync(p, v, ct); }
        public Task<ResultBox<int>> DeleteAllAsync(string? p = null, CancellationToken ct = default) { Interlocked.Increment(ref LegacyDeleteCount); return _inner.DeleteAllAsync(p, ct); }
        public Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(CancellationToken ct = default) => _inner.ListAllAsync(ct);
        public Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(MultiProjectionStateRecord r, CancellationToken ct = default) => _inner.OpenStateDataReadStreamAsync(r, ct);

        public CheckpointStoreCapabilityDescriptor DescribeCheckpointCapability() => _inner.DescribeCheckpointCapability();
        public Task<ResultBox<CheckpointSlot>> ReadCheckpointSlotAsync(string p, string v, CancellationToken ct = default) => _inner.ReadCheckpointSlotAsync(p, v, ct);
        public Task<CheckpointCasOutcome> ConditionalUpsertAsync(MultiProjectionStateWriteRequest req, Stream s, CheckpointExpectation e, int off, CancellationToken ct = default) { Interlocked.Increment(ref ConditionalUpsertCount); return _inner.ConditionalUpsertAsync(req, s, e, off, ct); }
        public Task<CheckpointCasOutcome> InvalidateWithTombstoneAsync(string p, string v, CheckpointExpectation e, CancellationToken ct = default) { Interlocked.Increment(ref InvalidateCount); return _inner.InvalidateWithTombstoneAsync(p, v, e, ct); }
        public Task<CheckpointCasOutcome> CommitRebuiltAsync(MultiProjectionStateWriteRequest req, Stream s, CheckpointExpectation e, int off, CancellationToken ct = default) { Interlocked.Increment(ref CommitRebuiltCount); return _inner.CommitRebuiltAsync(req, s, e, off, ct); }
    }

    internal static class Shared
    {
        public static DcbDomainTypes Domain { get; private set; } = BuildDomain();
        public static InMemoryEventStore EventStore { get; private set; } = new(Domain.EventTypes);
        public static CountingCasStore StateStore { get; private set; } = new();

        public static void Reset()
        {
            Domain = BuildDomain();
            EventStore = new InMemoryEventStore(Domain.EventTypes);
            StateStore = new CountingCasStore();
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
