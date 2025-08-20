using FluentAssertions;
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

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<TestClientConfigurator>();
        
        _cluster = builder.Build();
        await _cluster.DeployAsync();
        
        // Get services and create executor
        _domainTypes = _cluster.ServiceProvider.GetRequiredService<DcbDomainTypes>();
        _eventStore = _cluster.ServiceProvider.GetRequiredService<IEventStore>();
        _executor = new OrleansDcbExecutor(_client, _eventStore, _domainTypes);
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }

    [Fact]
    public async Task Should_Execute_Command_Through_Orleans()
    {
        // Arrange
        var aggregateId = Guid.NewGuid();
        var command = new CreateTestEntityCommand { AggregateId = aggregateId, Name = "Test Entity" };
        
        // Act - Execute command with handler
        var result = await _executor.ExecuteAsync(
            command,
            (cmd, context) =>
            {
                var @event = new TestEntityCreatedEvent { AggregateId = cmd.AggregateId, Name = cmd.Name };
                var tag = new TestAggregateTag(cmd.AggregateId);
                return Task.FromResult<ResultBox<EventOrNone>>(EventOrNone.EventWithTags(@event, tag));
            });
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        result.GetValue().Should().NotBeNull();
        result.GetValue().EventId.Should().NotBeEmpty();
        result.GetValue().TagWrites.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Should_Get_TagState_After_Command()
    {
        // Arrange - Execute a command first
        var aggregateId = Guid.NewGuid();
        var command = new CreateTestEntityCommand { AggregateId = aggregateId, Name = "Entity for State" };
        
        var executionResult = await _executor.ExecuteAsync(
            command,
            (cmd, context) =>
            {
                var @event = new TestEntityCreatedEvent { AggregateId = cmd.AggregateId, Name = cmd.Name };
                var tag = new TestAggregateTag(cmd.AggregateId);
                return Task.FromResult<ResultBox<EventOrNone>>(EventOrNone.EventWithTags(@event, tag));
            });
        
        executionResult.IsSuccess.Should().BeTrue();
        
        // Act - Get the tag state using the aggregate ID
        var tag = new TestAggregateTag(aggregateId);
        var tagStateId = new TagStateId(tag, "TestProjector");
        var tagStateResult = await _executor.GetTagStateAsync(tagStateId);
        
        // Assert
        tagStateResult.IsSuccess.Should().BeTrue();
        var tagState = tagStateResult.GetValue();
        tagState.Should().NotBeNull();
        tagState.Version.Should().Be(1);
        tagState.Version.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Should_Query_Through_MultiProjection_Grain()
    {
        // Arrange - Get the projection grain
        var projectorName = "TestProjector";
        var grain = _client.GetGrain<IMultiProjectionGrain>(projectorName);
        
        // Create and add some test events
        var events = new List<Event>();
        for (int i = 0; i < 3; i++)
        {
            var evt = new Event(
                new TestEntityCreatedEvent { AggregateId = Guid.NewGuid(), Name = $"Entity {i}" },
                "TestAggregate",
                $"TestAggregate:{Guid.NewGuid()}",
                Guid.NewGuid(),
                null,
                new List<string> { $"TestAggregate:{Guid.NewGuid()}" });
            events.Add(evt);
        }
        
        await grain.AddEventsAsync(events, true);
        
        // Act - Execute a query through grain
        var query = new TestListQuery();
        var queryResult = await grain.ExecuteListQueryAsync(query);
        
        // Assert
        queryResult.Should().NotBeNull();
        queryResult.Query.Should().Be(query);
        queryResult.Items.Should().NotBeNull();
    }

    [Fact]
    public async Task Should_Update_Aggregate_Multiple_Times()
    {
        // Arrange - Create aggregate
        var aggregateId = Guid.NewGuid();
        var createCommand = new CreateTestEntityCommand { AggregateId = aggregateId, Name = "Initial" };
        
        var createResult = await _executor.ExecuteAsync(
            createCommand,
            (cmd, context) =>
            {
                var @event = new TestEntityCreatedEvent { AggregateId = cmd.AggregateId, Name = cmd.Name };
                var tag = new TestAggregateTag(cmd.AggregateId);
                return Task.FromResult<ResultBox<EventOrNone>>(EventOrNone.EventWithTags(@event, tag));
            });
        
        createResult.IsSuccess.Should().BeTrue();
        
        // Act - Update the aggregate
        var updateCommand = new UpdateTestEntityCommand { AggregateId = aggregateId, NewName = "Updated" };
        var updateResult = await _executor.ExecuteAsync(
            updateCommand,
            async (cmd, context) =>
            {
                var tag = new TestAggregateTag(cmd.AggregateId);
                var state = await context.GetStateAsync<TestProjector>(tag);
                
                if (!state.IsSuccess)
                {
                    return ResultBox.Error<EventOrNone>(new InvalidOperationException("Aggregate not found"));
                }
                
                var @event = new TestEntityUpdatedEvent { AggregateId = cmd.AggregateId, Name = cmd.NewName };
                return EventOrNone.EventWithTags(@event, tag);
            });
        
        // Assert
        updateResult.IsSuccess.Should().BeTrue();
        updateResult.GetValue().Should().NotBeNull();
        
        // Verify final state using aggregate ID
        var finalTag = new TestAggregateTag(aggregateId);
        var tagStateId = new TagStateId(finalTag, "TestProjector");
        var finalTagState = await _executor.GetTagStateAsync(tagStateId);
        finalTagState.IsSuccess.Should().BeTrue();
        finalTagState.GetValue().Version.Should().Be(2);
    }

    // Test domain classes
    private record CreateTestEntityCommand : ICommand
    {
        public Guid AggregateId { get; init; }
        public string Name { get; init; } = string.Empty;
    }
    
    private record UpdateTestEntityCommand : ICommand
    {
        public Guid AggregateId { get; init; }
        public string NewName { get; init; } = string.Empty;
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
                    Version = 1
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
    
    // Test configurators
    private class TestSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .UseLocalhostClustering()
                .ConfigureServices(services =>
                {
                    // Add domain types with test registrations
                    services.AddSingleton<DcbDomainTypes>(provider =>
                    {
                        var eventTypes = new SimpleEventTypes();
                        eventTypes.RegisterEventType<TestEntityCreatedEvent>();
                        eventTypes.RegisterEventType<TestEntityUpdatedEvent>();
                        
                        var tagTypes = new SimpleTagTypes();
                        // Tags are handled differently in DCB
                        
                        var tagProjectorTypes = new SimpleTagProjectorTypes();
                        tagProjectorTypes.RegisterProjector<TestProjector>();
                        
                        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
                        tagStatePayloadTypes.RegisterPayloadType<TestStatePayload>();
                        
                        var multiProjectorTypes = new SimpleMultiProjectorTypes();
                        var queryTypes = new SimpleQueryTypes();
                        
                        return new DcbDomainTypes(
                            eventTypes,
                            tagTypes,
                            tagProjectorTypes,
                            tagStatePayloadTypes,
                            multiProjectorTypes,
                            queryTypes,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    });
                    
                    // Add storage and subscription
                    services.AddSingleton<IEventStore, Sekiban.Dcb.Tests.InMemoryEventStore>();
                    services.AddSingleton<IEventSubscription, InMemoryEventSubscription>();
                    services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("OrleansStorage");
        }
    }
    
    private class TestClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.UseLocalhostClustering();
        }
    }
}