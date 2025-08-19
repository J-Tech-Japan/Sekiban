using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Tests;

public class GeneralEventProviderTests
{
    private record TestEvent : IEventPayload
    {
        public string Data { get; init; } = "";
    }

    private record TestTag : ITag
    {
        public string Group { get; init; } = "";
        public string Content { get; init; } = "";
        
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => Group;
        public string GetTagContent() => Content;
    }

    private Event CreateTestEvent(
        string eventType, 
        DateTime timestamp,
        string? data = null)
    {
        var eventId = Guid.NewGuid();
        return new Event(
            new TestEvent { Data = data ?? $"Event_{eventType}" },
            SortableUniqueId.Generate(timestamp, eventId),
            eventType,
            eventId,
            new EventMetadata(
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString(),
                "TestUser"),
            new List<string>());
    }

    private (InMemoryEventStore, InMemoryEventSubscription) CreateInMemoryServices()
    {
        var eventStore = new InMemoryEventStore();
        var subscription = new InMemoryEventSubscription();
        return (eventStore, subscription);
    }

    [Fact]
    public async Task EventsAreProvidedInOrder()
    {
        // Arrange
        var (eventStore, subscription) = CreateInMemoryServices();
        var provider = new GeneralEventProvider(eventStore, subscription, TimeSpan.FromSeconds(5));
        
        var now = DateTime.UtcNow;
        var event1 = CreateTestEvent("Type1", now.AddSeconds(-10));
        var event2 = CreateTestEvent("Type2", now.AddSeconds(-8));
        var event3 = CreateTestEvent("Type3", now.AddSeconds(-6));
        
        // Add events to store
        await eventStore.AppendEventAsync(event1);
        await eventStore.AppendEventAsync(event2);
        await eventStore.AppendEventAsync(event3);
        
        var receivedEvents = new List<(Event evt, bool isSafe)>();
        
        // Act
        var handle = await provider.StartAsync(
            async (evt, isSafe) =>
            {
                receivedEvents.Add((evt, isSafe));
                await Task.CompletedTask;
            });
        
        // Wait for catch-up
        await handle.WaitForCatchUpAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);
        
        // Assert
        Assert.Equal(3, receivedEvents.Count);
        Assert.Equal(event1.Id, receivedEvents[0].evt.Id);
        Assert.Equal(event2.Id, receivedEvents[1].evt.Id);
        Assert.Equal(event3.Id, receivedEvents[2].evt.Id);
        
