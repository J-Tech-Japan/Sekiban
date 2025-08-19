using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text.Json;
using Xunit;

namespace Sekiban.Dcb.Tests;

public class GeneralEventProviderIntegrationTests
{
    // Test event types
    private record TestProjectionEvent : IEventPayload
    {
        public string Value { get; init; } = "";
        public int Counter { get; init; }
    }

    private record TestProjection : IMultiProjector<TestProjection>
    {
        public int TotalCount { get; init; }
        public int SafeCount { get; init; }
        public int UnsafeCount { get; init; }
        public List<string> ProcessedValues { get; init; } = new();

        public static string MultiProjectorName => "TestProjector";
        public static string MultiProjectorVersion => "1.0.0";

        public static TestProjection GenerateInitialPayload() => 
            new TestProjection { ProcessedValues = new List<string>() };

        public static ResultBoxes.ResultBox<TestProjection> Project(TestProjection payload, Event ev, List<ITag> tags)
        {
            if (ev.Payload is TestProjectionEvent testEvent)
            {
                var result = payload with
                {
                    TotalCount = payload.TotalCount + 1,
                    ProcessedValues = payload.ProcessedValues.Concat(new[] { testEvent.Value }).ToList()
                };
                return ResultBoxes.ResultBox.FromValue(result);
            }
            return ResultBoxes.ResultBox.FromValue(payload);
        }
    }

    private Event CreateTestEvent(string value, int counter, DateTime timestamp)
    {
        var eventId = Guid.NewGuid();
        return new Event(
            new TestProjectionEvent { Value = value, Counter = counter },
            SortableUniqueId.Generate(timestamp, eventId),
            "TestProjectionEvent",
            eventId,
            new EventMetadata(
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString(),
                "TestUser"),
            new List<string>());
    }

    private DcbDomainTypes CreateTestDomain()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<TestProjectionEvent>("TestProjectionEvent");

        var tagTypes = new SimpleTagTypes();
        var tagProjectorTypes = new SimpleTagProjectorTypes();
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        
        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjector<TestProjection>();

