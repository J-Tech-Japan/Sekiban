using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Streams;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Sekiban.Dcb.TestSupport;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     The development-time event delivery statistics (<see cref="RecordingMultiProjectionEventStatistics" />) must
///     count events delivered to the MultiProjectionGrain through event-store catch-up and through the Orleans stream.
/// </summary>
public class EventDeliveryStatisticsOrleansTests : IAsyncLifetime
{
    private static readonly InMemoryEventStore SharedEventStore = new();
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"TestCluster-{uniqueId}";
        builder.Options.ServiceId = $"TestService-{uniqueId}";
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        builder.AddClientBuilderConfigurator<ClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
        SharedEventStore.Clear();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }

    [Fact]
    public async Task RecordingStatistics_CountCatchUpAndStreamDeliveries()
    {
        var grain = _cluster.Client.GetGrain<IMultiProjectionGrain>(DeliveryCountingMulti.MultiProjectorName);
        await grain.GetStatusAsync();

        // Catch-up path: events written to the authoritative store and read by the grain's catch-up.
        var catchUpEvents = CreateEvents(3);
        var write = await SharedEventStore.WriteSerializableEventsAsync(catchUpEvents).AsResultBoxAsync();
        Assert.True(write.IsSuccess);
        await grain.RefreshAsync();
        await WaitUntilEventsProcessedAsync(grain, 3);

        // Stream path: events delivered through the production Orleans stream subscription.
        var streamEvents = CreateEvents(2);
        var stream = _cluster.Client
            .GetStreamProvider("EventStreamProvider")
            .GetStream<SerializableEvent>(StreamId.Create(
                ServiceIdGrainKey.BuildStreamNamespace("AllEvents", DefaultServiceIdProvider.DefaultServiceId),
                Guid.Empty));
        await stream.OnNextBatchAsync(streamEvents);
        await WaitUntilEventsProcessedAsync(grain, 5);

        var stats = await grain.GetEventDeliveryStatisticsAsync();
        Assert.Equal(5, stats.TotalUniqueEvents);
        Assert.True(stats.CatchUpDeliveries >= 3, $"catch-up deliveries: {stats.CatchUpDeliveries}");
        Assert.Equal(3, stats.CatchUpUniqueEvents);
        Assert.True(stats.StreamDeliveries >= 2, $"stream deliveries: {stats.StreamDeliveries}");
        Assert.Equal(2, stats.StreamUniqueEvents);
        Assert.True(stats.TotalDeliveries >= 5, $"total deliveries: {stats.TotalDeliveries}");
    }

    private static List<SerializableEvent> CreateEvents(int count)
    {
        var baseTick = DateTime.UtcNow.AddSeconds(-30).Ticks;
        return Enumerable.Range(0, count)
            .Select(index =>
            {
                var payload = new DeliveryCounted(index);
                return new SerializableEvent(
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)),
                    SortableUniqueId.GetTickString(baseTick + index) + SortableUniqueId.GetIdString(Guid.NewGuid()),
                    Guid.NewGuid(),
                    new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
                    [],
                    nameof(DeliveryCounted));
            })
            .ToList();
    }

    private static async Task WaitUntilEventsProcessedAsync(IMultiProjectionGrain grain, long expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        long last = -1;
        while (DateTime.UtcNow < deadline)
        {
            last = (await grain.GetStatusAsync()).EventsProcessed;
            if (last >= expected)
            {
                return;
            }
            await Task.Delay(100);
        }
        Assert.Fail($"EventsProcessed stayed at {last}; expected {expected}.");
    }

    public record DeliveryCounted(int Index) : IEventPayload;

    public record DeliveryCountingMulti : IMultiProjector<DeliveryCountingMulti>
    {
        public int Count { get; init; }

        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "delivery-counting";
        public static DeliveryCountingMulti GenerateInitialPayload() => new();

        public static ResultBox<DeliveryCountingMulti> Project(
            DeliveryCountingMulti payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) =>
            ResultBox.FromValue(payload with { Count = payload.Count + 1 });
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton<DcbDomainTypes>(_ =>
                    {
                        var eventTypes = new SimpleEventTypes();
                        eventTypes.RegisterEventType<DeliveryCounted>();
                        var multiProjectorTypes = new SimpleMultiProjectorTypes();
                        multiProjectorTypes.RegisterProjector<DeliveryCountingMulti>();
                        return new DcbDomainTypes(
                            eventTypes,
                            new SimpleTagTypes(),
                            new SimpleTagProjectorTypes(),
                            new SimpleTagStatePayloadTypes(),
                            multiProjectorTypes,
                            new SimpleQueryTypes(),
                            new JsonSerializerOptions());
                    });
                    services.AddSingleton<IEventStore>(SharedEventStore);
                    services.AddSingleton<IMultiProjectionStateStore, InMemoryMultiProjectionStateStore>();
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
                    services.AddSingleton<Sekiban.Dcb.Snapshots.IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics, RecordingMultiProjectionEventStatistics>();
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions { SafeWindowMs = 20000 });
                    services.AddSekibanDcbNativeRuntime();
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("OrleansStorage")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }

    private sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) =>
            clientBuilder.AddMemoryStreams("EventStreamProvider");
    }
}
