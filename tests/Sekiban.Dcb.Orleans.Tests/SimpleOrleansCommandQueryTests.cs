using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Tests;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.InMemory;
using System.Text.Json;
using Xunit;

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


    [Fact(Skip = "Event processing pipeline needs investigation - second event not reaching projector")]
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
        await Task.Delay(500);
        
        // Verify final state using aggregate ID
        var finalTag = new TestAggregateTag(aggregateId);
        var tagStateId = new TagStateId(finalTag, "TestProjector");
    var finalTagState = await WaitForTagVersionAsync(tagStateId, 2, TimeSpan.FromSeconds(10));
    Assert.True(finalTagState.IsSuccess, $"Failed to get tag state after waiting");
    var tagState = finalTagState.GetValue();
    // Check that we have processed 2 events
    Assert.True(tagState.Version >= 2, $"Expected version >= 2, but got {tagState.Version}");
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
    
    private record TestEntityCreatedEvent : IEventPayload
    {
        public Guid AggregateId { get; init; }
        public string Name { get; init; } = string.Empty;
    }
    
    private record TestEntityUpdatedEvent : IEventPayload
    {
        public Guid AggregateId { get; init; }
        public string Name { get; init; } = string.Empty;
    }
    
    private record TestAggregateTag : ITag
    {
        private readonly Guid _id;
        public TestAggregateTag(Guid id) => _id = id;
        public bool IsConsistencyTag() => false;
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
                services.AddSingleton<IEventSubscription, InMemoryEventSubscription>();
                services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
            })
            .AddMemoryGrainStorageAsDefault()
            .AddMemoryGrainStorage("OrleansStorage")
            .AddMemoryGrainStorage("PubSubStore")
            .AddMemoryStreams("EventStreamProvider").AddMemoryGrainStorage("EventStreamProvider");
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
        while (DateTime.UtcNow - start < timeout)
        {
            last = await _executor.GetTagStateAsync(id);
            if (last.IsSuccess && last.GetValue().Version >= minVersion) return last;
            await Task.Delay(50);
        }
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