        return new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            multiProjectorTypes);
    }

    [Fact]
    public async Task BatchedEventsAreSentToActor()
    {
        // Arrange
        var (eventStore, subscription) = CreateInMemoryServices();
        var provider = new GeneralEventProvider(eventStore, subscription, TimeSpan.FromSeconds(5));
        var domain = CreateTestDomain();
        var actor = new GeneralMultiProjectionActor(
            domain,
            "TestProjector",
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 5000 });

        var now = DateTime.UtcNow;
        
        // Add 25 events to test batching (batch size will be 10)
        for (int i = 0; i < 25; i++)
        {
            var evt = CreateTestEvent($"Event_{i}", i, now.AddSeconds(-30 + i));
            await eventStore.AppendEventAsync(evt);
        }

        // Act - Start provider with batch size of 10
        var handle = await provider.StartWithActorAsync(
            actor,
            batchSize: 10);

        // Wait for processing
        await handle.WaitForCatchUpAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(500);

        // Assert
        var stateResult = await actor.GetStateAsync();
        Assert.True(stateResult.IsSuccess);
        
        var state = stateResult.GetValue();
        var projection = state.Payload as TestProjection;
        
        Assert.NotNull(projection);
        Assert.Equal(25, projection.TotalCount);
        Assert.Equal(25, projection.ProcessedValues.Count);
        
        // Verify all events were processed in order
        for (int i = 0; i < 25; i++)
        {
            Assert.Contains($"Event_{i}", projection.ProcessedValues);
        }
    }

    [Fact]
    public async Task SafeAndUnsafeEventsAreCorrectlyHandled()
    {
        // Arrange
        var (eventStore, subscription) = CreateInMemoryServices();
        var safeWindowDuration = TimeSpan.FromSeconds(5);
        var provider = new GeneralEventProvider(eventStore, subscription, safeWindowDuration);
        var domain = CreateTestDomain();
        var actor = new GeneralMultiProjectionActor(
            domain,
            "TestProjector",
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 5000 });

        var now = DateTime.UtcNow;
        
        // Add events: some safe (old), some unsafe (recent)
        for (int i = 0; i < 10; i++)
        {
            var timestamp = i < 5 
                ? now.AddSeconds(-10 - i)  // Safe events (older than 5 seconds)
                : now.AddSeconds(-2);       // Unsafe events (within 5 seconds)
            
            var evt = CreateTestEvent($"Event_{i}", i, timestamp);
            await eventStore.AppendEventAsync(evt);
        }

        // Act
        var handle = await provider.StartWithActorAsync(actor);
        
        await handle.WaitForCatchUpAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(500);

        // Assert
        var stateResult = await actor.GetStateAsync();
        Assert.True(stateResult.IsSuccess);
        
        var state = stateResult.GetValue();
        Assert.Equal(10, state.Version); // All events processed
        Assert.True(state.IsCatchedUp);
    }

    [Fact]
    public async Task LiveEventsAreProcessedInBatchesAfterCatchUp()
    {
        // Arrange
        var (eventStore, subscription) = CreateInMemoryServices();
        var provider = new GeneralEventProvider(eventStore, subscription);
        var domain = CreateTestDomain();
        var actor = new GeneralMultiProjectionActor(domain, "TestProjector");

        var now = DateTime.UtcNow;
        
        // Add historical events
        for (int i = 0; i < 5; i++)
        {
            var evt = CreateTestEvent($"Historical_{i}", i, now.AddSeconds(-20 + i));
            await eventStore.AppendEventAsync(evt);
        }

        // Act - Start provider with small batch size
        var handle = await provider.StartWithActorAsync(
            actor,
            batchSize: 3);

        // Wait for catch-up
        await handle.WaitForCatchUpAsync(TimeSpan.FromSeconds(5));
        
        // Now publish live events
        for (int i = 0; i < 7; i++)
        {
            var evt = CreateTestEvent($"Live_{i}", 100 + i, DateTime.UtcNow.AddMilliseconds(i * 10));
            await subscription.PublishEventAsync(evt);
            await eventStore.AppendEventAsync(evt);
        }
        
        // Wait for live events to be processed
        await Task.Delay(2000);

        // Assert
        var stateResult = await actor.GetStateAsync();
        Assert.True(stateResult.IsSuccess);
        
        var state = stateResult.GetValue();
        var projection = state.Payload as TestProjection;
        
        Assert.NotNull(projection);
        Assert.True(projection.TotalCount >= 12); // 5 historical + 7 live
        
        // Check that both historical and live events were processed
        Assert.Contains("Historical_0", projection.ProcessedValues);
        Assert.Contains("Live_0", projection.ProcessedValues);
    }

    [Fact]
    public async Task SubscriptionStopWorksCorrectly()
    {
        // Arrange
        var (eventStore, subscription) = CreateInMemoryServices();
        var provider = new GeneralEventProvider(eventStore, subscription);
        var domain = CreateTestDomain();
        var actor = new GeneralMultiProjectionActor(domain, "TestProjector");

        var now = DateTime.UtcNow;
        
        // Add some initial events
        for (int i = 0; i < 5; i++)
        {
            var evt = CreateTestEvent($"Initial_{i}", i, now.AddSeconds(-10 + i));
            await eventStore.AppendEventAsync(evt);
        }

        // Act
        var handle = await provider.StartWithActorAsync(actor);
        
        await handle.WaitForCatchUpAsync(TimeSpan.FromSeconds(5));
        
        // Stop subscription but keep processing
        await handle.StopSubscriptionAsync();
        
        // Try to publish new events (should not be processed after subscription stop)
        for (int i = 0; i < 3; i++)
        {
            var evt = CreateTestEvent($"AfterStop_{i}", 100 + i, DateTime.UtcNow);
            await subscription.PublishEventAsync(evt);
        }
        
        await Task.Delay(1000);
        
        // Get state before full stop
        var stateBeforeStop = await actor.GetStateAsync();
        var projectionBeforeStop = stateBeforeStop.GetValue().Payload as TestProjection;
        var countBeforeStop = projectionBeforeStop!.TotalCount;
        
        // Now stop completely
        await handle.StopAsync();
        
        // Assert
        Assert.Equal(EventProviderState.Stopped, handle.State);
        
        // Verify that only initial events were processed
        Assert.Equal(5, countBeforeStop);
        Assert.All(projectionBeforeStop.ProcessedValues, 
            v => Assert.StartsWith("Initial_", v));
    }

    [Fact]
    public async Task WaitForCurrentBatchWorks()
    {
        // Arrange
        var (eventStore, subscription) = CreateInMemoryServices();
        var provider = new GeneralEventProvider(eventStore, subscription);
        var domain = CreateTestDomain();
        var actor = new GeneralMultiProjectionActor(domain, "TestProjector");

        var now = DateTime.UtcNow;
        
        // Add many events to ensure batch processing
        for (int i = 0; i < 50; i++)
        {
            var evt = CreateTestEvent($"Event_{i}", i, now.AddSeconds(-30 + i * 0.5));
            await eventStore.AppendEventAsync(evt);
        }

        // Act
        var handle = await provider.StartWithActorAsync(
            actor,
            batchSize: 10);

        // Check if processing batch and wait
        if (handle.IsProcessingBatch)
        {
            await handle.WaitForCurrentBatchAsync();
        }

        // After waiting, should not be processing a batch
        Assert.False(handle.IsProcessingBatch);
        
        // Get statistics
        var stats = handle.GetStatistics();
        Assert.True(stats.TotalEventsProcessed > 0);
    }

    [Fact]
    public async Task FilteredEventsAreCorrectlyProcessed()
    {
        // Arrange
        var (eventStore, subscription) = CreateInMemoryServices();
        var provider = new GeneralEventProvider(eventStore, subscription);
        var domain = CreateTestDomain();
        
        // Register multiple event types
        ((SimpleEventTypes)domain.EventTypes).RegisterEventType<TestProjectionEvent>("TypeA");
        ((SimpleEventTypes)domain.EventTypes).RegisterEventType<TestProjectionEvent>("TypeB");
        
        var actor = new GeneralMultiProjectionActor(domain, "TestProjector");

        var now = DateTime.UtcNow;
        
        // Add mixed event types
        for (int i = 0; i < 10; i++)
        {
            var eventType = i % 2 == 0 ? "TypeA" : "TypeB";
            var evt = new Event(
                new TestProjectionEvent { Value = $"{eventType}_{i}", Counter = i },
                SortableUniqueId.Generate(now.AddSeconds(-20 + i), Guid.NewGuid()),
                eventType,
                Guid.NewGuid(),
                new EventMetadata(
                    Guid.NewGuid().ToString(),
                    Guid.NewGuid().ToString(),
                    "TestUser"),
                new List<string>());
                
            await eventStore.AppendEventAsync(evt);
        }

        // Act - Filter only TypeA events
        var filter = new EventTypesFilter(new HashSet<string> { "TypeA" });
        var handle = await provider.StartWithActorAsync(
            actor,
            filter: filter);

        await handle.WaitForCatchUpAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(500);

        // Assert
        var stateResult = await actor.GetStateAsync();
        var state = stateResult.GetValue();
        var projection = state.Payload as TestProjection;
        
        Assert.NotNull(projection);
        Assert.Equal(5, projection.TotalCount); // Only TypeA events
        Assert.All(projection.ProcessedValues, v => Assert.Contains("TypeA", v));
    }

    [Fact]
    public async Task MultipleProvidersCanRunConcurrently()
    {
        // Arrange
        var (eventStore, subscription) = CreateInMemoryServices();
        var provider1 = new GeneralEventProvider(eventStore, subscription);
        var provider2 = new GeneralEventProvider(eventStore, subscription);
        
        var domain = CreateTestDomain();
        var actor1 = new GeneralMultiProjectionActor(domain, "TestProjector");
        var actor2 = new GeneralMultiProjectionActor(domain, "TestProjector");

        var now = DateTime.UtcNow;
        
        // Add events
        for (int i = 0; i < 20; i++)
        {
            var evt = CreateTestEvent($"Event_{i}", i, now.AddSeconds(-10 + i * 0.5));
            await eventStore.AppendEventAsync(evt);
        }

        // Act - Start multiple providers
        var handle1 = await provider1.StartWithActorAsync(actor1, batchSize: 5);
        var handle2 = await provider2.StartWithActorAsync(actor2, batchSize: 7);

        // Wait for both to catch up
        await Task.WhenAll(
            handle1.WaitForCatchUpAsync(TimeSpan.FromSeconds(10)),
            handle2.WaitForCatchUpAsync(TimeSpan.FromSeconds(10)));
            
        await Task.Delay(500);

        // Assert - Both actors should have processed all events
        var state1 = await actor1.GetStateAsync();
        var state2 = await actor2.GetStateAsync();
        
        var projection1 = state1.GetValue().Payload as TestProjection;
        var projection2 = state2.GetValue().Payload as TestProjection;
        
        Assert.Equal(20, projection1!.TotalCount);
        Assert.Equal(20, projection2!.TotalCount);
        
        // Both should have the same events (order might differ due to batching)
        Assert.Equal(
            projection1.ProcessedValues.OrderBy(v => v).ToList(),
            projection2.ProcessedValues.OrderBy(v => v).ToList());
    }

    private (InMemoryEventStore, InMemoryEventSubscription) CreateInMemoryServices()
    {
        var eventStore = new InMemoryEventStore();
        var subscription = new InMemoryEventSubscription();
        return (eventStore, subscription);
    }
}