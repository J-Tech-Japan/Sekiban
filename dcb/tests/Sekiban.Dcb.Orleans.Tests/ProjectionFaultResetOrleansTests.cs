using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Runtime.Hosting;
using Orleans.Storage;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     SEK-G14 operator-only admin surface: <c>ResetProjectionFaultAsync</c>. Drives the REAL grain method on a faulted
///     grain. The reset closes the early-healthy window by itself (in-activation host recreation + first-query barrier),
///     so these tests do NOT deactivate manually except where the point IS a reactivation restore. The three token
///     fields are validated as one atomic precondition against the persisted descriptor inside the single-writer gate;
///     a correct reset also invalidates the derived external snapshot so a rebuild starts from the beginning.
/// </summary>
public class ProjectionFaultResetOrleansTests : IAsyncLifetime
{
    private static readonly InMemoryEventStore SharedEventStore = new();
    private static readonly InMemoryMultiProjectionStateStore SharedStateStore = new();
    internal static volatile bool PoisonActive = true;
    private TestCluster _cluster = null!;
    private ISekibanExecutor _executor = null!;
    private IClusterClient Client => _cluster.Client;

    internal static DcbDomainTypes CreateDomain()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<ResetTriggerEvent>();
        var mp = new SimpleMultiProjectorTypes();
        mp.RegisterProjector<ResettableProjector>();
        var q = new SimpleQueryTypes();
        q.RegisterQuery<ResetCountQuery>();
        q.RegisterListQuery<ResetRowListQuery>();
        return new DcbDomainTypes(
            eventTypes,
            new SimpleTagTypes(),
            new SimpleTagProjectorTypes(),
            new SimpleTagStatePayloadTypes(),
            mp,
            q,
            new JsonSerializerOptions());
    }

    public async Task InitializeAsync()
    {
        SharedEventStore.Clear();
        PoisonActive = true;
        await SharedStateStore.DeleteAllAsync(ResettableProjector.MultiProjectorName);
        TogglableGrainStorage.Reset();
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var id = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"ResetCluster-{id}";
        builder.Options.ServiceId = $"ResetService-{id}";
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
        _executor = new OrleansDcbExecutor(Client, SharedEventStore, CreateDomain());
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
        {
            await _cluster.StopAllSilosAsync();
        }
    }

    private static SerializableEvent Event(bool poison, long tick, Guid id) =>
        new(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new ResetTriggerEvent(poison))),
            new SortableUniqueId(SortableUniqueId.GetTickString(tick) + SortableUniqueId.GetIdString(Guid.Empty)).Value,
            id,
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
            [],
            nameof(ResetTriggerEvent));

    private static async Task InjectExternalSnapshotAsync(string position)
    {
        var request = new MultiProjectionStateWriteRequest(
            ResettableProjector.MultiProjectorName,
            ResettableProjector.MultiProjectorVersion,
            nameof(ResettableProjector),
            position,
            EventsProcessed: 1,
            IsOffloaded: false,
            OffloadKey: null,
            OffloadProvider: null,
            OriginalSizeBytes: 4,
            CompressedSizeBytes: 4,
            SafeWindowThreshold: position,
            CreatedAt: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            BuildSource: "test",
            BuildHost: null);
        using var payload = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var result = await SharedStateStore.UpsertFromStreamAsync(request, payload, 1_000_000);
        Assert.True(result.IsSuccess);
    }

    private static async Task PollUntilAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 60; i++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException("condition not met within the poll window");
    }

    private async Task<IMultiProjectionGrain> ReactivateAsync(IMultiProjectionGrain grain, Func<IMultiProjectionGrain, Task<bool>> until)
    {
        await grain.RequestDeactivationAsync();
        var reactivated = Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);
        await PollUntilAsync(() => until(reactivated));
        return reactivated;
    }

    private async Task<(IMultiProjectionGrain Grain, ResetProjectionFaultRequest Token, SerializableEvent Poison)> FaultAndTokenAsync(long tick)
    {
        var id = Guid.CreateVersion7();
        var ev = Event(poison: true, tick, id);
        var grain = Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);
        await grain.SeedEventsAsync(new List<SerializableEvent> { ev });
        await grain.RefreshAsync();
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess); // faulted
        var token = new ResetProjectionFaultRequest(ResettableProjector.MultiProjectorName, id.ToString(), ev.SortableUniqueIdValue);
        return (grain, token, ev);
    }

    // ---- token validation: each field is part of one atomic precondition; any mismatch rejects with zero effect ----

    [Fact]
    public async Task WrongProjector_IsRejected_NoWrite_FaultRetained()
    {
        var (grain, token, _) = await FaultAndTokenAsync(1_000);
        var writesBefore = TogglableGrainStorage.WriteCount;

        var result = await grain.ResetProjectionFaultAsync(token with { ProjectorName = "some-other-projector" });

        Assert.False(result.IsSuccess);
        Assert.Equal(writesBefore, TogglableGrainStorage.WriteCount);
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
    }

    [Fact]
    public async Task WrongEventId_IsRejected_NoWrite_FaultRetained()
    {
        var (grain, token, _) = await FaultAndTokenAsync(1_100);
        var writesBefore = TogglableGrainStorage.WriteCount;

        var result = await grain.ResetProjectionFaultAsync(token with { FaultEventId = Guid.NewGuid().ToString() });

        Assert.False(result.IsSuccess);
        Assert.Equal(writesBefore, TogglableGrainStorage.WriteCount);
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
    }

    [Fact]
    public async Task WrongPosition_IsRejected_NoWrite_FaultRetained()
    {
        var (grain, token, _) = await FaultAndTokenAsync(1_200);
        var writesBefore = TogglableGrainStorage.WriteCount;

        var result = await grain.ResetProjectionFaultAsync(token with { FaultPosition = "a-different-position" });

        Assert.False(result.IsSuccess);
        Assert.Equal(writesBefore, TogglableGrainStorage.WriteCount);
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
    }

    [Fact]
    public async Task SameTokenRace_AtMostOneSucceeds()
    {
        var (grain, token, _) = await FaultAndTokenAsync(1_300);
        PoisonActive = false;

        var a = grain.ResetProjectionFaultAsync(token);
        var b = grain.ResetProjectionFaultAsync(token);
        var results = await Task.WhenAll(a, b);

        Assert.Equal(1, results.Count(r => r.IsSuccess));
    }

    // ---- reset semantics: the reset ALONE closes the early-healthy window (no manual deactivation) ----

    [Fact]
    public async Task CorrectToken_FoldableAfterFix_RebuildsImmediately_ExactRecovery_NoEarlyHealthyWindow()
    {
        var (grain, token, poison) = await FaultAndTokenAsync(2_000);
        PoisonActive = false; // the event now folds cleanly

        Assert.True((await grain.ResetProjectionFaultAsync(token)).IsSuccess);

        // No manual deactivation: the very next query on the SAME grain rebuilds via the barrier before answering.
        var count = await _executor.QueryAsync(new ResetCountQuery());
        Assert.True(count.IsSuccess);
        Assert.Equal(1, count.GetValue().Count);                                   // exact scalar value

        var list = await _executor.QueryAsync(new ResetRowListQuery());
        Assert.True(list.IsSuccess);
        Assert.Single(list.GetValue().Items);                                      // exact list count

        // The state snapshot serialized successfully (recovered, no fault) and reflects the recovered position.
        var snapshot = await grain.GetSnapshotJsonAsync();
        Assert.True(snapshot.IsSuccess);
        Assert.Contains(poison.SortableUniqueIdValue, snapshot.GetValue());
    }

    [Fact]
    public async Task CorrectToken_PoisonRemains_RebuildReFaultsImmediately_QueriesRejected_DescriptorPersisted()
    {
        var (grain, token, _) = await FaultAndTokenAsync(3_000);

        Assert.True((await grain.ResetProjectionFaultAsync(token)).IsSuccess);

        // Poison is still poison: the immediate rebuild re-encounters it and re-faults, on the SAME grain.
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
        Assert.False((await _executor.QueryAsync(new ResetCountQuery())).IsSuccess);
        Assert.False((await _executor.QueryAsync(new ResetRowListQuery())).IsSuccess);

        // The re-fault is persisted: a genuine fresh activation restores it and fails closed.
        var reactivated = await ReactivateAsync(grain, async g => !(await g.GetSnapshotJsonAsync()).IsSuccess);
        Assert.False((await reactivated.GetSnapshotJsonAsync()).IsSuccess);
    }

    [Fact]
    public async Task FailFirstPersistedClearWrite_QueriesRemainRejected_ThenRetrySucceeds_ExactRecovery()
    {
        var (grain, token, _) = await FaultAndTokenAsync(4_000);
        PoisonActive = false;

        TogglableGrainStorage.FailNextWrite = true;
        Assert.False((await grain.ResetProjectionFaultAsync(token)).IsSuccess);

        // Before storage recovery: descriptor + live fault retained, every surface rejected.
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
        Assert.False((await _executor.QueryAsync(new ResetCountQuery())).IsSuccess);
        Assert.False((await _executor.QueryAsync(new ResetRowListQuery())).IsSuccess);

        // Retry the SAME correct token through the real method: reset commits and rebuilds immediately.
        Assert.True((await grain.ResetProjectionFaultAsync(token)).IsSuccess);
        var count = await _executor.QueryAsync(new ResetCountQuery());
        Assert.True(count.IsSuccess);
        Assert.Equal(1, count.GetValue().Count);
    }

    // ---- external snapshot invalidation: a full rebuild cannot restore a pre-poison derived snapshot ----

    [Fact]
    public async Task Reset_InvalidatesExternalSnapshot_FreshActivationCannotRestorePrePoison()
    {
        // Fault the projection (descriptor durably persisted). A derived EXTERNAL snapshot for this projector/version
        // also exists — as a real deployment would have persisted a pre-poison snapshot before the poison arrived.
        var (grain, token, _) = await FaultAndTokenAsync(6_000);
        await InjectExternalSnapshotAsync("000000000000000500000000000000");
        Assert.True((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue);

        Assert.True((await grain.ResetProjectionFaultAsync(token)).IsSuccess);

        // The external snapshot is invalidated by the reset — a fresh activation cannot restore a pre-poison snapshot.
        Assert.False((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue);

        // The in-activation rebuild re-encounters the still-poison event and re-faults immediately — it did NOT come up
        // healthy from a restored pre-poison snapshot (which is gone).
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton(_ => CreateDomain());
                    services.AddSingleton<IEventStore>(SharedEventStore);
                    services.AddSingleton<IMultiProjectionStateStore>(SharedStateStore);
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
                    services.AddSingleton<Sekiban.Dcb.Snapshots.IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions { SafeWindowMs = 1 });
                    services.AddSekibanDcbNativeRuntime();
                    services.AddGrainStorage("OrleansStorage", (_, _) => new TogglableGrainStorage());
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }

    /// <summary>An in-memory grain storage that really persists (so reactivation restores) and can fail the next write.</summary>
    private sealed class TogglableGrainStorage : IGrainStorage
    {
        private static readonly object Sync = new();
        private static readonly Dictionary<string, object?> Store = new();
        public static int WriteCount;
        public static bool FailNextWrite;

        public static void Reset()
        {
            lock (Sync)
            {
                Store.Clear();
                WriteCount = 0;
                FailNextWrite = false;
            }
        }

        public Task ReadStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            lock (Sync)
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
            lock (Sync)
            {
                if (FailNextWrite)
                {
                    FailNextWrite = false;
                    throw new InvalidOperationException("injected: reset persisted clear write failure");
                }

                Store[grainId.ToString()] = grainState.State;
                WriteCount++;
            }

            return Task.CompletedTask;
        }

        public Task ClearStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            lock (Sync)
            {
                Store.Remove(grainId.ToString());
            }

            return Task.CompletedTask;
        }
    }

    internal record ResetTriggerEvent(bool Poison) : IEventPayload;

    internal record ResetCountResult(int Count);

    internal record ResetCountQuery : IMultiProjectionQuery<ResettableProjector, ResetCountQuery, ResetCountResult>
    {
        public static ResultBox<ResetCountResult> HandleQuery(
            ResettableProjector projector,
            ResetCountQuery query,
            IQueryContext context) => ResultBox.FromValue(new ResetCountResult(projector.Count));
    }

    internal record ResetRow(int Value);

    internal record ResetRowListQuery :
        IMultiProjectionListQuery<ResettableProjector, ResetRowListQuery, ResetRow>,
        IQueryPagingParameter
    {
        public int? PageNumber { get; init; }
        public int? PageSize { get; init; }

        public static ResultBox<IEnumerable<ResetRow>> HandleFilter(
            ResettableProjector projector,
            ResetRowListQuery query,
            IQueryContext context) => ResultBox.FromValue(Enumerable.Range(0, projector.Count).Select(i => new ResetRow(i)));

        public static ResultBox<IEnumerable<ResetRow>> HandleSort(
            IEnumerable<ResetRow> filtered,
            ResetRowListQuery query,
            IQueryContext context) => ResultBox.FromValue(filtered);
    }

    /// <summary>A projector that folds a poison event only while <see cref="PoisonActive" /> is set — so a test can "fix" it.</summary>
    internal record ResettableProjector : IMultiProjector<ResettableProjector>
    {
        public int Count { get; init; }
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "resettable-fault-projector";
        public static ResettableProjector GenerateInitialPayload() => new();

        public static ResultBox<ResettableProjector> Project(
            ResettableProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold)
        {
            if (ev.Payload is ResetTriggerEvent { Poison: true } && PoisonActive)
            {
                throw new InvalidOperationException("poison event: refuses to fold while poison is active");
            }

            return ResultBox.FromValue(payload with { Count = payload.Count + 1 });
        }
    }
}
