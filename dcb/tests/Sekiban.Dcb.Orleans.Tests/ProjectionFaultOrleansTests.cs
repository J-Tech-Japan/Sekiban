using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Testing;
using System.Text;
using System.Text.Json;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     Issue #1075 on the Orleans production path: a fold that crashed during background catch-up recorded an internal
///     error and eventually stopped, while queries kept answering current/empty state as a success. These tests hold
///     the Orleans multi-projection grain to the fault contract across ALL query surfaces (state via snapshot, scalar,
///     list): a confirmed fault fails them with context; the fault is durable without a manual persist and survives a
///     reactivation; and ordinary catch-up lag fails nothing.
/// </summary>
[Collection("projection-fault-orleans")]
public class ProjectionFaultOrleansTests : IAsyncLifetime
{
    private static readonly InMemoryEventStore SharedEventStore = new();
    private TestCluster _cluster = null!;
    private ISekibanExecutor _executor = null!;
    private IClusterClient Client => _cluster.Client;

    internal static DcbDomainTypes CreateDomain()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<FaultTriggerEvent>();
        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjector<FaultTestProjector>();
        var queryTypes = new SimpleQueryTypes();
        queryTypes.RegisterQuery<FaultCountQuery>();
        queryTypes.RegisterListQuery<FaultRowListQuery>();
        return new DcbDomainTypes(
            eventTypes,
            new SimpleTagTypes(),
            new SimpleTagProjectorTypes(),
            new SimpleTagStatePayloadTypes(),
            multiProjectorTypes,
            queryTypes,
            new JsonSerializerOptions());
    }

    public async Task InitializeAsync()
    {
        SharedEventStore.Clear(); // isolate each test — SeedEventsAsync writes to this shared static store
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var id = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"FaultCluster-{id}";
        builder.Options.ServiceId = $"FaultService-{id}";
        var portBase = 53_000 + (Environment.ProcessId % 3_000) * 2;
        builder.PortAllocator = new FixedPortAllocator(portBase, portBase + 1);
        builder.Options.BaseSiloPort = portBase;
        builder.Options.BaseGatewayPort = portBase + 1;
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
            _cluster.Dispose();
        }
    }

    internal static SerializableEvent Event(bool poison, long tick) =>
        new(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new FaultTriggerEvent(poison))),
            new SortableUniqueId(SortableUniqueId.GetTickString(tick) + SortableUniqueId.GetIdString(Guid.Empty)).Value,
            Guid.CreateVersion7(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
            [],
            nameof(FaultTriggerEvent));

    [Fact]
    public async Task ConfirmedFault_FailsTheStateSurface_WithContext()
    {
        var grain = Client.GetGrain<IMultiProjectionGrain>(FaultTestProjector.MultiProjectorName);
        await grain.SeedEventsAsync(new List<SerializableEvent> { Event(poison: true, tick: 1_000) });
        await grain.RefreshAsync();

        var state = await grain.GetSnapshotJsonAsync();
        Assert.False(state.IsSuccess); // NOT an empty success
        Assert.Contains(FaultTestProjector.MultiProjectorName, state.GetException().Message);
    }

    [Fact]
    public async Task ConfirmedFault_FailsScalarAndListQuerySurfaces_ThroughTheProductionQueryPath()
    {
        var grain = Client.GetGrain<IMultiProjectionGrain>(FaultTestProjector.MultiProjectorName);
        await grain.SeedEventsAsync(new List<SerializableEvent> { Event(poison: true, tick: 5_000) });
        await grain.RefreshAsync();

        // Both surfaces go through NativeProjectionQueryExecutor -> the actor's faulted GetStateAsync, and must fail
        // rather than launder into an empty successful result (scalar count 0 / empty list).
        Assert.False((await _executor.QueryAsync(new FaultCountQuery())).IsSuccess);
        Assert.False((await _executor.QueryAsync(new FaultRowListQuery())).IsSuccess);
    }

    [Fact]
    public async Task OrdinaryCatchUp_AllQuerySurfacesSucceed_LagIsNotAFault()
    {
        var grain = Client.GetGrain<IMultiProjectionGrain>(FaultTestProjector.MultiProjectorName);
        await grain.SeedEventsAsync(new List<SerializableEvent>
        {
            Event(poison: false, tick: 2_000), Event(poison: false, tick: 2_001)
        });
        await grain.RefreshAsync();

        Assert.True((await grain.GetSnapshotJsonAsync()).IsSuccess);
        Assert.True((await _executor.QueryAsync(new FaultCountQuery())).IsSuccess);
        Assert.True((await _executor.QueryAsync(new FaultRowListQuery())).IsSuccess);
    }

    [Fact]
    public async Task FreshActivation_WithPoisonInStore_ButNoRestoredDescriptor_FailsTheFirstQuery_Synchronously()
    {
        // The descriptor-loss case: durable poison sits in the store, but this grain activates fresh with nothing to
        // restore (as would happen if the descriptor were lost to a crash while persistence was failing). The FIRST
        // query must synchronously catch up, re-encounter the poison and fault — no empty-success window before a
        // background timer eventually re-faults.
        await SharedEventStore.WriteSerializableEventsAsync(
            new List<SerializableEvent> { Event(poison: true, tick: 8_000) });

        var grain = Client.GetGrain<IMultiProjectionGrain>(FaultTestProjector.MultiProjectorName);

        // No RefreshAsync, no seeding through the grain — the very first thing asked of the fresh activation.
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
        Assert.False((await _executor.QueryAsync(new FaultCountQuery())).IsSuccess);
        Assert.False((await _executor.QueryAsync(new FaultRowListQuery())).IsSuccess);
    }

    [Fact]
    public async Task Fault_IsDurable_WithoutAnyManualPersist_AndSurvivesReactivation()
    {
        var grain = Client.GetGrain<IMultiProjectionGrain>(FaultTestProjector.MultiProjectorName);
        await grain.SeedEventsAsync(new List<SerializableEvent> { Event(poison: true, tick: 3_000) });
        await grain.RefreshAsync();
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);

        // NO grain.PersistStateAsync() — fault handling persisted the descriptor on its own. Deactivate, reactivate,
        // and the first query must still fail: no window where a fresh activation answers success before it re-reaches
        // the poison.
        await grain.RequestDeactivationAsync();
        await Task.Delay(1000);

        var reactivated = Client.GetGrain<IMultiProjectionGrain>(FaultTestProjector.MultiProjectorName);
        Assert.False((await reactivated.GetSnapshotJsonAsync()).IsSuccess);
    }

    [Fact]
    public async Task ReRaise_DeserializedOnTheClient_HasTheMarkerAndAllFaultAnnotations()
    {
        var poison = Event(poison: true, tick: 3_500);
        var grain = Client.GetGrain<IMultiProjectionGrain>(FaultTestProjector.MultiProjectorName);
        await grain.SeedEventsAsync(new List<SerializableEvent> { poison });
        await grain.RefreshAsync();
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
        var laterWindowEvent = Event(poison: false, tick: 3_501);
        await grain.SeedEventsAsync(new List<SerializableEvent> { laterWindowEvent });

        // A replacement activation reconstructs the descriptor. This ResultBox arrives through the actual cluster
        // client, so the assertions below are about the Orleans-deserialized client exception, not the server object.
        await grain.RequestDeactivationAsync();
        await Task.Delay(1000);
        var reactivated = Client.GetGrain<IMultiProjectionGrain>(FaultTestProjector.MultiProjectorName);
        // Ask the real state surface while it initiates its background tail catch-up window. The durable descriptor
        // predates this request, so the reconstructed position must still be surfaced rather than being misdiagnosed
        // as a fault in this window.
        var result = await reactivated.GetStateAsync(canGetUnsafeState: true, waitForCatchUp: false);

        Assert.False(result.IsSuccess);
        var ex = Assert.IsType<SekibanProjectionFaultException>(result.GetException());
        Assert.True(ex.IsReRaise);
        Assert.Equal(true, ex.Data[ProjectionFaultDescriptor.ReRaiseDataKey]);
        Assert.Equal($"MultiProjection.Fold ({FaultTestProjector.MultiProjectorName})", ex.Data[ProjectionFaultDescriptor.OperationDataKey]);
        Assert.Equal(nameof(FaultTriggerEvent), ex.Data[ProjectionFaultDescriptor.TargetDataKey]);
        Assert.Equal(poison.Id.ToString(), ex.Data[ProjectionFaultDescriptor.EventIdDataKey]);
        Assert.Equal(poison.SortableUniqueIdValue, ex.Data[ProjectionFaultDescriptor.PositionDataKey]);
        Assert.Contains("previously faulted at event", ex.Message, StringComparison.Ordinal);
        Assert.Contains("first observed", ex.Message, StringComparison.Ordinal);
        Assert.Equal(poison.SortableUniqueIdValue, ex.Fault.Position);
        Assert.NotEqual(laterWindowEvent.SortableUniqueIdValue, ex.Fault.Position); // the stored position is outside this requested tail window
    }

    [Fact]
    public void ProjectionFaultExceptionConverter_ReappliesAnnotationsIdempotently()
    {
        var descriptor = new ProjectionFaultDescriptor(
            Guid.Parse("12345678-1234-1234-1234-123456789abc"),
            "example-event",
            "example-projector",
            "00000000000000000000000000000001",
            "example failure",
            new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc).Ticks);
        var converter = new ProjectionFaultExceptionConverter();
        var constructed = new SekibanProjectionFaultException(descriptor);
        var surrogate = converter.ConvertToSurrogate(constructed);
        var reconstructed = converter.ConvertFromSurrogate(surrogate);
        reconstructed.Fault.AnnotateReRaise(reconstructed); // a third application remains idempotent

        Assert.True(reconstructed.IsReRaise);
        Assert.Equal(5, reconstructed.Data.Count);
        Assert.Equal($"MultiProjection.Fold ({descriptor.ProjectorName})", reconstructed.Data[ProjectionFaultDescriptor.OperationDataKey]);
        Assert.Equal(descriptor.EventType, reconstructed.Data[ProjectionFaultDescriptor.TargetDataKey]);
        Assert.Equal(descriptor.EventId.ToString(), reconstructed.Data[ProjectionFaultDescriptor.EventIdDataKey]);
        Assert.Equal(descriptor.Position, reconstructed.Data[ProjectionFaultDescriptor.PositionDataKey]);
        Assert.Equal(true, reconstructed.Data[ProjectionFaultDescriptor.ReRaiseDataKey]);
    }

    [Fact]
    public async Task ProjectionFaultAdminRead_UsesTheRealProxyForFaultAndNoFault()
    {
        var grain = Client.GetGrain<IMultiProjectionGrain>(FaultTestProjector.MultiProjectorName);

        var noFault = await grain.TryGetProjectionFaultAsync();
        Assert.True(noFault.IsSuccess);
        Assert.False(noFault.GetValue().HasFault);
        Assert.Null(noFault.GetValue().Fault);

        var poison = Event(poison: true, tick: 3_600);
        await grain.SeedEventsAsync(new List<SerializableEvent> { poison });
        await grain.RefreshAsync();
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);

        var fault = await grain.TryGetProjectionFaultAsync();
        Assert.True(fault.IsSuccess); // a generated Orleans proxy reached MultiProjectionGrain, not the default body
        Assert.True(fault.GetValue().HasFault);
        var info = Assert.IsType<ProjectionFaultInfo>(fault.GetValue().Fault);
        Assert.Equal(FaultTestProjector.MultiProjectorName, info.ProjectorName);
        Assert.Equal(poison.Id, info.FaultEventId);
        Assert.Equal(nameof(FaultTriggerEvent), info.EventType);
        Assert.Equal(poison.SortableUniqueIdValue, info.Position);
        Assert.Equal(DateTimeKind.Utc, info.FirstObservedUtc.Kind);
    }

    [Fact]
    public async Task LegacyDirectImplementation_DefaultAdminReadIsExplicitlyUnsupported()
    {
        IMultiProjectionGrain legacy = new LegacyProjectionGrainFake();

        var result = await legacy.TryGetProjectionFaultAsync();

        Assert.False(result.IsSuccess);
        Assert.IsType<NotSupportedException>(result.GetException());
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
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("OrleansStorage")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }

    private sealed class FixedPortAllocator(int baseSiloPort, int baseGatewayPort) : ITestClusterPortAllocator
    {
        public (int, int) AllocateConsecutivePortPairs(int numPorts) => (baseSiloPort, baseGatewayPort);
        public void Dispose() { }
    }

    internal record FaultTriggerEvent(bool Poison) : IEventPayload;

    // Intentionally omits TryGetProjectionFaultAsync. This represents a direct legacy fake that was compiled before the
    // default interface member existed; the explicit unsupported result prevents it from becoming a false no-fault read.
    private sealed class LegacyProjectionGrainFake : IMultiProjectionGrain
    {
        private static Exception Unsupported() => new NotSupportedException("legacy fake");
        public Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true, bool waitForCatchUp = false) => Task.FromException<ResultBox<MultiProjectionState>>(Unsupported());
        public Task<ResultBox<string>> GetSnapshotJsonAsync(bool canGetUnsafeState = true) => Task.FromException<ResultBox<string>>(Unsupported());
        public Task AddEventsAsync(IReadOnlyList<SerializableEvent> events, bool finishedCatchUp = true) => Task.FromException(Unsupported());
        public Task<MultiProjectionGrainStatus> GetStatusAsync() => Task.FromException<MultiProjectionGrainStatus>(Unsupported());
        public Task<ResultBox<bool>> PersistStateAsync() => Task.FromException<ResultBox<bool>>(Unsupported());
        public Task StopSubscriptionAsync() => Task.FromException(Unsupported());
        public Task StartSubscriptionAsync() => Task.FromException(Unsupported());
        public Task<SerializableQueryResult> ExecuteQueryAsync(SerializableQueryParameter query) => Task.FromException<SerializableQueryResult>(Unsupported());
        public Task<SerializableQueryResult> ExecuteQueryAsync(SerializableQueryParameter query, bool waitForCatchUp) => Task.FromException<SerializableQueryResult>(Unsupported());
        public Task<SerializableListQueryResult> ExecuteListQueryAsync(SerializableQueryParameter query) => Task.FromException<SerializableListQueryResult>(Unsupported());
        public Task<SerializableListQueryResult> ExecuteListQueryAsync(SerializableQueryParameter query, bool waitForCatchUp) => Task.FromException<SerializableListQueryResult>(Unsupported());
        public Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId) => Task.FromException<bool>(Unsupported());
        public Task RefreshAsync() => Task.FromException(Unsupported());
        public Task RequestDeactivationAsync() => Task.FromException(Unsupported());
        public Task<bool> OverwritePersistedStateVersionAsync(string newVersion) => Task.FromException<bool>(Unsupported());
        public Task SeedEventsAsync(IReadOnlyList<SerializableEvent> events) => Task.FromException(Unsupported());
        public Task<EventDeliveryStatistics> GetEventDeliveryStatisticsAsync() => Task.FromException<EventDeliveryStatistics>(Unsupported());
        public Task<MultiProjectionCatchUpStatus> GetCatchUpStatusAsync() => Task.FromException<MultiProjectionCatchUpStatus>(Unsupported());
        public Task<MultiProjectionHeadStatusSnapshot> GetProjectionHeadStatusAsync() => Task.FromException<MultiProjectionHeadStatusSnapshot>(Unsupported());
        public Task<MultiProjectionHealthStatus> GetHealthStatusAsync() => Task.FromException<MultiProjectionHealthStatus>(Unsupported());
        public Task<bool> DeleteExternalStateAsync() => Task.FromException<bool>(Unsupported());
        public Task<ResultBox<bool>> ResetProjectionFaultAsync(ResetProjectionFaultRequest request) => Task.FromException<ResultBox<bool>>(Unsupported());
    }

    internal record FaultCountResult(int Count);

    internal record FaultCountQuery :
        IMultiProjectionQuery<FaultTestProjector, FaultCountQuery, FaultCountResult>
    {
        public static ResultBox<FaultCountResult> HandleQuery(
            FaultTestProjector projector,
            FaultCountQuery query,
            IQueryContext context) => ResultBox.FromValue(new FaultCountResult(projector.Count));
    }

    internal record FaultRow(int Value);

    internal record FaultRowListQuery :
        IMultiProjectionListQuery<FaultTestProjector, FaultRowListQuery, FaultRow>,
        IQueryPagingParameter
    {
        public int? PageNumber { get; init; }
        public int? PageSize { get; init; }

        public static ResultBox<IEnumerable<FaultRow>> HandleFilter(
            FaultTestProjector projector,
            FaultRowListQuery query,
            IQueryContext context) => ResultBox.FromValue(
                Enumerable.Range(1, projector.Count).Select(value => new FaultRow(value)));

        public static ResultBox<IEnumerable<FaultRow>> HandleSort(
            IEnumerable<FaultRow> filtered,
            FaultRowListQuery query,
            IQueryContext context) => ResultBox.FromValue(filtered);
    }

    /// <summary>A projector that folds cleanly until it meets a poison event, then throws — a fold crash, deterministically.</summary>
    [GenerateSerializer]
    internal record FaultTestProjector([property: Id(0)] int Count) : IMultiProjector<FaultTestProjector>
    {
        public FaultTestProjector() : this(0) { }

        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "fault-test-projector";
        public static FaultTestProjector GenerateInitialPayload() => new(0);

        public static ResultBox<FaultTestProjector> Project(
            FaultTestProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold)
        {
            if (ev.Payload is FaultTriggerEvent { Poison: true })
            {
                throw new InvalidOperationException("poison event: this projector refuses to fold it");
            }

            return ResultBox.FromValue(payload with { Count = payload.Count + 1 });
        }
    }
}

[CollectionDefinition("projection-fault-orleans", DisableParallelization = true)]
public sealed class ProjectionFaultOrleansCollection { }
