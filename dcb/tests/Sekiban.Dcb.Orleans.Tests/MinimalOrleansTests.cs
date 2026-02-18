using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.InMemory;
using System.Text.Json;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     Minimal Orleans tests to verify Orleans integration works
/// </summary>
public class MinimalOrleansTests : IAsyncLifetime
{
    private static readonly CountingInMemoryEventStore SharedEventStore = new();
    private TestCluster _cluster = null!;
    private IClusterClient _client => _cluster.Client;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"TestCluster-{uniqueId}";
        builder.Options.ServiceId = $"TestService-{uniqueId}";
        // Use real networking with explicit fixed ports to avoid client assuming 30000 while silo chooses dynamic port.
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();

        _cluster = builder.Build();
        await _cluster.DeployAsync();
        SharedEventStore.Clear();
        SharedEventStore.ClearReadAllEventsTracking();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }

    [Fact]
    public async Task Orleans_TestCluster_Should_Start_Successfully()
    {
        // Assert
        Assert.NotNull(_cluster);
        Assert.NotNull(_client);
        Assert.NotNull(_cluster.ServiceProvider);
    }

    [Fact]
    public async Task Orleans_Should_Activate_MultiProjectionGrain()
    {
        // Arrange & Act
        var grain = _client.GetGrain<IMultiProjectionGrain>("test-projector");
        var status = await grain.GetStatusAsync();

        // Assert
        Assert.NotNull(status);
        Assert.Equal("test-projector", status.ProjectorName);
        Assert.Equal(0, status.EventsProcessed);
        Assert.True(status.IsSubscriptionActive); // Subscription auto-starts on activation
    }

    [Fact]
    public async Task Orleans_MultiProjectionCatchUp_Should_Read_With_BatchLimit()
    {
        var grain = _client.GetGrain<IMultiProjectionGrain>("test-projector");
        SharedEventStore.ClearReadAllEventsTracking();

        var baseTick = DateTime.UtcNow.Ticks;
        var events = Enumerable.Range(0, 3501)
            .Select(i => new Event(
                new TestProjectionEvent(i),
                new SortableUniqueId(
                    SortableUniqueId.GetTickString(baseTick + i) + SortableUniqueId.GetIdString(Guid.Empty)),
                nameof(TestProjectionEvent),
                Guid.CreateVersion7(),
                new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
                new List<string>()))
            .ToList();

        await grain.SeedEventsAsync(events);
        await grain.RefreshAsync();

        var due = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < due && SharedEventStore.ReadAllEventsCallCount == 0)
        {
            await Task.Delay(100);
        }

        Assert.True(SharedEventStore.ReadAllEventsCallCount > 0);
        Assert.All(SharedEventStore.ReadAllEventsMaxCounts, maxCount => Assert.Equal(3000, maxCount));
    }

    [Fact]
    public async Task Orleans_Should_Support_Multiple_Grain_Instances()
    {
        // Arrange & Act
        var grain1 = _client.GetGrain<IMultiProjectionGrain>("projector-1");
        var grain2 = _client.GetGrain<IMultiProjectionGrain>("projector-2");

        var status1 = await grain1.GetStatusAsync();
        var status2 = await grain2.GetStatusAsync();

        // Assert
        Assert.Equal("projector-1", status1.ProjectorName);
        Assert.Equal("projector-2", status2.ProjectorName);
    }

    [Fact]
    public async Task Orleans_Grain_Should_Manage_Subscription_State()
    {
        // Arrange
        var grain = _client.GetGrain<IMultiProjectionGrain>("subscription-test");

        // Act - Get initial status (subscription auto-starts on activation)
        var initialStatus = await grain.GetStatusAsync();

        // Start subscription explicitly (idempotent)
        await grain.StartSubscriptionAsync();
        var activeStatus = await grain.GetStatusAsync();

        // Stop subscription
        await grain.StopSubscriptionAsync();
        var stoppedStatus = await grain.GetStatusAsync();

        // Start subscription again
        await grain.StartSubscriptionAsync();
        var reactivatedStatus = await grain.GetStatusAsync();

        // Assert
        Assert.True(initialStatus.IsSubscriptionActive); // Auto-started on activation
        Assert.True(activeStatus.IsSubscriptionActive);
        Assert.False(stoppedStatus.IsSubscriptionActive);
        Assert.True(reactivatedStatus.IsSubscriptionActive);
    }

    [Fact]
    public async Task Orleans_Grain_Should_Return_Snapshot_Envelope()
    {
        // Arrange
        var grain = _client.GetGrain<IMultiProjectionGrain>("serialization-test");

        // Act
        var stateResult = await grain.GetSnapshotJsonAsync();

        // Assert
        Assert.NotNull(stateResult);
        Assert.True(stateResult.IsSuccess);
        var env = JsonSerializer.Deserialize<Sekiban.Dcb.Snapshots.SerializableMultiProjectionStateEnvelope>(stateResult.GetValue(), new JsonSerializerOptions());
        Assert.NotNull(env);
    }

    [Fact]
    public async Task Orleans_Grain_Should_Handle_Persistence()
    {
        // Arrange
        var grain = _client.GetGrain<IMultiProjectionGrain>("persistence-test");

        // Act
        var persistResult = await grain.PersistStateAsync();

        // Assert
        Assert.NotNull(persistResult);
        Assert.True(persistResult.IsSuccess);

        // Get status to verify persistence details
        var status = await grain.GetStatusAsync();
        Assert.NotNull(status.LastPersistTime);
    }

    [Fact]
    public async Task Orleans_Should_Isolate_TagConsistentGrain_By_ServiceId()
    {
        var tagId = "order:123";
        var tenantA = ServiceIdGrainKey.Build("tenant-a", tagId);
        var tenantB = ServiceIdGrainKey.Build("tenant-b", tagId);

        var grainA = _client.GetGrain<ITagConsistentGrain>(tenantA);
        var grainB = _client.GetGrain<ITagConsistentGrain>(tenantB);

        var reservationA = await grainA.MakeReservationAsync(string.Empty);
        var reservationB = await grainB.MakeReservationAsync(string.Empty);

        Assert.True(reservationA.IsSuccess);
        Assert.True(reservationB.IsSuccess);
        Assert.Equal(tagId, reservationA.GetValue().Tag);
        Assert.Equal(tagId, reservationB.GetValue().Tag);
        Assert.Equal(tagId, await grainA.GetTagActorIdAsync());
        Assert.Equal(tagId, await grainB.GetTagActorIdAsync());
    }

    [Fact]
    public async Task Orleans_Should_Isolate_TagStateGrain_By_ServiceId()
    {
        var tagStateId = "order:123:projector";
        var tenantA = ServiceIdGrainKey.Build("tenant-a", tagStateId);
        var tenantB = ServiceIdGrainKey.Build("tenant-b", tagStateId);

        var grainA = _client.GetGrain<ITagStateGrain>(tenantA);
        var grainB = _client.GetGrain<ITagStateGrain>(tenantB);

        Assert.Equal(tagStateId, await grainA.GetTagStateActorIdAsync());
        Assert.Equal(tagStateId, await grainB.GetTagStateActorIdAsync());
    }

    [Fact]
    public void DefaultOrleansEventSubscriptionResolver_Should_Separate_StreamNamespace_By_ServiceId()
    {
        var resolver = new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty);
        var tenantKey = ServiceIdGrainKey.Build("tenant-a", "projector");

        var tenantStream = resolver.Resolve(tenantKey) as OrleansSekibanStream;
        var defaultStream = resolver.Resolve("projector") as OrleansSekibanStream;

        Assert.NotNull(tenantStream);
        Assert.NotNull(defaultStream);
        Assert.Equal("tenant-a|AllEvents", tenantStream!.StreamNamespace);
        Assert.Equal("AllEvents", defaultStream!.StreamNamespace);
    }

    private class TestSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    // Add required domain types for Orleans
                    services.AddSingleton<DcbDomainTypes>(provider =>
                    {
                        var eventTypes = new SimpleEventTypes();
                        var tagTypes = new SimpleTagTypes();
                        var tagProjectorTypes = new SimpleTagProjectorTypes();
                        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
                        var multiProjectorTypes = new SimpleMultiProjectorTypes();
                        var queryTypes = new SimpleQueryTypes();
                        multiProjectorTypes.RegisterProjector<EmptyTestMultiProjector>();
                        multiProjectorTypes.RegisterProjector<TestProjectorMulti>();
                        multiProjectorTypes.RegisterProjector<Projector1Multi>();
                        multiProjectorTypes.RegisterProjector<Projector2Multi>();
                        multiProjectorTypes.RegisterProjector<SubscriptionTestMulti>();
                        multiProjectorTypes.RegisterProjector<SerializationTestMulti>();
                        multiProjectorTypes.RegisterProjector<PersistenceTestMulti>();

                        return new DcbDomainTypes(
                            eventTypes,
                            tagTypes,
                            tagProjectorTypes,
                            tagStatePayloadTypes,
                            multiProjectorTypes,
                            queryTypes,
                            new JsonSerializerOptions());
                    });

                    // Add storage
                    services.AddSingleton<IEventStore>(SharedEventStore);
                    services.AddSingleton<IMultiProjectionStateStore, Sekiban.Dcb.InMemory.InMemoryMultiProjectionStateStore>();
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
                    // Add mock IBlobStorageSnapshotAccessor for tests
                    services.AddSingleton<Sekiban.Dcb.Snapshots.IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    // Add event statistics for MultiProjectionGrain
                    services.AddTransient<Sekiban.Dcb.MultiProjections.IMultiProjectionEventStatistics, Sekiban.Dcb.MultiProjections.NoOpMultiProjectionEventStatistics>();
                    // Add actor options for MultiProjectionGrain
                    services.AddTransient<Sekiban.Dcb.Actors.GeneralMultiProjectionActorOptions>(_ => new Sekiban.Dcb.Actors.GeneralMultiProjectionActorOptions
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


    private record EmptyTestMultiProjector : IMultiProjector<EmptyTestMultiProjector>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "empty-test";
        public static EmptyTestMultiProjector GenerateInitialPayload() => new();
        public static ResultBox<EmptyTestMultiProjector> Project(
            EmptyTestMultiProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload);
    }

    private record TestProjectorMulti : IMultiProjector<TestProjectorMulti>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "test-projector";
        public static TestProjectorMulti GenerateInitialPayload() => new();
        public static ResultBox<TestProjectorMulti> Project(TestProjectorMulti payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) =>
                ResultBox.FromValue(payload);
    }

    private record Projector1Multi : IMultiProjector<Projector1Multi>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "projector-1";
        public static Projector1Multi GenerateInitialPayload() => new();
        public static ResultBox<Projector1Multi> Project(Projector1Multi payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) =>
                ResultBox.FromValue(payload);
    }

    private record Projector2Multi : IMultiProjector<Projector2Multi>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "projector-2";
        public static Projector2Multi GenerateInitialPayload() => new();
        public static ResultBox<Projector2Multi> Project(Projector2Multi payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) =>
                ResultBox.FromValue(payload);
    }

    private record SubscriptionTestMulti : IMultiProjector<SubscriptionTestMulti>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "subscription-test";
        public static SubscriptionTestMulti GenerateInitialPayload() => new();
        public static ResultBox<SubscriptionTestMulti>
            Project(SubscriptionTestMulti payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload);
    }

    private record SerializationTestMulti : IMultiProjector<SerializationTestMulti>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "serialization-test";
        public static SerializationTestMulti GenerateInitialPayload() => new();
        public static ResultBox<SerializationTestMulti> Project(
            SerializationTestMulti payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload);
    }

    private record PersistenceTestMulti : IMultiProjector<PersistenceTestMulti>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "persistence-test";
        public static PersistenceTestMulti GenerateInitialPayload() => new();
        public static ResultBox<PersistenceTestMulti>
            Project(PersistenceTestMulti payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload);
    }

    private record TestProjectionEvent(int Value) : IEventPayload;

    private class CountingInMemoryEventStore : IEventStore
    {
        private readonly InMemoryEventStore _inner = new();
        private readonly object _lock = new();
        private readonly List<int?> _readAllEventsMaxCounts = new();

        public int ReadAllEventsCallCount { get; private set; }

        public IReadOnlyList<int?> ReadAllEventsMaxCounts
        {
            get
            {
                lock (_lock)
                {
                    return _readAllEventsMaxCounts.ToList();
                }
            }
        }

        public void Clear() => _inner.Clear();

        public void ClearReadAllEventsTracking()
        {
            lock (_lock)
            {
                ReadAllEventsCallCount = 0;
                _readAllEventsMaxCounts.Clear();
            }
        }

        public Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null, int? maxCount = null)
        {
            lock (_lock)
            {
                ReadAllEventsCallCount++;
                _readAllEventsMaxCounts.Add(maxCount);
            }

            return _inner.ReadAllEventsAsync(since, maxCount);
        }

        public Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null) =>
            _inner.ReadEventsByTagAsync(tag, since);

        public Task<ResultBox<Event>> ReadEventAsync(Guid eventId) => _inner.ReadEventAsync(eventId);

        public Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteEventsAsync(
            IEnumerable<Event> events) => _inner.WriteEventsAsync(events);

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => _inner.ReadTagsAsync(tag);

        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => _inner.GetLatestTagAsync(tag);

        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => _inner.TagExistsAsync(tag);

        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => _inner.GetEventCountAsync(since);

        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => _inner.GetAllTagsAsync(tagGroup);

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            _inner.ReadAllSerializableEventsAsync(since);

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null) => _inner.ReadSerializableEventsByTagAsync(tag, since);

        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) => _inner.WriteSerializableEventsAsync(events);
    }
}
