using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Grains;
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
        Assert.False(status.IsSubscriptionActive); // Grain uses lazy subscription, not activated until first query/state access
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

        // Act - Get initial status (no subscription yet due to lazy subscription)
        var initialStatus = await grain.GetStatusAsync();

        // Start subscription explicitly
        await grain.StartSubscriptionAsync();
        var activeStatus = await grain.GetStatusAsync();

        // Stop subscription
        await grain.StopSubscriptionAsync();
        var stoppedStatus = await grain.GetStatusAsync();

        // Start subscription again
        await grain.StartSubscriptionAsync();
        var reactivatedStatus = await grain.GetStatusAsync();

        // Assert
        Assert.False(initialStatus.IsSubscriptionActive); // Lazy subscription, not activated until explicitly started
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
                    services.AddSingleton<IEventStore, InMemoryEventStore>();
                    services.AddSingleton<IMultiProjectionStateStore, Sekiban.Dcb.InMemory.InMemoryMultiProjectionStateStore>();
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    // Add mock IBlobStorageSnapshotAccessor for tests
                    services.AddSingleton<Sekiban.Dcb.Snapshots.IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    // Add event statistics for MultiProjectionGrain
                    services.AddTransient<Sekiban.Dcb.MultiProjections.IMultiProjectionEventStatistics, Sekiban.Dcb.MultiProjections.NoOpMultiProjectionEventStatistics>();
                    // Add actor options for MultiProjectionGrain
                    services.AddTransient<Sekiban.Dcb.Actors.GeneralMultiProjectionActorOptions>(_ => new Sekiban.Dcb.Actors.GeneralMultiProjectionActorOptions
                    {
                        SafeWindowMs = 20000
                    });
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
}