        // Verify order by timestamp
        for (int i = 1; i < receivedEvents.Count; i++)
        {
            Assert.True(string.Compare(
                receivedEvents[i - 1].evt.SortableUniqueIdValue,
                receivedEvents[i].evt.SortableUniqueIdValue,
                StringComparison.Ordinal) < 0);
        }
    }

    [Fact]
    public async Task SafeAndUnsafeEventsAreCorrectlyIdentified()
    {
        // Arrange
        var (eventStore, subscription) = CreateInMemoryServices();
        var safeWindowDuration = TimeSpan.FromSeconds(5);
        var provider = new GeneralEventProvider(eventStore, subscription, safeWindowDuration);
        
        var now = DateTime.UtcNow;
        var safeEvent1 = CreateTestEvent("Safe1", now.AddSeconds(-10)); // Safe
        var safeEvent2 = CreateTestEvent("Safe2", now.AddSeconds(-6));  // Safe
        var unsafeEvent = CreateTestEvent("Unsafe", now.AddSeconds(-2)); // Unsafe (within 5 seconds)
        
        await eventStore.AppendEventAsync(safeEvent1);
        await eventStore.AppendEventAsync(safeEvent2);
        await eventStore.AppendEventAsync(unsafeEvent);
        
        var receivedEvents = new List<(Event evt, bool isSafe)>();
        
        // Act
        var handle = await provider.StartAsync(
            async (evt, isSafe) =>
            {
                receivedEvents.Add((evt, isSafe));
                await Task.CompletedTask;
            });
        
        await handle.WaitForCatchUpAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);
        
        // Assert
        Assert.Equal(3, receivedEvents.Count);
        
        // First two events should be safe
        Assert.True(receivedEvents[0].isSafe);
        Assert.True(receivedEvents[1].isSafe);
        
        // Last event should be unsafe
        Assert.False(receivedEvents[2].isSafe);
    }

    [Fact]
    public async Task StartFromSpecificPosition()
    {
        // Arrange
        var (eventStore, subscription) = CreateInMemoryServices();
        var provider = new GeneralEventProvider(eventStore, subscription);
        
        var now = DateTime.UtcNow;
        var event1 = CreateTestEvent("Type1", now.AddSeconds(-10));
        var event2 = CreateTestEvent("Type2", now.AddSeconds(-8));
        var event3 = CreateTestEvent("Type3", now.AddSeconds(-6));
        
        await eventStore.AppendEventAsync(event1);
        await eventStore.AppendEventAsync(event2);
        await eventStore.AppendEventAsync(event3);
        
        var receivedEvents = new List<Event>();
        
        // Act - Start from after event1
        var startPosition = new SortableUniqueId(event1.SortableUniqueIdValue);
        var handle = await provider.StartAsync(
            async (evt, isSafe) =>
            {
                receivedEvents.Add(evt);
                await Task.CompletedTask;
            },
            fromPosition: startPosition);
        
        await handle.WaitForCatchUpAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);
        
        // Assert - Should only receive event2 and event3
        Assert.Equal(2, receivedEvents.Count);
        Assert.Equal(event2.Id, receivedEvents[0].Id);
        Assert.Equal(event3.Id, receivedEvents[1].Id);
    }

    [Fact]
    public async Task LiveEventsReceivedAfterCatchUp()
    {
        // Arrange
        var (eventStore, subscription) = CreateInMemoryServices();
        var provider = new GeneralEventProvider(eventStore, subscription);
        
        var now = DateTime.UtcNow;
        var historicalEvent = CreateTestEvent("Historical", now.AddSeconds(-10));
        
        await eventStore.AppendEventAsync(historicalEvent);
        
        var receivedEvents = new List<(Event evt, bool isSafe, string phase)>();
        var caughtUp = false;
        
        // Act
        var handle = await provider.StartAsync(
            async (evt, isSafe) =>
            {
                var phase = caughtUp ? "Live" : "CatchUp";
                receivedEvents.Add((evt, isSafe, phase));
                await Task.CompletedTask;
            });
        
        // Wait for catch-up
        await handle.WaitForCatchUpAsync(TimeSpan.FromSeconds(5));
        caughtUp = true;
        
        // Publish live events
        var liveEvent1 = CreateTestEvent("Live1", DateTime.UtcNow);
        var liveEvent2 = CreateTestEvent("Live2", DateTime.UtcNow.AddMilliseconds(100));
        
        await subscription.PublishEventAsync(liveEvent1);
        await subscription.PublishEventAsync(liveEvent2);
        await eventStore.AppendEventAsync(liveEvent1);
        await eventStore.AppendEventAsync(liveEvent2);
        
        await Task.Delay(200);
        
        // Assert
        Assert.True(receivedEvents.Count >= 3); // Historical + 2 live
        
        // First event should be from catch-up phase
        Assert.Equal("CatchUp", receivedEvents[0].phase);
        Assert.Equal(historicalEvent.Id, receivedEvents[0].evt.Id);
        
        // Last events should be from live phase and unsafe
        var liveEvents = receivedEvents.Where(e => e.phase == "Live").ToList();
        Assert.True(liveEvents.Count >= 2);
        Assert.All(liveEvents, e => Assert.False(e.isSafe)); // Live events are always unsafe
    }

    [Fact]
    public async Task NoDuplicatesWhenTransitioningToLive()
    {
        // Arrange
        var (eventStore, subscription) = CreateInMemoryServices();
        var provider = new GeneralEventProvider(eventStore, subscription);
        
        var now = DateTime.UtcNow;
        var event1 = CreateTestEvent("Event1", now.AddSeconds(-10));
        var event2 = CreateTestEvent("Event2", now.AddSeconds(-5));
        
        await eventStore.AppendEventAsync(event1);
        await eventStore.AppendEventAsync(event2);
        
        var receivedEventIds = new HashSet<Guid>();
        var duplicateFound = false;
        
        // Act
        var handle = await provider.StartAsync(
            async (evt, isSafe) =>
            {
                if (!receivedEventIds.Add(evt.Id))
                {
                    duplicateFound = true;
                }
                await Task.CompletedTask;
            });
        
        // Publish same events via subscription during catch-up
        await subscription.PublishEventAsync(event2);
        
        await handle.WaitForCatchUpAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);
        
        // Assert
        Assert.False(duplicateFound, "Duplicate events were received");
        Assert.Contains(event1.Id, receivedEventIds);
        Assert.Contains(event2.Id, receivedEventIds);
    }

    [Fact]
    public async Task EventTypeFilterWorksCorrectly()
    {
        // Arrange
        var (eventStore, subscription) = CreateInMemoryServices();
        var provider = new GeneralEventProvider(eventStore, subscription);
        
        var now = DateTime.UtcNow;
        var event1 = CreateTestEvent("TypeA", now.AddSeconds(-10));
        var event2 = CreateTestEvent("TypeB", now.AddSeconds(-8));
        var event3 = CreateTestEvent("TypeA", now.AddSeconds(-6));
        var event4 = CreateTestEvent("TypeC", now.AddSeconds(-4));
        
        await eventStore.AppendEventAsync(event1);
        await eventStore.AppendEventAsync(event2);
        await eventStore.AppendEventAsync(event3);
        await eventStore.AppendEventAsync(event4);
        
        var receivedEvents = new List<Event>();
        var filter = new EventTypesFilter(new HashSet<string> { "TypeA", "TypeC" });
        
        // Act
        var handle = await provider.StartAsync(
            async (evt, isSafe) =>
            {
                receivedEvents.Add(evt);
                await Task.CompletedTask;
            },
            filter: filter);
        
        await handle.WaitForCatchUpAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);
        
        // Assert
        Assert.Equal(3, receivedEvents.Count);
        Assert.Contains(event1.Id, receivedEvents.Select(e => e.Id));
        Assert.Contains(event3.Id, receivedEvents.Select(e => e.Id));
        Assert.Contains(event4.Id, receivedEvents.Select(e => e.Id));
        Assert.DoesNotContain(event2.Id, receivedEvents.Select(e => e.Id)); // TypeB filtered out
    }

    [Fact]
    public async Task PauseAndResumeWorksCorrectly()
    {
        // Arrange
        var (eventStore, subscription) = CreateInMemoryServices();
        var provider = new GeneralEventProvider(eventStore, subscription);
        
        var now = DateTime.UtcNow;
        var event1 = CreateTestEvent("Event1", now.AddSeconds(-10));
        var event2 = CreateTestEvent("Event2", now.AddSeconds(-8));
        var event3 = CreateTestEvent("Event3", now.AddSeconds(-6));
        
        await eventStore.AppendEventAsync(event1);
        await eventStore.AppendEventAsync(event2);
        await eventStore.AppendEventAsync(event3);
        
        var receivedEvents = new List<Event>();
        var processedBeforePause = 0;
        IEventProviderHandle? handleRef = null;
        
        // Act
        var handle = await provider.StartAsync(
            async (evt, isSafe) =>
            {
                receivedEvents.Add(evt);
                
                // Pause after first event
                if (receivedEvents.Count == 1 && processedBeforePause == 0 && handleRef != null)
                {
                    processedBeforePause = receivedEvents.Count;
                    await handleRef.PauseAsync();
                }
                
                await Task.CompletedTask;
            });
        
        handleRef = handle;
        
        await Task.Delay(500); // Give time to process first event and pause
        
        var countAfterPause = receivedEvents.Count;
        Assert.Equal(1, countAfterPause); // Should have processed only 1 event
        
        // Resume
        await handle.ResumeAsync();
        await handle.WaitForCatchUpAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);
        
        // Assert
        Assert.Equal(3, receivedEvents.Count); // All events should be processed after resume
        Assert.Equal(event1.Id, receivedEvents[0].Id);
        Assert.Equal(event2.Id, receivedEvents[1].Id);
        Assert.Equal(event3.Id, receivedEvents[2].Id);
    }

    [Fact]
    public async Task StatisticsAreTrackedCorrectly()
    {
        // Arrange
        var (eventStore, subscription) = CreateInMemoryServices();
        var provider = new GeneralEventProvider(eventStore, subscription, TimeSpan.FromSeconds(5));
        
        var now = DateTime.UtcNow;
        var safeEvent1 = CreateTestEvent("Safe1", now.AddSeconds(-10));
        var safeEvent2 = CreateTestEvent("Safe2", now.AddSeconds(-8));
        var unsafeEvent = CreateTestEvent("Unsafe", now.AddSeconds(-2));
        
        await eventStore.AppendEventAsync(safeEvent1);
        await eventStore.AppendEventAsync(safeEvent2);
        await eventStore.AppendEventAsync(unsafeEvent);
        
        // Act
        var handle = await provider.StartAsync(
            async (evt, isSafe) => await Task.CompletedTask);
        
        await handle.WaitForCatchUpAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);
        
        var stats = handle.GetStatistics();
        
        // Assert
        Assert.Equal(3, stats.TotalEventsProcessed);
        Assert.Equal(2, stats.SafeEventsProcessed);
        Assert.Equal(1, stats.UnsafeEventsProcessed);
        Assert.NotNull(stats.LastEventTime);
        Assert.NotNull(stats.LastEventPosition);
        Assert.NotNull(stats.CatchUpDuration);
        Assert.True(stats.CatchUpDuration.Value.TotalMilliseconds > 0);
    }

    [Fact]
    public async Task StopCleansUpResources()
    {
        // Arrange
        var (eventStore, subscription) = CreateInMemoryServices();
        var provider = new GeneralEventProvider(eventStore, subscription);
        
        var now = DateTime.UtcNow;
        var event1 = CreateTestEvent("Event1", now.AddSeconds(-10));
        await eventStore.AppendEventAsync(event1);
        
        var receivedCount = 0;
        
        // Act
        var handle = await provider.StartAsync(
            async (evt, isSafe) =>
            {
                receivedCount++;
                await Task.CompletedTask;
            });
        
        await Task.Delay(200);
        await handle.StopAsync();
        
        // Try to publish more events after stop
        var event2 = CreateTestEvent("Event2", DateTime.UtcNow);
        await subscription.PublishEventAsync(event2);
        await Task.Delay(200);
        
        // Assert
        Assert.Equal(1, receivedCount); // Only the first event should be processed
        Assert.Equal(EventProviderState.Stopped, handle.State);
    }
}