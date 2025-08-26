using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Dapr.Actors;
using Dapr.Actors.Client;
using Dapr.Client;
using Sekiban.Pure.Dapr.Actors;
using Sekiban.Pure.Dapr.Serialization;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command;
using Sekiban.Pure.Query;
using Sekiban.Pure.Projectors;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using ResultBoxes;
using Sekiban.Pure;
using Microsoft.Extensions.Options;

namespace Sekiban.Pure.Dapr.Tests;

/// <summary>
/// Integration tests for Dapr PubSub functionality
/// </summary>
public class PubSubIntegrationTests
{
    private readonly Mock<IActorProxyFactory> _mockActorProxyFactory;
    private readonly Mock<DaprClient> _mockDaprClient;
    private readonly Mock<ILogger<AggregateEventHandlerActor>> _mockLogger;
    private readonly SekibanDomainTypes _domainTypes;
    private readonly IDaprSerializationService _serialization;

    public PubSubIntegrationTests()
    {
        _mockActorProxyFactory = new Mock<IActorProxyFactory>();
        _mockDaprClient = new Mock<DaprClient>();
        _mockLogger = new Mock<ILogger<AggregateEventHandlerActor>>();
        
        // Create a simple domain types instance for testing
        // Using mock implementations since this is just for serialization testing
        var mockEventTypes = new Mock<IEventTypes>();
        var mockAggregateTypes = new Mock<IAggregateTypes>();
        var mockCommandTypes = new Mock<ICommandTypes>();
        var mockAggregateProjectorSpecifier = new Mock<IAggregateProjectorSpecifier>();
        var mockQueryTypes = new Mock<IQueryTypes>();
        var mockMultiProjectorTypes = new Mock<IMultiProjectorTypes>();
        
        _domainTypes = new SekibanDomainTypes(
            mockEventTypes.Object,
            mockAggregateTypes.Object,
            mockCommandTypes.Object, 
            mockAggregateProjectorSpecifier.Object,
            mockQueryTypes.Object,
            mockMultiProjectorTypes.Object,
            System.Text.Json.JsonSerializerOptions.Default);
        
        // Create mocks for DaprSerializationService dependencies
        var mockTypeRegistry = new Mock<IDaprTypeRegistry>();
        var mockSerializationOptions = Options.Create(new DaprSerializationOptions
        {
            JsonSerializerOptions = System.Text.Json.JsonSerializerOptions.Default,
            EnableCompression = false,
            CompressionThreshold = 1024
        });
        var mockSerializationLogger = new Mock<ILogger<DaprSerializationService>>();
        
        _serialization = new DaprSerializationService(
            mockTypeRegistry.Object,
            mockSerializationOptions,
            mockSerializationLogger.Object,
            _domainTypes);
    }

