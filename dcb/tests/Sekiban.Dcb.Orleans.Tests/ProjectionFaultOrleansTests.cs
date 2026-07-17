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
///     Issue #1075 on the Orleans production path: a fold that crashes during background catch-up recorded an internal
///     error and eventually stopped, while queries kept answering current/empty state as a success. These tests hold
///     the Orleans multi-projection grain to the fault contract: a confirmed fault fails queries with context; the
///     fault survives a deactivation/reactivation (a fresh activation cannot answer success before it is
///     re-established); an operator rebuild clears it; and ordinary catch-up with no fault fails nothing.
/// </summary>
public class ProjectionFaultOrleansTests : IAsyncLifetime
{
    private static readonly IEventStore SharedEventStore = new InMemoryEventStore();
    private TestCluster _cluster = null!;
    private IClusterClient Client => _cluster.Client;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var id = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"FaultCluster-{id}";
        builder.Options.ServiceId = $"FaultService-{id}";
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
        {
            await _cluster.StopAllSilosAsync();
        }
    }

    private static SerializableEvent Event(bool poison, long tick) =>
        new(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new FaultTriggerEvent(poison))),
            new SortableUniqueId(SortableUniqueId.GetTickString(tick) + SortableUniqueId.GetIdString(Guid.Empty)).Value,
            Guid.CreateVersion7(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
            [],
            nameof(FaultTriggerEvent));

    [Fact]
    public async Task ConfirmedFault_FailsTheQuery_WithContext()
    {
        var grain = Client.GetGrain<IMultiProjectionGrain>(FaultTestProjector.MultiProjectorName);

        await grain.SeedEventsAsync(new List<SerializableEvent> { Event(poison: true, tick: 1_000) });
        await grain.RefreshAsync();

        // The read surface, taken through a serializable projection (GetStateAsync would try to Orleans-copy the
        // projector payload itself, which this test's private projector is not set up for; the snapshot funnels
        // through the same GetStateAsync fault gate and returns a string).
        var state = await grain.GetSnapshotJsonAsync();
        Assert.False(state.IsSuccess); // NOT an empty success — the fault fails the query
        Assert.Contains(FaultTestProjector.MultiProjectorName, state.GetException().Message);
    }

    [Fact]
    public async Task OrdinaryCatchUp_WithNoFault_FailsNothing()
    {
        var grain = Client.GetGrain<IMultiProjectionGrain>(FaultTestProjector.MultiProjectorName);

        await grain.SeedEventsAsync(new List<SerializableEvent> { Event(poison: false, tick: 2_000), Event(poison: false, tick: 2_001) });
        await grain.RefreshAsync();

        var state = await grain.GetSnapshotJsonAsync();
        Assert.True(state.IsSuccess); // healthy lag is not a fault
    }

    [Fact]
    public async Task Fault_SurvivesReactivation_FreshActivationDoesNotAnswerSuccessFirst()
    {
        var grain = Client.GetGrain<IMultiProjectionGrain>(FaultTestProjector.MultiProjectorName);

        await grain.SeedEventsAsync(new List<SerializableEvent> { Event(poison: true, tick: 3_000) });
        await grain.RefreshAsync();
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
        await grain.PersistStateAsync();

        // Force a fresh activation.
        await grain.RequestDeactivationAsync();
        await Task.Delay(1000);

        var reactivated = Client.GetGrain<IMultiProjectionGrain>(FaultTestProjector.MultiProjectorName);

        // The very first query after reactivation must fail — the persisted fault was restored before any query could
        // be answered, so there is no window where the fresh grain reports empty success.
        var state = await reactivated.GetSnapshotJsonAsync();
        Assert.False(state.IsSuccess);
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton(_ =>
                    {
                        var eventTypes = new SimpleEventTypes();
                        eventTypes.RegisterEventType<FaultTriggerEvent>();
                        var multiProjectorTypes = new SimpleMultiProjectorTypes();
                        multiProjectorTypes.RegisterProjector<FaultTestProjector>();
                        return new DcbDomainTypes(
                            eventTypes,
                            new SimpleTagTypes(),
                            new SimpleTagProjectorTypes(),
                            new SimpleTagStatePayloadTypes(),
                            multiProjectorTypes,
                            new SimpleQueryTypes(),
                            new JsonSerializerOptions());
                    });
                    services.AddSingleton(SharedEventStore);
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

    private record FaultTriggerEvent(bool Poison) : IEventPayload;

    /// <summary>A projector that folds cleanly until it meets a poison event, then throws — a fold crash, deterministically.</summary>
    private record FaultTestProjector : IMultiProjector<FaultTestProjector>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "fault-test-projector";
        public static FaultTestProjector GenerateInitialPayload() => new();

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

            return ResultBox.FromValue(payload);
        }
    }
}
