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
using System.Text;
using System.Text.Json;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     SEK-G14 operator-only admin surface: <c>ResetProjectionFaultAsync</c>. Drives the REAL grain method on a faulted
///     grain across the required scenarios — wrong/stale token rejected with no write; correct token with a now-foldable
///     event rebuilds and recovers; correct token while the poison remains re-faults; a fail-first persisted clear
///     write leaves both the persisted descriptor and the live fault intact; and a same-token race commits at most once.
/// </summary>
public class ProjectionFaultResetOrleansTests : IAsyncLifetime
{
    private static readonly InMemoryEventStore SharedEventStore = new();
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

    private static SerializableEvent PoisonEvent(long tick, Guid id) =>
        new(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new ResetTriggerEvent(true))),
            new SortableUniqueId(SortableUniqueId.GetTickString(tick) + SortableUniqueId.GetIdString(Guid.Empty)).Value,
            id,
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
            [],
            nameof(ResetTriggerEvent));

    private async Task<(IMultiProjectionGrain Grain, ResetProjectionFaultRequest Token)> FaultAndTokenAsync(long tick)
    {
        var id = Guid.CreateVersion7();
        var ev = PoisonEvent(tick, id);
        var grain = Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);
        await grain.SeedEventsAsync(new List<SerializableEvent> { ev });
        await grain.RefreshAsync();
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess); // faulted
        var token = new ResetProjectionFaultRequest(ResettableProjector.MultiProjectorName, id.ToString(), ev.SortableUniqueIdValue);
        return (grain, token);
    }

    [Fact]
    public async Task WrongToken_IsRejected_NoWrite_FaultAndQueriesRetained()
    {
        var (grain, _) = await FaultAndTokenAsync(1_000);
        var writesBefore = TogglableGrainStorage.WriteCount;

        var wrong = new ResetProjectionFaultRequest(ResettableProjector.MultiProjectorName, Guid.NewGuid().ToString(), "wrong-position");
        var result = await grain.ResetProjectionFaultAsync(wrong);

        Assert.False(result.IsSuccess);                                   // rejected
        Assert.Equal(writesBefore, TogglableGrainStorage.WriteCount);     // no provider write
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);     // still faulted
        Assert.False((await _executor.QueryAsync(new ResetCountQuery())).IsSuccess);
        Assert.False((await _executor.QueryAsync(new ResetRowListQuery())).IsSuccess);
    }

    [Fact]
    public async Task CorrectToken_FoldableAfterFix_ResetCommits_RebuildsAndRecovers_NoEarlyHealthyWindow()
    {
        var (grain, token) = await FaultAndTokenAsync(2_000);

        // The event now folds cleanly (projector fixed / redeployed).
        PoisonActive = false;
        var reset = await grain.ResetProjectionFaultAsync(token);
        Assert.True(reset.IsSuccess);

        // Force the fresh activation the reset requested, then the first query rebuilds via the barrier before answering.
        await grain.RequestDeactivationAsync();
        await Task.Delay(750);
        var reactivated = Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);

        Assert.True((await reactivated.GetSnapshotJsonAsync()).IsSuccess);            // recovered, no fault
        Assert.True((await _executor.QueryAsync(new ResetCountQuery())).IsSuccess);   // correct projection result
        Assert.True((await _executor.QueryAsync(new ResetRowListQuery())).IsSuccess);
    }

    [Fact]
    public async Task CorrectToken_PoisonRemains_ResetCommits_RebuildReFaults_QueriesStillRejected()
    {
        var (grain, token) = await FaultAndTokenAsync(3_000);

        // Poison is still poison: the reset clears the descriptor, but the rebuild re-encounters it and re-faults.
        var reset = await grain.ResetProjectionFaultAsync(token);
        Assert.True(reset.IsSuccess);

        await grain.RequestDeactivationAsync();
        await Task.Delay(750);
        var reactivated = Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);

        Assert.False((await reactivated.GetSnapshotJsonAsync()).IsSuccess);            // re-faulted
        Assert.False((await _executor.QueryAsync(new ResetCountQuery())).IsSuccess);
        Assert.False((await _executor.QueryAsync(new ResetRowListQuery())).IsSuccess);
    }

    [Fact]
    public async Task FailFirstPersistedClearWrite_LeavesDescriptorAndLiveFault_ThenRetrySucceeds()
    {
        var (grain, token) = await FaultAndTokenAsync(4_000);
        PoisonActive = false; // so a later successful reset can rebuild cleanly

        // The reset's persisted clear write fails.
        TogglableGrainStorage.FailNextWrite = true;
        var failed = await grain.ResetProjectionFaultAsync(token);
        Assert.False(failed.IsSuccess);

        // Descriptor + live fault retained: queries still rejected on the SAME activation (live fault not cleared).
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);

        // Recover storage and retry the SAME correct token through the real method: reset commits, rebuild recovers.
        var ok = await grain.ResetProjectionFaultAsync(token);
        Assert.True(ok.IsSuccess);
        await grain.RequestDeactivationAsync();
        await Task.Delay(750);
        var reactivated = Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);
        Assert.True((await reactivated.GetSnapshotJsonAsync()).IsSuccess);
    }

    [Fact]
    public async Task SameTokenRace_AtMostOneSucceeds()
    {
        var (grain, token) = await FaultAndTokenAsync(5_000);
        PoisonActive = false;

        var a = grain.ResetProjectionFaultAsync(token);
        var b = grain.ResetProjectionFaultAsync(token);
        var results = await Task.WhenAll(a, b);

        // The persisted descriptor is the concurrency authority: the first clears it, the second's token no longer
        // matches, so exactly one succeeds.
        Assert.Equal(1, results.Count(r => r.IsSuccess));
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
                    services.AddSingleton<IMultiProjectionStateStore, InMemoryMultiProjectionStateStore>();
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
                    services.AddSingleton<Sekiban.Dcb.Snapshots.IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions { SafeWindowMs = 1 });
                    services.AddSekibanDcbNativeRuntime();
                    // The grain's [PersistentState("multiProjection", "OrleansStorage")] binds to this provider.
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
            IQueryContext context) => ResultBox.FromValue(Enumerable.Repeat(new ResetRow(projector.Count), Math.Max(0, projector.Count)));

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
