using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     Tests verifying that query results include IsCatchUpInProgress flag
///     and that the waitForCatchUp parameter works correctly.
/// </summary>
public class CatchUpQueryAwarenessTests : IAsyncLifetime
{
    private static readonly InMemoryEventStore SharedEventStore = new();
    private TestCluster _cluster = null!;
    private IClusterClient _client => _cluster.Client;

    public async Task InitializeAsync()
    {
        SharedEventStore.Clear();

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"CatchUpTest-{uniqueId}";
        builder.Options.ServiceId = $"CatchUpTestSvc-{uniqueId}";
        builder.AddSiloBuilderConfigurator<CatchUpTestSiloConfigurator>();

        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }

    [Fact]
    public async Task ListQuery_WithWaitForCatchUp_ReturnsAllResults()
    {
        const int eventCount = 200;
        var grain = _client.GetGrain<IMultiProjectionGrain>("catchup-counter");

        // Seed events into the event store
        var events = CreateTestEvents(eventCount);
        await grain.SeedEventsAsync(ToSerializableEvents(events));

        // Request deactivation to simulate restart
        await grain.RequestDeactivationAsync();
        await Task.Delay(2000); // Allow deactivation to complete

        // Execute list query WITH waitForCatchUp — should return all items
        var domainTypes = CreateDomainTypes();
        var query = new CountingListQuery();
        var serializableQuery = await SerializableQueryParameter.CreateFromAsync(
            query, domainTypes.JsonSerializerOptions);

        var result = await grain.ExecuteListQueryAsync(serializableQuery, waitForCatchUp: true);

        // Verify we got all results and catch-up is complete
        Assert.NotNull(result);
        Assert.False(result.IsCatchUpInProgress, "IsCatchUpInProgress should be false after waitForCatchUp");
        Assert.Equal(eventCount, result.TotalCount);
    }

    [Fact]
    public async Task ListQuery_AfterNaturalCatchUp_ReturnsIsCatchUpInProgressFalse()
    {
        const int eventCount = 50;
        var grain = _client.GetGrain<IMultiProjectionGrain>("catchup-counter");

        // Seed events and let catch-up complete
        var events = CreateTestEvents(eventCount);
        await grain.SeedEventsAsync(ToSerializableEvents(events));

        // Wait for catch-up to complete via RefreshAsync and polling
        await grain.RefreshAsync();

        // Poll until catch-up completes or timeout
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var status = await grain.GetCatchUpStatusAsync();
            if (!status.IsActive) break;
            await Task.Delay(200);
        }

        var finalStatus = await grain.GetCatchUpStatusAsync();
        Assert.False(finalStatus.IsActive, "Catch-up should complete within the polling timeout");

        // Execute list query — should return all items with IsCatchUpInProgress=false
        var domainTypes = CreateDomainTypes();
        var query = new CountingListQuery();
        var serializableQuery = await SerializableQueryParameter.CreateFromAsync(
            query, domainTypes.JsonSerializerOptions);

        var result = await grain.ExecuteListQueryAsync(serializableQuery);

