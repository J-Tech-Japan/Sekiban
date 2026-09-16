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

/// <summary>
///     SEK-G85 / #1244: checkpoint tombstone on fresh activation must fail-closed queries fast (not await the forced full
///     replay), kick timer-path catch-up progress, and settle the gate after head proof — without leaking onto G18/G14/CR
///     re-arms.
/// </summary>
public class TombstoneFailClosedTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private IClusterClient _client => _cluster.Client;
    private GatingCheckpointStore GatingStore => Env.GatingStore;

    public async Task InitializeAsync()
    {
        Env.Reset();
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"G85-tfc-{uniqueId}";
        builder.Options.ServiceId = $"G85-tfc-{uniqueId}";
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
    public async Task TombstoneActivation_GetState_FailClosedFast_WithStableMessage()
    {
        var grain = await PrepareFreshActivationWithOpenTombstoneAsync();
        await AssertFailClosedFastAsync(() => grain.GetStateAsync(canGetUnsafeState: false, waitForCatchUp: false));
    }

    [Fact]
    public async Task TombstoneActivation_GetSnapshotJson_FailClosedFast_WithStableMessage()
    {
        var grain = await PrepareFreshActivationWithOpenTombstoneAsync();
        await AssertFailClosedFastAsync(() => grain.GetSnapshotJsonAsync(canGetUnsafeState: false));
    }

    [Fact]
    public async Task TombstoneActivation_ScalarQuery_FailClosedFast_WithStableMessage()
    {
        _ = await PrepareFreshActivationWithOpenTombstoneAsync();
        var executor = new OrleansDcbExecutor(_client, Env.EventStore, Env.Domain);
        await AssertFailClosedFastAsync(async () =>
        {
            var result = await executor.QueryAsync(new CountQuery());
            return result.IsSuccess
                ? ResultBox.FromValue(0)
                : ResultBox.Error<int>(result.GetException());
        });
    }

    [Fact]
    public async Task TombstoneActivation_ListQuery_FailClosedFast_WithStableMessage()
    {
        _ = await PrepareFreshActivationWithOpenTombstoneAsync();
        var executor = new OrleansDcbExecutor(_client, Env.EventStore, Env.Domain);
        await AssertFailClosedFastAsync(async () =>
        {
            var result = await executor.QueryAsync(new CountListQuery());
            return result.IsSuccess
                ? ResultBox.FromValue(0)
                : ResultBox.Error<int>(result.GetException());
        });
    }

    [Fact]
    public async Task TombstoneActivation_SkipsExternalRestore_NoStaleSuccess()
    {
        var grain = await PrepareFreshActivationWithOpenTombstoneAsync();

        // Pre-tombstone snapshot carried count == 1 ("later" only). Fail-closed must not serve it.
        var state = await grain.GetStateAsync(canGetUnsafeState: false, waitForCatchUp: false);
        Assert.False(state.IsSuccess);
        AssertTombstoneFailClosed(state.GetException());

        var record = (await Env.GatingStore.GetLatestForVersionAsync(CountProjector.MultiProjectorName, "1.0.0")).GetValue();
        Assert.True(record.HasValue);
        Assert.Equal(30, record.GetValue().EventsProcessed);
    }

    [Fact]
    public async Task TombstoneActivation_RecoversWithoutMidWindowPolling_AfterTimerCatchUpAndEnsureSettle()
    {
        var grain = await PrepareFreshActivationWithOpenTombstoneAsync();

        await PollUntilAsync(
            async () => !(await grain.GetStatusAsync()).TombstoneFailClosedPending,
            timeoutMs: 60_000,
            label: "tombstone fail-closed cleared");

        var recovered = await grain.GetStateAsync(canGetUnsafeState: false, waitForCatchUp: false);
        Assert.True(recovered.IsSuccess, recovered.IsSuccess ? "" : recovered.GetException().ToString());
        Assert.Equal(50, ((CountProjector)recovered.GetValue().Payload).Count);
    }

    [Fact]
    public async Task TombstoneActivation_GetStatus_ExposesPendingAndCatchUpProgress()
    {
        var grain = await PrepareFreshActivationWithOpenTombstoneAsync();
        var activateAndStatus = grain.GetStatusAsync();
        var failClosedProbe = grain.GetStateAsync(canGetUnsafeState: false, waitForCatchUp: false);
        await Task.WhenAll(activateAndStatus, failClosedProbe);
        var status = await activateAndStatus;
        Assert.True(status.TombstoneFailClosedPending);
        Assert.True(status.IsCatchUpActive || status.CatchUpBatchesProcessed > 0 || status.EventsProcessed > 0);
        Assert.False((await failClosedProbe).IsSuccess);
    }

    [Fact]
    public async Task ScalarList_FailClosed_DoesNotOverwriteLastError()
    {
        var grain = await PrepareFreshActivationWithOpenTombstoneAsync();
        var executor = new OrleansDcbExecutor(_client, Env.EventStore, Env.Domain);

        try { await executor.QueryAsync(new CountQuery()); }
        catch (InvalidOperationException ex) { AssertTombstoneFailClosed(ex); }

        try { await executor.QueryAsync(new CountListQuery()); }
        catch (InvalidOperationException ex) { AssertTombstoneFailClosed(ex); }

        var status = await grain.GetStatusAsync();
        Assert.False(status.HasError);
        Assert.Null(status.LastError);
    }

    [Fact]
    public async Task ConcurrentFailClosedPolls_DoNotRestartFullReplayPerPoll()
    {
        var grain = await PrepareFreshActivationWithOpenTombstoneAsync();

        await PollUntilAsync(
            async () => (await grain.GetStatusAsync()).CatchUpBatchesProcessed > 0
                        || !(await grain.GetStatusAsync()).IsCatchUpActive,
            timeoutMs: 15_000,
            label: "catch-up started or finished");

        var polls = Enumerable.Range(0, 8)
            .Select(_ => grain.GetStateAsync(canGetUnsafeState: false, waitForCatchUp: false))
            .ToArray();
        var results = await Task.WhenAll(polls);
        foreach (var rb in results)
        {
            if (!rb.IsSuccess)
            {
                AssertTombstoneFailClosed(rb.GetException());
            }
        }

        await PollUntilAsync(
            async () => !(await grain.GetStatusAsync()).TombstoneFailClosedPending,
            timeoutMs: 30_000,
            label: "tombstone fail-closed cleared after shared ensure");

        var final = await grain.GetStateAsync();
        Assert.True(final.IsSuccess);
        Assert.Equal(50, ((CountProjector)final.GetValue().Payload).Count);
    }

    private async Task<IMultiProjectionGrain> PrepareFreshActivationWithOpenTombstoneAsync()
    {
        const int persistedEvents = 30;
        const int tailEvents = 20;
        var grain = _client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);

        var baseline = Enumerable.Range(0, persistedEvents)
            .Select(i => ToSerializable(CreateEvent(new Counted($"e{i}"), DateTime.UtcNow.AddSeconds(-60 + i))))
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
            .Select(i => ToSerializable(CreateEvent(new Counted($"e{i}"), DateTime.UtcNow.AddSeconds(-60 + i))))
            .ToArray();
        await Env.EventStore.WriteSerializableEventsAsync(tail);

        await grain.RequestDeactivationAsync();
        await Task.Delay(1000);

        Assert.True((await ReadSlotAsync()).IsTombstoned);
        return _client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);
    }

    private static async Task AssertFailClosedFastAsync(Func<Task<ResultBox<int>>> query)
    {
        var sw = Stopwatch.StartNew();
        var rb = await query();
        sw.Stop();
        Assert.False(rb.IsSuccess);
        AssertTombstoneFailClosed(rb.GetException());
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"expected fail-closed in <5s, took {sw.Elapsed}");
    }

    private static async Task AssertFailClosedFastAsync(Func<Task<ResultBox<MultiProjectionState>>> query)
    {
        var sw = Stopwatch.StartNew();
        var rb = await query();
        sw.Stop();
        Assert.False(rb.IsSuccess);
        AssertTombstoneFailClosed(rb.GetException());
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"expected fail-closed in <5s, took {sw.Elapsed}");
    }

    private static async Task AssertFailClosedFastAsync(Func<Task<ResultBox<string>>> query)
    {
        var sw = Stopwatch.StartNew();
        var rb = await query();
        sw.Stop();
        Assert.False(rb.IsSuccess);
        AssertTombstoneFailClosed(rb.GetException());
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"expected fail-closed in <5s, took {sw.Elapsed}");
    }

    private static void AssertTombstoneFailClosed(Exception ex)
    {
        var message = ex.ToString();
        Assert.Contains("Projection rebuild is pending:", message, StringComparison.Ordinal);
        Assert.Contains("checkpoint tombstone", message, StringComparison.Ordinal);
    }

    private static async Task PollUntilAsync(Func<Task<bool>> predicate, int timeoutMs, string label)
    {
        for (var i = 0; i < timeoutMs / 100; i++)
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
        public static string MultiProjectorName => "g85-tfc-count";
        public static string MultiProjectorVersion => "1.0.0";
        public static CountProjector GenerateInitialPayload() => new();
        public static ResultBox<CountProjector> Project(
            CountProjector payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) =>
            ResultBox.FromValue(payload with { Count = payload.Count + 1 });
    }

    internal static class Env
    {
        public static DcbDomainTypes Domain { get; private set; } = BuildDomain();
        public static InMemoryEventStore EventStore { get; private set; } = new(Domain.EventTypes);
        public static GatingCheckpointStore GatingStore { get; private set; } = new(new InMemoryMultiProjectionStateStore());

        public static void Reset()
        {
            Domain = BuildDomain();
            EventStore = new InMemoryEventStore(Domain.EventTypes);
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
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton<DcbDomainTypes>(Env.Domain);
                    services.AddSingleton<IEventStore>(Env.EventStore);
                    services.AddSingleton<IMultiProjectionStateStore>(Env.GatingStore);
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
