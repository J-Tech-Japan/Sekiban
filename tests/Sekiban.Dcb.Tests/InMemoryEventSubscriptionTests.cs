using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
namespace Sekiban.Dcb.Tests;

public class InMemoryEventSubscriptionTests
{
    private Event CreateTestEvent(Guid id, string eventType, DateTime? timestamp = null)
    {
        var sortableId = SortableUniqueId.Generate(timestamp ?? DateTime.UtcNow, id);
        return new Event(
            new TestEventPayload { Data = "test" },
            sortableId,
            eventType,
            id,
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "TestUser"),
            new List<string>());
    }

    [Fact]
    public async Task Subscribe_ReceivesPublishedEvents()
    {
        // Arrange
        using var subscription = new InMemoryEventSubscription();
        var receivedEvents = new List<Event>();
        var handle = await subscription.SubscribeAsync(async evt =>
        {
            receivedEvents.Add(evt);
            await Task.CompletedTask;
        });

        // Act
        var event1 = CreateTestEvent(Guid.NewGuid(), "TestEvent1");
        var event2 = CreateTestEvent(Guid.NewGuid(), "TestEvent2");

        await subscription.PublishEventAsync(event1);
        await subscription.PublishEventAsync(event2);

        // Small delay to ensure async processing completes
        await Task.Delay(100);

        // Assert
        Assert.Equal(2, receivedEvents.Count);
        Assert.Contains(event1, receivedEvents);
        Assert.Contains(event2, receivedEvents);
    }

    [Fact]
    public async Task SubscribeFrom_ReceivesOnlyEventsAfterPosition()
    {
        // Arrange
        using var subscription = new InMemoryEventSubscription();

        // Publish some events first
        var event1 = CreateTestEvent(Guid.NewGuid(), "TestEvent1", DateTime.UtcNow.AddSeconds(-3));
        var event2 = CreateTestEvent(Guid.NewGuid(), "TestEvent2", DateTime.UtcNow.AddSeconds(-2));
        var event3 = CreateTestEvent(Guid.NewGuid(), "TestEvent3", DateTime.UtcNow.AddSeconds(-1));

        await subscription.PublishEventAsync(event1);
        await subscription.PublishEventAsync(event2);
        await subscription.PublishEventAsync(event3);

        // Subscribe from position after event1
        var receivedEvents = new List<Event>();
        var handle = await subscription.SubscribeFromAsync(
            event1.SortableUniqueIdValue,
            async evt =>
            {
                receivedEvents.Add(evt);
                await Task.CompletedTask;
            });

        // Act - Publish new event
        var event4 = CreateTestEvent(Guid.NewGuid(), "TestEvent4");
        await subscription.PublishEventAsync(event4);

        await Task.Delay(100);

        // Assert - Should receive event2, event3 (historical) and event4 (new)
        Assert.Equal(3, receivedEvents.Count);
        Assert.DoesNotContain(event1, receivedEvents);
        Assert.Contains(event2, receivedEvents);
        Assert.Contains(event3, receivedEvents);
        Assert.Contains(event4, receivedEvents);
    }

    [Fact]
    public async Task SubscribeWithFilter_OnlyReceivesMatchingEvents()
    {
        // Arrange
        using var subscription = new InMemoryEventSubscription();
        var filter = new EventTypeFilter(new HashSet<string> { "TypeA", "TypeC" });

        var receivedEvents = new List<Event>();
        var handle = await subscription.SubscribeWithFilterAsync(
            filter,
            async evt =>
            {
                receivedEvents.Add(evt);
                await Task.CompletedTask;
            });

        // Act
        var eventA = CreateTestEvent(Guid.NewGuid(), "TypeA");
        var eventB = CreateTestEvent(Guid.NewGuid(), "TypeB");
        var eventC = CreateTestEvent(Guid.NewGuid(), "TypeC");
        var eventD = CreateTestEvent(Guid.NewGuid(), "TypeD");

        await subscription.PublishEventAsync(eventA);
        await subscription.PublishEventAsync(eventB);
        await subscription.PublishEventAsync(eventC);
        await subscription.PublishEventAsync(eventD);

        await Task.Delay(100);

        // Assert
        Assert.Equal(2, receivedEvents.Count);
        Assert.Contains(eventA, receivedEvents);
        Assert.Contains(eventC, receivedEvents);
        Assert.DoesNotContain(eventB, receivedEvents);
        Assert.DoesNotContain(eventD, receivedEvents);
    }

    [Fact]
    public async Task Pause_StopsReceivingEvents()
    {
        // Arrange
        using var subscription = new InMemoryEventSubscription();
        var receivedEvents = new List<Event>();
        var handle = await subscription.SubscribeAsync(async evt =>
        {
            receivedEvents.Add(evt);
            await Task.CompletedTask;
        });

        // Act
        var event1 = CreateTestEvent(Guid.NewGuid(), "Event1");
        await subscription.PublishEventAsync(event1);
        await Task.Delay(100);

        await handle.PauseAsync();

        var event2 = CreateTestEvent(Guid.NewGuid(), "Event2");
        await subscription.PublishEventAsync(event2);
        await Task.Delay(100);

        // Assert
        Assert.Single(receivedEvents);
        Assert.Contains(event1, receivedEvents);
        Assert.DoesNotContain(event2, receivedEvents);
    }

    [Fact]
    public async Task Resume_StartsReceivingEventsAgain()
    {
        // Arrange
        using var subscription = new InMemoryEventSubscription();
        var receivedEvents = new List<Event>();
        var handle = await subscription.SubscribeAsync(async evt =>
        {
            receivedEvents.Add(evt);
            await Task.CompletedTask;
        });

        // Act
        await handle.PauseAsync();

        var event1 = CreateTestEvent(Guid.NewGuid(), "Event1");
        await subscription.PublishEventAsync(event1);
        await Task.Delay(100);

        await handle.ResumeAsync();

        var event2 = CreateTestEvent(Guid.NewGuid(), "Event2");
        await subscription.PublishEventAsync(event2);
        await Task.Delay(100);

        // Assert
        Assert.Single(receivedEvents);
        Assert.DoesNotContain(event1, receivedEvents);
        Assert.Contains(event2, receivedEvents);
    }

    [Fact]
    public async Task Unsubscribe_StopsReceivingEvents()
    {
        // Arrange
        using var subscription = new InMemoryEventSubscription();
        var receivedEvents = new List<Event>();
        var handle = await subscription.SubscribeAsync(async evt =>
        {
            receivedEvents.Add(evt);
            await Task.CompletedTask;
        });

        // Act
        var event1 = CreateTestEvent(Guid.NewGuid(), "Event1");
        await subscription.PublishEventAsync(event1);
        await Task.Delay(100);

        await handle.UnsubscribeAsync();

        var event2 = CreateTestEvent(Guid.NewGuid(), "Event2");
        await subscription.PublishEventAsync(event2);
        await Task.Delay(100);

        // Assert
        Assert.Single(receivedEvents);
        Assert.Contains(event1, receivedEvents);
        Assert.DoesNotContain(event2, receivedEvents);
        Assert.False(handle.IsActive);
    }

    [Fact]
    public async Task MultipleSubscriptions_AllReceiveEvents()
    {
        // Arrange
        using var subscription = new InMemoryEventSubscription();
        var receivedEvents1 = new List<Event>();
        var receivedEvents2 = new List<Event>();

        var handle1 = await subscription.SubscribeAsync(async evt =>
        {
            receivedEvents1.Add(evt);
            await Task.CompletedTask;
        });

        var handle2 = await subscription.SubscribeAsync(async evt =>
        {
            receivedEvents2.Add(evt);
            await Task.CompletedTask;
        });

        // Act
        var event1 = CreateTestEvent(Guid.NewGuid(), "Event1");
        await subscription.PublishEventAsync(event1);
        await Task.Delay(100);

        // Assert
        Assert.Single(receivedEvents1);
        Assert.Single(receivedEvents2);
        Assert.Contains(event1, receivedEvents1);
        Assert.Contains(event1, receivedEvents2);
    }

    [Fact]
    public async Task CurrentPosition_UpdatesAsEventsAreProcessed()
    {
        // Arrange
        using var subscription = new InMemoryEventSubscription();
        var handle = await subscription.SubscribeAsync(async evt => await Task.CompletedTask);

        // Act & Assert
        Assert.Null(handle.CurrentPosition);

        var event1 = CreateTestEvent(Guid.NewGuid(), "Event1");
        await subscription.PublishEventAsync(event1);
        await Task.Delay(100);

        Assert.Equal(event1.SortableUniqueIdValue, handle.CurrentPosition);

        var event2 = CreateTestEvent(Guid.NewGuid(), "Event2");
        await subscription.PublishEventAsync(event2);
        await Task.Delay(100);

        Assert.Equal(event2.SortableUniqueIdValue, handle.CurrentPosition);
    }

    [Fact]
    public async Task CompositeFilter_WorksCorrectly()
    {
        // Arrange
        using var subscription = new InMemoryEventSubscription();

        var typeFilter = new EventTypeFilter(new HashSet<string> { "TypeA", "TypeB" });
        var tagFilter = new EventTagFilter(new HashSet<string> { "important" });
        var compositeFilter = new CompositeEventFilter(new List<IEventFilter> { typeFilter, tagFilter });

        var receivedEvents = new List<Event>();
        var handle = await subscription.SubscribeWithFilterAsync(
            compositeFilter,
            async evt =>
            {
                receivedEvents.Add(evt);
                await Task.CompletedTask;
            });

        // Act - Create events with different combinations
        var eventId1 = Guid.NewGuid();
        var event1 = new Event(
            new TestEventPayload { Data = "test" },
            SortableUniqueId.Generate(DateTime.UtcNow, eventId1),
            "TypeA",
            eventId1,
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "TestUser"),
            new List<string> { "important" }); // TypeA + important tag

        var eventId2 = Guid.NewGuid();
        var event2 = new Event(
            new TestEventPayload { Data = "test" },
            SortableUniqueId.Generate(DateTime.UtcNow, eventId2),
            "TypeA",
            eventId2,
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "TestUser"),
            new List<string>()); // TypeA but no important tag

        var eventId3 = Guid.NewGuid();
        var event3 = new Event(
            new TestEventPayload { Data = "test" },
            SortableUniqueId.Generate(DateTime.UtcNow, eventId3),
            "TypeC",
            eventId3,
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "TestUser"),
            new List<string> { "important" }); // Wrong type but has important tag

        await subscription.PublishEventAsync(event1);
        await subscription.PublishEventAsync(event2);
        await subscription.PublishEventAsync(event3);
        await Task.Delay(100);

        // Assert - Only event1 should match both filters
        Assert.Single(receivedEvents);
        Assert.Contains(event1, receivedEvents);
    }

    private record TestEventPayload : IEventPayload
    {
        public string Data { get; init; } = "";
    }
}