        Assert.NotNull(result);
        Assert.False(result.IsCatchUpInProgress, "IsCatchUpInProgress should be false after catch-up completes");
        Assert.Equal(eventCount, result.TotalCount);
    }

    [Fact]
    public async Task ListQuery_DefaultBehavior_DoesNotBlock()
    {
        const int eventCount = 200;
        var grain = _client.GetGrain<IMultiProjectionGrain>("catchup-counter");

        // Seed events
        var events = CreateTestEvents(eventCount);
        await grain.SeedEventsAsync(ToSerializableEvents(events));

        // Request deactivation to simulate restart
        await grain.RequestDeactivationAsync();
        await Task.Delay(2000);

        // Execute the original single-parameter overload — should return immediately (not block)
        var domainTypes = CreateDomainTypes();
        var query = new CountingListQuery();
        var serializableQuery = await SerializableQueryParameter.CreateFromAsync(
            query, domainTypes.JsonSerializerOptions);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await grain.ExecuteListQueryAsync(serializableQuery);
        sw.Stop();

        // The call should return quickly (not wait 30 seconds)
        Assert.NotNull(result);
        // We accept any result — the key is that it doesn't block for 30 seconds
        Assert.True(sw.ElapsedMilliseconds < 25000, "Default behavior should not block for catch-up");
    }

    // --- Test domain types ---

    private static List<Event> CreateTestEvents(int count)
    {
        var baseTick = DateTime.UtcNow.Ticks;
        return Enumerable.Range(0, count)
            .Select(i => new Event(
                new CounterIncrementedEvent(i),
                new SortableUniqueId(
                    SortableUniqueId.GetTickString(baseTick + i) +
                    SortableUniqueId.GetIdString(Guid.Empty)),
                nameof(CounterIncrementedEvent),
                Guid.CreateVersion7(),
                new EventMetadata(
                    Guid.NewGuid().ToString(),
                    Guid.NewGuid().ToString(),
                    "test"),
                new List<string>()))
            .ToList();
    }

    private static IReadOnlyList<SerializableEvent> ToSerializableEvents(IEnumerable<Event> events) =>
        events
            .Select(e => new SerializableEvent(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
                e.SortableUniqueIdValue,
                e.Id,
                e.EventMetadata,
                e.Tags.ToList(),
                e.EventType))
            .ToList();

    internal static DcbDomainTypes CreateDomainTypes()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<CounterIncrementedEvent>();

        var tagTypes = new SimpleTagTypes();
        var tagProjectorTypes = new SimpleTagProjectorTypes();
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();

        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjector<CatchUpCountingProjector>();

        var queryTypes = new SimpleQueryTypes();
        queryTypes.RegisterListQuery<CatchUpCountingProjector, CountingListQuery, CountItem>();

        return new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            multiProjectorTypes,
            queryTypes,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
    }

    // --- Event ---
    public record CounterIncrementedEvent(int Value) : IEventPayload;

    // --- Multi-projector that counts events ---
    public record CatchUpCountingProjector : IMultiProjector<CatchUpCountingProjector>
    {
        public int Count { get; init; }
        public CatchUpCountingProjector() => Count = 0;

        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "catchup-counter";

        public static CatchUpCountingProjector GenerateInitialPayload() => new();

        public static ResultBox<CatchUpCountingProjector> Project(
            CatchUpCountingProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) =>
            ResultBox.FromValue(new CatchUpCountingProjector { Count = payload.Count + 1 });
    }

    // --- Query result item ---
    public record CountItem(int Value);

    // --- List query ---
    public sealed record CountingListQuery(
        int? PageNumber = null,
        int? PageSize = null
    ) : ICoreMultiProjectionListQuery<CatchUpCountingProjector, CountingListQuery, CountItem>,
        IQueryPagingParameter,
        IEquatable<CountingListQuery>
    {
        public static ResultBox<IEnumerable<CountItem>> HandleFilter(
            CatchUpCountingProjector projector,
            CountingListQuery query,
            IQueryContext context)
        {
            // Return one item per counted event
            var items = Enumerable.Range(0, projector.Count)
                .Select(i => new CountItem(i));
            return ResultBox.FromValue(items);
        }

        public static ResultBox<IEnumerable<CountItem>> HandleSort(
            IEnumerable<CountItem> filteredList,
            CountingListQuery query,
            IQueryContext context) =>
            ResultBox.FromValue(filteredList.OrderBy(item => item.Value).AsEnumerable());
    }

    // --- Silo configurator ---
    private class CatchUpTestSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton<DcbDomainTypes>(_ => CreateDomainTypes());
                    services.AddSingleton<IEventStore>(SharedEventStore);
                    services.AddSingleton<IMultiProjectionStateStore, InMemoryMultiProjectionStateStore>();
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver(
                            "EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
                    services.AddSingleton<Sekiban.Dcb.Snapshots.IBlobStorageSnapshotAccessor,
                        MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics,
                        NoOpMultiProjectionEventStatistics>();
                    services.AddTransient<GeneralMultiProjectionActorOptions>(_ =>
                        new GeneralMultiProjectionActorOptions
                        {
                            SafeWindowMs = 20000
                        });
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
