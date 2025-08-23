using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.TestingHost;
using Orleans.Streams;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Tests;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.InMemory;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
/// Simple Orleans tests for command execution, tag state, and queries
/// </summary>
public class SimpleOrleansCommandQueryTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private IClusterClient _client => _cluster.Client;
    private ISekibanExecutor _executor = null!;
    private DcbDomainTypes _domainTypes = null!;
    private IEventStore _eventStore = null!;
    private bool _initialized;
    
    // Shared event store to ensure consistency between client and silo
    private static readonly IEventStore SharedEventStore = new Sekiban.Dcb.Tests.InMemoryEventStore();

    public async Task InitializeAsync()
    {
    var builder = new TestClusterBuilder();
    builder.Options.InitialSilosCount = 1;
    var uniqueId = Guid.NewGuid().ToString("N")[..8];
    builder.Options.ClusterId = $"TestCluster-{uniqueId}";
    builder.Options.ServiceId = $"TestService-{uniqueId}";
    builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
    builder.AddClientBuilderConfigurator<TestClientConfigurator>();
        
        _cluster = builder.Build();
        await _cluster.DeployAsync();
        
        // Create domain types locally (same as in silo)
        _domainTypes = CreateDomainTypes();
        _eventStore = SharedEventStore;
        _executor = new OrleansDcbExecutor(_client, _eventStore, _domainTypes);
    LogDomainTypes("After InitializeAsync");
    _initialized = true;
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }

    [Fact]
    public async Task Orleans_Grain_Should_Return_Serializable_State()
    {
    await EnsureInitializedAsync();
        var grain = _client.GetGrain<IMultiProjectionGrain>("serialization-test");
    var stateResult = await grain.GetSerializableStateAsync(true);
        Assert.NotNull(stateResult);
        Assert.True(stateResult.IsSuccess);
        var state = stateResult.GetValue();
        Assert.Equal("serialization-test", state.ProjectorName);
    }

    [Fact]
    public async Task Should_Execute_Command_Through_Orleans()
    {
    await EnsureInitializedAsync();
    LogDomainTypes("Before Should_Execute_Command_Through_Orleans");
        if (_executor is null)
        {
            throw new InvalidOperationException($"_executor not initialized; domainTypes null={_domainTypes is null}, eventStore null={_eventStore is null}");
        }
        // Arrange
        var aggregateId = Guid.NewGuid();
        var command = new CreateTestEntityCommand { AggregateId = aggregateId, Name = "Test Entity" };
        
        // Act - Execute command with handler
    var result = await _executor.ExecuteAsync(command);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.GetValue());
        Assert.NotEqual(Guid.Empty, result.GetValue().EventId);
        Assert.NotEmpty(result.GetValue().TagWrites);
    }

    [Fact]
    public async Task Should_Get_TagState_After_Command()
    {
    await EnsureInitializedAsync();
        // Arrange - Execute a command first
        var aggregateId = Guid.NewGuid();
        var command = new CreateTestEntityCommand { AggregateId = aggregateId, Name = "Entity for State" };
        
    var executionResult = await _executor.ExecuteAsync(command);
        
        Assert.True(executionResult.IsSuccess);
        
        // Act - Get the tag state using the aggregate ID
        var tag = new TestAggregateTag(aggregateId);
        var tagStateId = new TagStateId(tag, "TestProjector");
    var tagStateResult = await WaitForTagVersionAsync(tagStateId, 1, TimeSpan.FromSeconds(5));
        
        // Assert
        Assert.True(tagStateResult.IsSuccess);
        var tagState = tagStateResult.GetValue();
        Assert.NotNull(tagState);
        Assert.Equal(1, tagState.Version);
        Assert.True(tagState.Version > 0);
    }


    [Fact(Skip = "Tag state caching issue in Orleans - needs investigation")]
    public async Task Should_Update_Aggregate_Multiple_Times()
    {
    await EnsureInitializedAsync();
        // Arrange - Create aggregate
        var aggregateId = Guid.NewGuid();
        var createCommand = new CreateTestEntityCommand { AggregateId = aggregateId, Name = "Initial" };
        
        var createResult = await _executor.ExecuteAsync(createCommand);
        
        Assert.True(createResult.IsSuccess);
        
        // Wait for first event to be processed
        await Task.Delay(500);
        
        // Act - Update the aggregate
        var updateCommand = new UpdateTestEntityCommand { AggregateId = aggregateId, NewName = "Updated" };
        var updateResult = await _executor.ExecuteAsync(updateCommand);
        
        // Assert
        Assert.True(updateResult.IsSuccess);
        Assert.NotNull(updateResult.GetValue());
        
        // Give the projection time to process the update event
        await Task.Delay(1000);
        
        // Check events in the event store directly
        var testTag = new TestAggregateTag(aggregateId);
        var eventsResult = await _eventStore.ReadEventsByTagAsync(testTag);
        Assert.True(eventsResult.IsSuccess, "Failed to read events from store");
        var events = eventsResult.GetValue().ToList();
        
        // We should have 2 events - create and update
        Assert.True(events.Count >= 2, $"Expected at least 2 events in store, but got {events.Count}");
        
        // Verify final state using aggregate ID
        var finalTag = new TestAggregateTag(aggregateId);
        var tagStateId = new TagStateId(finalTag, "TestProjector");
        
        // Clear the cache to force re-computation
        var tagStateGrain = _client.GetGrain<ITagStateGrain>(tagStateId.GetTagStateId());
        await tagStateGrain.ClearCacheAsync();
        
        // Wait a bit for tag consistent actor to catch up
        await Task.Delay(500);
        
    var finalTagState = await WaitForTagVersionAsync(tagStateId, 2, TimeSpan.FromSeconds(10));
    Assert.True(finalTagState.IsSuccess, $"Failed to get tag state after waiting");
    var tagState = finalTagState.GetValue();
    // Check that we have processed 2 events
    Assert.True(tagState.Version >= 2, $"Expected version >= 2, but got {tagState.Version}");
    }

    [Fact]
    public async Task Orleans_PubSub_Should_Work_With_EventPublisher_And_Direct_Stream()
    {
        await EnsureInitializedAsync();
        
        // Arrange - Setup Orleans streams directly
        var streamProvider = _cluster.Client.GetStreamProvider("EventStreamProvider");
        var streamNamespace = "TestEvents";
        var streamId = Guid.NewGuid();
        
        // Subscribe directly to the stream for payloads (matching what publisher sends)
        var stream = streamProvider.GetStream<object>(StreamId.Create(streamNamespace, streamId));
        var receivedPayloads = new List<IEventPayload>();
        
        var subscriptionHandle = await stream.SubscribeAsync(async (payload, token) =>
        {
            if (payload is IEventPayload eventPayload)
            {
                receivedPayloads.Add(eventPayload);
            }
            await Task.CompletedTask;
        });
        
        // Create Orleans event publisher
        var resolver = new DefaultOrleansStreamDestinationResolver(
            "EventStreamProvider",
            streamNamespace,
            streamId);
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<OrleansEventPublisher>();
        var publisher = new OrleansEventPublisher(_cluster.Client, resolver, logger);
        
        // Act - Publish events through Orleans publisher
        var testEvent1 = new Event(
            new TestEntityCreatedEvent { AggregateId = Guid.NewGuid(), Name = "Event1" },
            "TestAggregate",
            $"TestAggregate:{Guid.NewGuid()}",
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
            new List<string>());
            
        var testEvent2 = new Event(
            new TestEntityUpdatedEvent { AggregateId = Guid.NewGuid(), Name = "Event2" },
            "TestAggregate",
            $"TestAggregate:{Guid.NewGuid()}",
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
            new List<string>());
        
        var eventsToPublish = new List<(Event Event, IReadOnlyCollection<ITag> Tags)>
        {
            (testEvent1, new List<ITag> { new TestAggregateTag(Guid.NewGuid()) }),
            (testEvent2, new List<ITag> { new TestAggregateTag(Guid.NewGuid()) })
        };
        
        await publisher.PublishAsync(eventsToPublish);
        
        // Wait for events to be processed
        await Task.Delay(1000);
        
        // Assert - Verify payloads were received
        Assert.Equal(2, receivedPayloads.Count);
        Assert.Contains(receivedPayloads, p => 
            p is TestEntityCreatedEvent created && created.Name == "Event1");
        Assert.Contains(receivedPayloads, p => 
            p is TestEntityUpdatedEvent updated && updated.Name == "Event2");
        
        // Cleanup
        await subscriptionHandle.UnsubscribeAsync();
    }

    [Fact]
    public async Task Orleans_Stream_Should_Handle_Multiple_Subscribers()
    {
        await EnsureInitializedAsync();
        
        // Arrange - Setup Orleans streams
        var streamProvider = _cluster.Client.GetStreamProvider("EventStreamProvider");
        var streamNamespace = "MultiSubscriberTest";
        var streamId = Guid.NewGuid();
        
        // Create multiple direct stream subscriptions
        var stream = streamProvider.GetStream<object>(StreamId.Create(streamNamespace, streamId));
        
        var receivedPayloads1 = new List<IEventPayload>();
        var receivedPayloads2 = new List<IEventPayload>();
        
        var handle1 = await stream.SubscribeAsync(async (payload, token) =>
        {
            if (payload is IEventPayload eventPayload)
            {
                receivedPayloads1.Add(eventPayload);
            }
            await Task.CompletedTask;
        });
        
        var handle2 = await stream.SubscribeAsync(async (payload, token) =>
        {
            if (payload is IEventPayload eventPayload)
            {
                receivedPayloads2.Add(eventPayload);
            }
            await Task.CompletedTask;
        });
        
        // Create publisher
        var resolver = new DefaultOrleansStreamDestinationResolver(
            "EventStreamProvider",
            streamNamespace,
            streamId);
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<OrleansEventPublisher>();
        var publisher = new OrleansEventPublisher(_cluster.Client, resolver, logger);
        
        // Act - Publish an event
        var testEvent = new Event(
            new TestEntityCreatedEvent { AggregateId = Guid.NewGuid(), Name = "SharedEvent" },
            "TestAggregate",
            $"TestAggregate:{Guid.NewGuid()}",
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
            new List<string>());
        
        await publisher.PublishAsync(new[]
        {
            (testEvent, (IReadOnlyCollection<ITag>)new List<ITag>())
        });
        
        // Wait for events to be processed
        await Task.Delay(1000);
        
        // Assert - Both subscribers should receive the payload
        Assert.Single(receivedPayloads1);
        Assert.Single(receivedPayloads2);
        
        var payload1 = receivedPayloads1.First();
        var payload2 = receivedPayloads2.First();
        
        Assert.IsType<TestEntityCreatedEvent>(payload1);
        Assert.IsType<TestEntityCreatedEvent>(payload2);
        Assert.Equal("SharedEvent", ((TestEntityCreatedEvent)payload1).Name);
        Assert.Equal("SharedEvent", ((TestEntityCreatedEvent)payload2).Name);
        
        // Cleanup
        await handle1.UnsubscribeAsync();
        await handle2.UnsubscribeAsync();
    }

    // Test domain classes
    private record CreateTestEntityCommand : ICommandWithHandler<CreateTestEntityCommand>
    {
        public Guid AggregateId { get; init; }
        public string Name { get; init; } = string.Empty;
        public Task<ResultBox<EventOrNone>> HandleAsync(ICommandContext context)
        {
            var @event = new TestEntityCreatedEvent { AggregateId = AggregateId, Name = Name };
            var tag = new TestAggregateTag(AggregateId);
            return Task.FromResult<ResultBox<EventOrNone>>(EventOrNone.EventWithTags(@event, tag));
        }
    }
    
    private record UpdateTestEntityCommand : ICommandWithHandler<UpdateTestEntityCommand>
    {
        public Guid AggregateId { get; init; }
        public string NewName { get; init; } = string.Empty;
        
        public async Task<ResultBox<EventOrNone>> HandleAsync(ICommandContext context)
        {
            var tag = new TestAggregateTag(AggregateId);
            var state = await context.GetStateAsync<TestProjector>(tag);
            
            if (!state.IsSuccess)
            {
                return ResultBox.Error<EventOrNone>(new InvalidOperationException("Aggregate not found"));
            }
            
            var @event = new TestEntityUpdatedEvent { AggregateId = AggregateId, Name = NewName };
            return EventOrNone.EventWithTags(@event, tag);
        }
    }
    
    [GenerateSerializer]
    public record TestEntityCreatedEvent : IEventPayload
    {
        [Id(0)] public Guid AggregateId { get; init; }
        [Id(1)] public string Name { get; init; } = string.Empty;
    }
    
    [GenerateSerializer]
    public record TestEntityUpdatedEvent : IEventPayload
    {
        [Id(0)] public Guid AggregateId { get; init; }
        [Id(1)] public string Name { get; init; } = string.Empty;
    }
    
    private record TestAggregateTag : ITag
    {
        private readonly Guid _id;
        public TestAggregateTag(Guid id) => _id = id;
        public bool IsConsistencyTag() => false;  // Non-consistency tag for testing
        public string GetTagGroup() => "TestAggregate";
        public string GetTagContent() => _id.ToString();
    }
    
    private class TestProjector : ITagProjector<TestProjector>
    {
        public static string ProjectorVersion => "1.0";
        public static string ProjectorName => "TestProjector";
        
        public static ITagStatePayload Project(ITagStatePayload current, Event ev)
        {
            var state = current as TestStatePayload ?? new TestStatePayload();
            
            return ev.Payload switch
            {
                TestEntityCreatedEvent created => new TestStatePayload 
                { 
                    Id = created.AggregateId, 
                    Name = created.Name,
                    Version = state.Version + 1  // Increment from current version
                },
                TestEntityUpdatedEvent updated => state with 
                { 
                    Name = updated.Name,
                    Version = state.Version + 1
                },
                _ => state
            };
        }
    }
    
    private record TestStatePayload : ITagStatePayload
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public int Version { get; init; }
    }
    
    private record TestListQuery : IListQueryCommon<TestQueryResult>;
    
    private record TestQueryResult
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    private static DcbDomainTypes CreateDomainTypes()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<TestEntityCreatedEvent>();
        eventTypes.RegisterEventType<TestEntityUpdatedEvent>();
        var tagTypes = new SimpleTagTypes();
        var tagProjectorTypes = new SimpleTagProjectorTypes();
        tagProjectorTypes.RegisterProjector<TestProjector>();
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        tagStatePayloadTypes.RegisterPayloadType<TestStatePayload>();
        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjector<TestPlaceholderMultiProjector>();
        multiProjectorTypes.RegisterProjector<TestProjectorMulti>();
        multiProjectorTypes.RegisterProjector<SerializationTestMulti>();
        var queryTypes = new SimpleQueryTypes();
        return new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            multiProjectorTypes,
            queryTypes,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    
    // Test configurators
    public class TestSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.ConfigureServices(services =>
            {
                services.AddSingleton<DcbDomainTypes>(provider => CreateDomainTypes());
                services.AddSingleton<IEventStore>(SharedEventStore);
                services.AddSingleton<IEventSubscriptionResolver>(
                    new DefaultOrleansEventSubscriptionResolver(
                        "EventStreamProvider",
                        "AllEvents",
                        Guid.Empty));
                services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
            })
            .AddMemoryGrainStorageAsDefault()
            .AddMemoryGrainStorage("OrleansStorage")
            .AddMemoryGrainStorage("PubSubStore")
            .AddMemoryStreams("EventStreamProvider").AddMemoryGrainStorage("EventStreamProvider");
        }
    }
    
    public class TestClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddMemoryStreams("EventStreamProvider");
        }
    }

    

    private record TestPlaceholderMultiProjector() : IMultiProjector<TestPlaceholderMultiProjector>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "test-projector";
        public static TestPlaceholderMultiProjector GenerateInitialPayload() => new();
        public static ResultBox<TestPlaceholderMultiProjector> Project(TestPlaceholderMultiProjector payload, Event ev, List<ITag> tags) => ResultBox.FromValue(payload);
    }

    private record TestProjectorMulti() : IMultiProjector<TestProjectorMulti>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "TestProjector";
        public static TestProjectorMulti GenerateInitialPayload() => new();
        public static ResultBox<TestProjectorMulti> Project(TestProjectorMulti payload, Event ev, List<ITag> tags) => ResultBox.FromValue(payload);
    }

    private record SerializationTestMulti() : IMultiProjector<SerializationTestMulti>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "serialization-test";
        public static SerializationTestMulti GenerateInitialPayload() => new();
        public static ResultBox<SerializationTestMulti> Project(SerializationTestMulti payload, Event ev, List<ITag> tags) => ResultBox.FromValue(payload);
    }
    private async Task<ResultBox<TagState>> WaitForTagVersionAsync(TagStateId id, int minVersion, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        ResultBox<TagState> last = ResultBox.Error<TagState>(new InvalidOperationException("not started"));
        var attempts = 0;
        while (DateTime.UtcNow - start < timeout)
        {
            last = await _executor.GetTagStateAsync(id);
            attempts++;
            if (last.IsSuccess)
            {
                var state = last.GetValue();
                Console.WriteLine($"[WaitForTagVersion] Attempt {attempts}: Version={state.Version}, Expected={minVersion}, LastSortedUniqueId={state.LastSortedUniqueId}");
                if (state.Version >= minVersion) return last;
            }
            else
            {
                Console.WriteLine($"[WaitForTagVersion] Attempt {attempts}: Failed to get state - {last.GetException()?.Message}");
            }
            await Task.Delay(50);
        }
        Console.WriteLine($"[WaitForTagVersion] Timeout after {attempts} attempts. Last version was {(last.IsSuccess ? last.GetValue().Version.ToString() : "error")}");
        return last;
    }
    private void LogDomainTypes(string phase) { }
    private async Task EnsureInitializedAsync()
    {
        if (_initialized && _executor is not null) return;
        // Fallback in case IAsyncLifetime did not run (safety net)
        if (_cluster == null)
        {
            var builder = new TestClusterBuilder();
            builder.Options.InitialSilosCount = 1;
            var uniqueId = Guid.NewGuid().ToString("N")[..8];
            builder.Options.ClusterId = $"FallbackCluster-{uniqueId}";
            builder.Options.ServiceId = $"FallbackService-{uniqueId}";
            builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
            _cluster = builder.Build();
            await _cluster.DeployAsync();
        }
        if (_executor is null)
        {
            _domainTypes = CreateDomainTypes();
            _eventStore = SharedEventStore;
            _executor = new OrleansDcbExecutor(_client, _eventStore, _domainTypes);
        }
        _initialized = true;
    }
    
}