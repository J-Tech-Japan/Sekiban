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
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     SEK-G18 item (2): single-cluster durable-rebuild-marker FAILURE behavior, when an out-of-global-order safe
///     promotion (incremental path, after a persist-time compaction) sets RebuildRequired.
///     <para>
///     Scenario ① (marker-write fail): the DURABLE marker write fails. Every state/scalar/list query must FAIL CLOSED
///     (never serve the stale pre-rebuild payload), the external snapshot must be untouched (fail-first: nothing
///     half-done), and once the marker store recovers a same-activation retry completes the rebuild exactly. This is the
///     mutation guard for the marker-commit-BEFORE-invalidate ordering and the query fail-closed path — removing either
///     makes a query serve the stale value here.
///     </para>
///     <para>
///     Scenario ② (external-invalidate fail): the marker commits durably but the external-snapshot invalidate
///     (DeleteAsync) fails, leaving the stale snapshot intact. A fresh activation must check the durable marker BEFORE
///     restore, skip the stale snapshot, and replay the full ordered history from scratch (count == 2, never the stale
///     1). This is the mutation guard for the activation marker-check-before-restore — removing it makes the fresh
///     activation restore the stale snapshot and answer 1.
///     </para>
/// </summary>
public class DurableRebuildMarkerFailClosedTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private IClusterClient _client => _cluster.Client;

    public async Task InitializeAsync()
    {
        Env.Reset();
        MarkerFailingGrainStorage.ResetStore();
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"G18-mfc-{uniqueId}";
        builder.Options.ServiceId = $"G18-mfc-{uniqueId}";
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
    public async Task MarkerWriteFail_AllQueriesFailClosed_NoExternalMutation_SameActivationRetrySucceeds()
    {
        var grain = _client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);
        var executor = new OrleansDcbExecutor(_client, Env.EventStore, Env.Domain);

        // Seed a later already-safe event, graduate + persist (compaction -> incremental promotion path).
        var later = ToSerializable(CreateEvent(new Counted("later"), DateTime.UtcNow.AddSeconds(-30)));
        await Env.EventStore.WriteSerializableEventsAsync(new[] { later });
        await grain.RefreshAsync();
        Assert.True((await grain.PersistStateAsync()).IsSuccess);
        var externalWritesBefore = Env.StateStore.WriteCount;
        Assert.Equal(1, ((CountProjector)(await grain.GetStateAsync()).GetValue().Payload).Count);

        // Arm the marker-write failure, then deliver a globally-EARLIER already-safe event out of order -> RebuildRequired.
        // It is also persisted to the authoritative event store so the eventual full rebuild re-reads BOTH events.
        MarkerFailingGrainStorage.FailMarkerWrites = true;
        var earlier = ToSerializable(CreateEvent(new Counted("earlier"), DateTime.UtcNow.AddSeconds(-31)));
        await Env.EventStore.WriteSerializableEventsAsync(new[] { earlier });
        await grain.AddEventsAsync(new[] { earlier });

        // With the durable marker not committed, all three query surfaces MUST fail closed (never a stale success).
        Assert.False((await grain.GetStateAsync()).IsSuccess);
        Assert.False((await executor.QueryAsync(new CountQuery())).IsSuccess);
        Assert.False((await executor.QueryAsync(new CountListQuery())).IsSuccess);

        // No external snapshot mutation happened (the marker write failed BEFORE any external invalidate/upsert).
        Assert.Equal(externalWritesBefore, Env.StateStore.WriteCount);
        Assert.Equal(0, Env.StateStore.DeleteCount);
        Assert.True((await Env.StateStore.GetLatestForVersionAsync(CountProjector.MultiProjectorName, "1.0.0")).GetValue().HasValue);

        // Same-activation retry: once the marker store recovers, the next query drives the durable rebuild to completion
        // and returns the recovered value (both events counted, from the authoritative global replay).
        MarkerFailingGrainStorage.FailMarkerWrites = false;
        var recovered = await PollUntilSuccessAsync(grain);
        Assert.True(recovered.IsSuccess, recovered.IsSuccess ? "" : recovered.GetException().ToString());
        Assert.Equal(2, ((CountProjector)recovered.GetValue().Payload).Count);
    }

    [Fact]
    public async Task ExternalInvalidateFail_DurableMarkerSurvives_FreshActivationSeesMarkerBeforeRestore_NoStaleSuccess_ExactReplay()
    {
        var grain = _client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);
        var executor = new OrleansDcbExecutor(_client, Env.EventStore, Env.Domain);

        var later = ToSerializable(CreateEvent(new Counted("later"), DateTime.UtcNow.AddSeconds(-30)));
        await Env.EventStore.WriteSerializableEventsAsync(new[] { later });
        await grain.RefreshAsync();
        Assert.True((await grain.PersistStateAsync()).IsSuccess);
        Assert.True((await Env.StateStore.GetLatestForVersionAsync(CountProjector.MultiProjectorName, "1.0.0")).GetValue().HasValue);

        // The DURABLE MARKER commits, but the external-snapshot invalidate (DeleteAsync) fails. Per the fail-first protocol
        // the marker stays durable while the stale external snapshot is left intact — a crash here must still rebuild.
        Env.StateStore.FailDeletes = true;
        var earlier = ToSerializable(CreateEvent(new Counted("earlier"), DateTime.UtcNow.AddSeconds(-31)));
        await Env.EventStore.WriteSerializableEventsAsync(new[] { earlier });
        await grain.AddEventsAsync(new[] { earlier });

        // The invalidate was attempted and failed; the stale external snapshot is still present; and because the live host
        // still signals RebuildRequired every query fails closed (never the stale pre-rebuild payload).
        Assert.True(Env.StateStore.DeleteCount >= 1);
        Assert.True((await Env.StateStore.GetLatestForVersionAsync(CountProjector.MultiProjectorName, "1.0.0")).GetValue().HasValue);
        Assert.False((await grain.GetStateAsync()).IsSuccess);
        Assert.False((await executor.QueryAsync(new CountQuery())).IsSuccess);
        Assert.False((await executor.QueryAsync(new CountListQuery())).IsSuccess);

        // Recover the external store, then FORCE A FRESH ACTIVATION. The durable marker is checked BEFORE restore, so the
        // fresh activation skips the stale snapshot and replays the full ordered history: count == 2 (both events), never
        // the stale 1. (A stale restore would restore 1 and catch up only events AFTER its position — earlier is BEFORE it,
        // so a stale restore could never reach 2. count == 2 proves a from-scratch rebuild happened.)
        Env.StateStore.FailDeletes = false;
        await grain.RequestDeactivationAsync();
        await Task.Delay(1000);

        var grain2 = _client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);
        var recovered = await PollUntilSuccessAsync(grain2);
        Assert.True(recovered.IsSuccess, recovered.IsSuccess ? "" : recovered.GetException().ToString());
        Assert.Equal(2, ((CountProjector)recovered.GetValue().Payload).Count);
    }

    private static async Task<ResultBox<Sekiban.Dcb.MultiProjections.MultiProjectionState>> PollUntilSuccessAsync(IMultiProjectionGrain grain)
    {
        ResultBox<Sekiban.Dcb.MultiProjections.MultiProjectionState> last = default!;
        for (var i = 0; i < 40; i++)
        {
            last = await grain.GetStateAsync();
            if (last.IsSuccess && ((CountProjector)last.GetValue().Payload).Count == 2)
            {
                return last;
            }
            await Task.Delay(150);
        }
        return last;
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
        public static string MultiProjectorName => "g18-mfc-count";
        public static string MultiProjectorVersion => "1.0.0";
        public static CountProjector GenerateInitialPayload() => new();
        public static ResultBox<CountProjector> Project(
            CountProjector payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) =>
            ResultBox.FromValue(payload with { Count = payload.Count + 1 });
    }

    // A grain storage that persists real state, but FAILS the durable write that carries RebuildRequired=true (the marker
    // commit) while FailMarkerWrites is armed. Models a transient marker-store failure that later recovers.
    private sealed class MarkerFailingGrainStorage : IGrainStorage
    {
        public static bool FailMarkerWrites;
        private static readonly Dictionary<string, object?> Store = new();
        private static readonly object Gate = new();

        public static void ResetStore()
        {
            lock (Gate)
            {
                Store.Clear();
            }
            FailMarkerWrites = false;
        }

        public Task ReadStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            lock (Gate)
            {
                if (Store.TryGetValue(grainId.ToString(), out var saved) && saved is T typed)
                {
                    grainState.State = typed;
                    grainState.RecordExists = true;
                }
                else
                {
                    grainState.RecordExists = false;
                }
            }
            return Task.CompletedTask;
        }

        public Task WriteStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            // The durable marker property is INTERNAL (kept off the public API), so include non-public in the lookup.
            var rebuildRequired = grainState.State?.GetType()
                .GetProperty("RebuildRequired", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(grainState.State) as bool?;
            lock (Gate)
            {
                if (FailMarkerWrites && rebuildRequired == true)
                {
                    throw new InvalidOperationException("injected: marker (RebuildRequired) write failure");
                }
                Store[grainId.ToString()] = grainState.State;
            }
            return Task.CompletedTask;
        }

        public Task ClearStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            lock (Gate)
            {
                Store.Remove(grainId.ToString());
            }
            return Task.CompletedTask;
        }
    }

    // A counting + delete-counting external checkpoint store (decorates the in-memory store).
    internal sealed class CountingStateStore : IMultiProjectionStateStore
    {
        private readonly InMemoryMultiProjectionStateStore _inner = new();
        private int _writeCount;
        private int _deleteCount;
        public int WriteCount => _writeCount;
        public int DeleteCount => _deleteCount;
        public bool FailDeletes { get; set; }

        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(string p, string v, CancellationToken ct = default) => _inner.GetLatestForVersionAsync(p, v, ct);
        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(string p, CancellationToken ct = default) => _inner.GetLatestAnyVersionAsync(p, ct);
        public Task<ResultBox<bool>> UpsertAsync(MultiProjectionStateRecord r, int off = 1_000_000, CancellationToken ct = default) { Interlocked.Increment(ref _writeCount); return _inner.UpsertAsync(r, off, ct); }
        public Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(CancellationToken ct = default) => _inner.ListAllAsync(ct);
        public Task<ResultBox<bool>> DeleteAsync(string p, string v, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _deleteCount);
            if (FailDeletes)
            {
                throw new InvalidOperationException("injected: external derived-snapshot invalidate (DeleteAsync) failure");
            }
            return _inner.DeleteAsync(p, v, ct);
        }
        public Task<ResultBox<int>> DeleteAllAsync(string? p = null, CancellationToken ct = default) => _inner.DeleteAllAsync(p, ct);
        public Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(MultiProjectionStateRecord r, CancellationToken ct = default) => _inner.OpenStateDataReadStreamAsync(r, ct);
        public Task<ResultBox<bool>> UpsertFromStreamAsync(MultiProjectionStateWriteRequest req, Stream s, int off, CancellationToken ct = default) { Interlocked.Increment(ref _writeCount); return _inner.UpsertFromStreamAsync(req, s, off, ct); }
    }

    internal static class Env
    {
        public static DcbDomainTypes Domain { get; private set; } = BuildDomain();
        public static InMemoryEventStore EventStore { get; private set; } = new(Domain.EventTypes);
        public static CountingStateStore StateStore { get; private set; } = new();

        public static void Reset()
        {
            Domain = BuildDomain();
            EventStore = new InMemoryEventStore(Domain.EventTypes);
            StateStore = new CountingStateStore();
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
                    services.AddSingleton<DcbDomainTypes>(Env.Domain);
                    services.AddSingleton<IEventStore>(Env.EventStore);
                    services.AddSingleton<IMultiProjectionStateStore>(Env.StateStore);
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions { SafeWindowMs = 3000 });
                    services.AddSekibanDcbNativeRuntime();
                    services.AddGrainStorage("OrleansStorage", (sp, name) => new MarkerFailingGrainStorage());
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }
}