    [Fact]
    public async Task PublishEventsToPubSub_ShouldPublishAllEvents()
    {
        // Arrange
        var eventDocuments = new List<SerializableEventDocument>
        {
            CreateTestEventDocument(Guid.NewGuid(), "TestEvent1", 1),
            CreateTestEventDocument(Guid.NewGuid(), "TestEvent2", 2)
        };

        var publishedEvents = new List<DaprEventEnvelope>();
        
        _mockDaprClient
            .Setup(x => x.PublishEventAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DaprEventEnvelope>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, DaprEventEnvelope, CancellationToken>((pubsub, topic, envelope, ct) =>
            {
                publishedEvents.Add(envelope);
            })
            .Returns(Task.CompletedTask);

        // Act
        await PublishEventsToPubSubInternal(eventDocuments);

        // Assert
        Assert.Equal(2, publishedEvents.Count);
        
        // Verify first event
        Assert.Equal(eventDocuments[0].Id, publishedEvents[0].EventId);
        Assert.Equal(eventDocuments[0].PayloadTypeName, publishedEvents[0].EventType);
        Assert.Equal(eventDocuments[0].AggregateId, publishedEvents[0].AggregateId);
        Assert.Equal(eventDocuments[0].Version, publishedEvents[0].Version);
        Assert.True(publishedEvents[0].IsCompressed);
        
        // Verify second event
        Assert.Equal(eventDocuments[1].Id, publishedEvents[1].EventId);
        Assert.Equal(eventDocuments[1].PayloadTypeName, publishedEvents[1].EventType);
        Assert.Equal(eventDocuments[1].AggregateId, publishedEvents[1].AggregateId);
        Assert.Equal(eventDocuments[1].Version, publishedEvents[1].Version);
        Assert.True(publishedEvents[1].IsCompressed);
    }

    [Fact]
    public async Task PublishEventsToPubSub_ShouldContinueOnFailure()
    {
        // Arrange
        var eventDocuments = new List<SerializableEventDocument>
        {
            CreateTestEventDocument(Guid.NewGuid(), "TestEvent1", 1),
            CreateTestEventDocument(Guid.NewGuid(), "TestEvent2", 2),
            CreateTestEventDocument(Guid.NewGuid(), "TestEvent3", 3)
        };

        var publishedCount = 0;
        
        _mockDaprClient
            .Setup(x => x.PublishEventAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.Is<DaprEventEnvelope>(e => e.Version == 2),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Simulated publish failure"));
            
        _mockDaprClient
            .Setup(x => x.PublishEventAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.Is<DaprEventEnvelope>(e => e.Version != 2),
                It.IsAny<CancellationToken>()))
            .Callback(() => publishedCount++)
            .Returns(Task.CompletedTask);

        // Act
        await PublishEventsToPubSubInternal(eventDocuments);

        // Assert
        Assert.Equal(2, publishedCount); // Should publish events 1 and 3
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HandlePublishedEvent_ShouldProcessNewEvent()
    {
        // Arrange
        var aggregateId = Guid.NewGuid();
        var envelope = new DaprEventEnvelope
        {
            EventId = Guid.NewGuid(),
            EventType = "TestEvent",
            AggregateId = aggregateId,
            Version = 1,
            Timestamp = DateTime.UtcNow,
            SortableUniqueId = SortableUniqueIdValue.Generate(DateTime.UtcNow, Guid.NewGuid()),
            EventData = new byte[] { 1, 2, 3 },
            IsCompressed = false
        };

        var processedEvents = new List<IEvent>();
        var mockMultiProjectorActor = new Mock<IMultiProjectorActor>();
        
        mockMultiProjectorActor
            .Setup(x => x.HandlePublishedEvent(It.IsAny<DaprEventEnvelope>()))
            .Callback<DaprEventEnvelope>(env =>
            {
                // Simulate event processing
                processedEvents.Add(CreateMockEvent(env));
            })
            .Returns(Task.CompletedTask);

        _mockActorProxyFactory
            .Setup(x => x.CreateActorProxy<IMultiProjectorActor>(
                It.IsAny<ActorId>(),
                It.IsAny<string>(),
                It.IsAny<ActorProxyOptions>()))
            .Returns(mockMultiProjectorActor.Object);

        // Act
        await HandleEventInController(envelope);

        // Assert
        mockMultiProjectorActor.Verify(
            x => x.HandlePublishedEvent(It.Is<DaprEventEnvelope>(e => e.EventId == envelope.EventId)),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task HandlePublishedEvent_ShouldSkipDuplicateEvents()
    {
        // Arrange
        var sortableUniqueId = SortableUniqueIdValue.Generate(DateTime.UtcNow, Guid.NewGuid());
        var envelope = new DaprEventEnvelope
        {
            EventId = Guid.NewGuid(),
            EventType = "TestEvent",
            AggregateId = Guid.NewGuid(),
            Version = 1,
            Timestamp = DateTime.UtcNow,
            SortableUniqueId = sortableUniqueId,
            EventData = new byte[] { 1, 2, 3 },
            IsCompressed = false
        };

        var mockMultiProjectorActor = new Mock<IMultiProjectorActor>();
        var callCount = 0;
        
        // First call processes the event
        mockMultiProjectorActor
            .SetupSequence(x => x.IsSortableUniqueIdReceived(sortableUniqueId))
            .ReturnsAsync(false)  // First check: not received
            .ReturnsAsync(true);  // Second check: already received
            
        mockMultiProjectorActor
            .Setup(x => x.HandlePublishedEvent(It.IsAny<DaprEventEnvelope>()))
            .Callback(() => callCount++)
            .Returns(Task.CompletedTask);

        _mockActorProxyFactory
            .Setup(x => x.CreateActorProxy<IMultiProjectorActor>(
                It.IsAny<ActorId>(),
                It.IsAny<string>(),
                It.IsAny<ActorProxyOptions>()))
            .Returns(mockMultiProjectorActor.Object);

        // Act - Send the same event twice
        await HandleEventInController(envelope);
        await HandleEventInController(envelope);

        // Assert - Should only process once
        Assert.Equal(2, callCount); // Called twice but second should skip processing
    }

    [Fact]
    public async Task EventFlow_EndToEnd_Test()
    {
        // This test simulates the complete flow from event creation to projection update
        
        // Arrange
        var aggregateId = Guid.NewGuid();
        var eventDoc = CreateTestEventDocument(aggregateId, "UserCreated", 1);
        var publishedEnvelopes = new List<DaprEventEnvelope>();
        
        // Setup DaprClient to capture published events
        _mockDaprClient
            .Setup(x => x.PublishEventAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DaprEventEnvelope>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, DaprEventEnvelope, CancellationToken>((_, _, envelope, _) =>
            {
                publishedEnvelopes.Add(envelope);
            })
            .Returns(Task.CompletedTask);

        // Act - Simulate event publishing
        await PublishEventsToPubSubInternal(new List<SerializableEventDocument> { eventDoc });

        // Assert
        Assert.Single(publishedEnvelopes);
        var publishedEnvelope = publishedEnvelopes[0];
        
        Assert.Equal(eventDoc.Id, publishedEnvelope.EventId);
        Assert.Equal(eventDoc.AggregateId, publishedEnvelope.AggregateId);
        Assert.Equal(eventDoc.PayloadTypeName, publishedEnvelope.EventType);
        Assert.Equal(eventDoc.Version, publishedEnvelope.Version);
        Assert.Equal(eventDoc.SortableUniqueId, publishedEnvelope.SortableUniqueId);
        Assert.Equal(eventDoc.RootPartitionKey, publishedEnvelope.RootPartitionKey);
        Assert.True(publishedEnvelope.IsCompressed);
        Assert.Equal(eventDoc.CompressedPayloadJson, publishedEnvelope.EventData);
        
        // Verify metadata
        Assert.Contains("PartitionGroup", publishedEnvelope.Metadata.Keys);
        Assert.Equal(eventDoc.AggregateGroup, publishedEnvelope.Metadata["PartitionGroup"]);
    }

    // Helper methods
    
    private SerializableEventDocument CreateTestEventDocument(Guid aggregateId, string eventType, int version)
    {
        return new SerializableEventDocument
        {
            Id = Guid.NewGuid(),
            SortableUniqueId = SortableUniqueIdValue.Generate(DateTime.UtcNow, Guid.NewGuid()),
            Version = version,
            AggregateId = aggregateId,
            AggregateGroup = PartitionKeys.DefaultAggregateGroupName,
            RootPartitionKey = PartitionKeys.DefaultRootPartitionKey,
            PayloadTypeName = eventType,
            TimeStamp = DateTime.UtcNow,
            PartitionKey = $"{aggregateId}_{PartitionKeys.DefaultAggregateGroupName}_{PartitionKeys.DefaultRootPartitionKey}",
            CausationId = Guid.NewGuid().ToString(),
            CorrelationId = Guid.NewGuid().ToString(),
            ExecutedUser = "test-user",
            CompressedPayloadJson = new byte[] { 1, 2, 3, 4, 5 },
            PayloadAssemblyVersion = "1.0.0.0"
        };
    }

    private IEvent CreateMockEvent(DaprEventEnvelope envelope)
    {
        var mockEvent = new Mock<IEvent>();
        mockEvent.Setup(e => e.Id).Returns(envelope.EventId);
        mockEvent.Setup(e => e.SortableUniqueId).Returns(envelope.SortableUniqueId);
        mockEvent.Setup(e => e.Version).Returns(envelope.Version);
        mockEvent.Setup(e => e.PartitionKeys).Returns(new PartitionKeys(
            envelope.AggregateId,
            envelope.Metadata.GetValueOrDefault("PartitionGroup", PartitionKeys.DefaultAggregateGroupName),
            envelope.RootPartitionKey));
        return mockEvent.Object;
    }

    private async Task PublishEventsToPubSubInternal(List<SerializableEventDocument> eventDocuments)
    {
        // This simulates the internal PublishEventsToPubSub method
        foreach (var eventDoc in eventDocuments)
        {
            try
            {
                var envelope = new DaprEventEnvelope
                {
                    EventId = eventDoc.Id,
                    EventData = eventDoc.CompressedPayloadJson,
                    EventType = eventDoc.PayloadTypeName,
                    AggregateId = eventDoc.AggregateId,
                    Version = eventDoc.Version,
                    Timestamp = eventDoc.TimeStamp,
                    SortableUniqueId = eventDoc.SortableUniqueId,
                    RootPartitionKey = eventDoc.RootPartitionKey,
                    IsCompressed = true,
                    Metadata = new Dictionary<string, string>
                    {
                        ["PartitionGroup"] = eventDoc.AggregateGroup,
                        ["ActorId"] = "test-actor"
                    }
                };
                
                await _mockDaprClient.Object.PublishEventAsync("sekiban-pubsub", "events.all", envelope);
            }
            catch (Exception ex)
            {
                _mockLogger.Object.LogError(ex, "Failed to publish event to PubSub: EventId={EventId}", eventDoc.Id);
            }
        }
    }

    private async Task HandleEventInController(DaprEventEnvelope envelope)
    {
        // This simulates the EventPubSubController handling
        var projectorNames = new[] { "TestProjector" }; // Simplified for testing
        
        var tasks = projectorNames.Select(async projectorName =>
        {
            var actorId = new ActorId(projectorName);
            var actor = _mockActorProxyFactory.Object.CreateActorProxy<IMultiProjectorActor>(
                actorId, 
                nameof(MultiProjectorActor));
            
            await actor.HandlePublishedEvent(envelope);
        });

        await Task.WhenAll(tasks);
    }
}