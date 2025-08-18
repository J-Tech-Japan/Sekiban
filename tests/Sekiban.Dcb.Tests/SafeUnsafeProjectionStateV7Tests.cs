using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Xunit;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for SafeUnsafeProjectionStateV7 to verify duplicate event handling and out-of-order processing
/// </summary>
public class SafeUnsafeProjectionStateV7Tests
{
    /// <summary>
    ///     Test item for projection
    /// </summary>
    public record TestItem(Guid Id, string Name, int UpdateCount);

    /// <summary>
    ///     Test event
    /// </summary>
    public record TestEvent(Guid ItemId, string NewName) : IEventPayload;

    /// <summary>
    ///     Create an event with specific timestamp and ID
    /// </summary>
    private static Event CreateEvent(Guid eventId, Guid itemId, string name, DateTime timestamp)
    {
        var sortableId = SortableUniqueId.Generate(timestamp, eventId);
        return new Event(
            Payload: new TestEvent(itemId, name),
            SortableUniqueIdValue: sortableId,
            EventType: "TestEvent",
            Id: eventId,
            EventMetadata: new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "TestUser"),
            Tags: new List<string>()
        );
    }

    [Fact]
    public void DuplicateEvents_ShouldBeIgnored_WithoutError()
    {
        // Arrange
        var state = new SafeUnsafeProjectionStateV7<Guid, TestItem>();
        var itemId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        
        // Create safe window threshold (events older than 5 seconds are safe)
        var threshold = SortableUniqueId.Generate(now.AddSeconds(-5), Guid.Empty);
        state = state with { SafeWindowThreshold = threshold };

        // Create an unsafe event (within 5 seconds)
        var unsafeEvent = CreateEvent(eventId, itemId, "Update 1", now.AddSeconds(-3));
        
        // Functions for projection
        Func<Event, IEnumerable<Guid>> getAffectedIds = evt =>
        {
            var payload = evt.Payload as TestEvent;
            return new[] { payload!.ItemId };
        };
        
        Func<Guid, TestItem?, Event, TestItem?> projectItem = (id, current, evt) =>
        {
            var payload = evt.Payload as TestEvent;
            if (current == null)
            {
                return new TestItem(id, payload!.NewName, 1);
            }
            return current with 
            { 
                Name = payload!.NewName, 
                UpdateCount = current.UpdateCount + 1 
            };
        };

        // Act - Process the same event multiple times
        state = state.ProcessEvent(unsafeEvent, getAffectedIds, projectItem);
        var firstState = state.GetCurrentState();
        
        state = state.ProcessEvent(unsafeEvent, getAffectedIds, projectItem);
        var secondState = state.GetCurrentState();
        
        state = state.ProcessEvent(unsafeEvent, getAffectedIds, projectItem);
        var thirdState = state.GetCurrentState();

        // Assert - The item should only be updated once (duplicates are ignored)
        Assert.Single(firstState);
        Assert.Single(secondState);
        Assert.Single(thirdState);
        
        var firstItem = firstState[itemId];
        var secondItem = secondState[itemId];
        var thirdItem = thirdState[itemId];
        
        // Duplicates ARE now ignored - processed only once
        Assert.Equal(1, firstItem.UpdateCount);
        Assert.Equal(1, secondItem.UpdateCount); // Still 1, duplicate ignored
        Assert.Equal(1, thirdItem.UpdateCount); // Still 1, duplicate ignored
        
        // All states should be identical since duplicates are ignored
        Assert.Equal(firstItem.Name, secondItem.Name);
        Assert.Equal(secondItem.Name, thirdItem.Name);
    }

    [Fact]
    public void OutOfOrderEvents_InUnsafePeriod_ShouldBeProcessedInCorrectOrder()
    {
        // Arrange
        var state = new SafeUnsafeProjectionStateV7<Guid, TestItem>();
        var itemId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        
        // Create safe window threshold (events older than 10 seconds are safe)
        var threshold = SortableUniqueId.Generate(now.AddSeconds(-10), Guid.Empty);
        state = state with { SafeWindowThreshold = threshold };

        // Create unsafe events (within 10 seconds) with different timestamps
        var event1 = CreateEvent(Guid.NewGuid(), itemId, "First", now.AddSeconds(-8));
        var event2 = CreateEvent(Guid.NewGuid(), itemId, "Second", now.AddSeconds(-6));
        var event3 = CreateEvent(Guid.NewGuid(), itemId, "Third", now.AddSeconds(-4));
        var event4 = CreateEvent(Guid.NewGuid(), itemId, "Fourth", now.AddSeconds(-2));
        
        // Functions for projection
        Func<Event, IEnumerable<Guid>> getAffectedIds = evt =>
        {
            var payload = evt.Payload as TestEvent;
            return new[] { payload!.ItemId };
        };
        
        Func<Guid, TestItem?, Event, TestItem?> projectItem = (id, current, evt) =>
        {
            var payload = evt.Payload as TestEvent;
            if (current == null)
            {
                return new TestItem(id, payload!.NewName, 1);
            }
            // Append name to track order
            return current with 
            { 
                Name = current.Name + " -> " + payload!.NewName,
                UpdateCount = current.UpdateCount + 1 
            };
        };

        // Act - Process events out of order
        state = state.ProcessEvent(event3, getAffectedIds, projectItem); // Third
        state = state.ProcessEvent(event1, getAffectedIds, projectItem); // First  
        state = state.ProcessEvent(event4, getAffectedIds, projectItem); // Fourth
        state = state.ProcessEvent(event2, getAffectedIds, projectItem); // Second
        
        var unsafeState = state.GetCurrentState();
        
        // Move threshold forward so all events become safe
        var newThreshold = SortableUniqueId.Generate(now, Guid.Empty);
        state = state.UpdateSafeWindowThreshold(newThreshold, getAffectedIds, projectItem);
        
        var safeState = state.GetSafeState();

        // Assert
        Assert.Single(unsafeState);
        Assert.Single(safeState);
        
        var unsafeItem = unsafeState[itemId];
        var safeItem = safeState[itemId];
        
        // In unsafe state, events were applied in the order they were received
        Assert.Equal("Third -> First -> Fourth -> Second", unsafeItem.Name);
        Assert.Equal(4, unsafeItem.UpdateCount);
        
        // In safe state, events should be applied in chronological order
        // This verifies that when events transition from unsafe to safe,
        // they are reprocessed in the correct order
        Assert.Equal("First -> Second -> Third -> Fourth", safeItem.Name);
        Assert.Equal(4, safeItem.UpdateCount);
    }

    [Fact]
    public void MixedSafeAndUnsafeEvents_OutOfOrder_ProcessedCorrectly()
    {
        // Arrange
        var state = new SafeUnsafeProjectionStateV7<Guid, TestItem>();
        var itemId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        
        // Create safe window threshold (events older than 5 seconds are safe)
        var threshold = SortableUniqueId.Generate(now.AddSeconds(-5), Guid.Empty);
        state = state with { SafeWindowThreshold = threshold };

        // Create mix of safe and unsafe events
        var safeEvent1 = CreateEvent(Guid.NewGuid(), itemId, "Safe1", now.AddSeconds(-10));
        var safeEvent2 = CreateEvent(Guid.NewGuid(), itemId, "Safe2", now.AddSeconds(-7));
        var unsafeEvent1 = CreateEvent(Guid.NewGuid(), itemId, "Unsafe1", now.AddSeconds(-3));
        var unsafeEvent2 = CreateEvent(Guid.NewGuid(), itemId, "Unsafe2", now.AddSeconds(-1));
        
        // Functions for projection
        Func<Event, IEnumerable<Guid>> getAffectedIds = evt =>
        {
            var payload = evt.Payload as TestEvent;
            return new[] { payload!.ItemId };
        };
        
        Func<Guid, TestItem?, Event, TestItem?> projectItem = (id, current, evt) =>
        {
            var payload = evt.Payload as TestEvent;
            if (current == null)
            {
                return new TestItem(id, payload!.NewName, 1);
            }
            return current with 
            { 
                Name = current.Name + " -> " + payload!.NewName,
                UpdateCount = current.UpdateCount + 1 
            };
        };

        // Act - Process events in mixed order
        state = state.ProcessEvent(unsafeEvent1, getAffectedIds, projectItem);
        state = state.ProcessEvent(safeEvent2, getAffectedIds, projectItem);
        state = state.ProcessEvent(unsafeEvent2, getAffectedIds, projectItem);
        state = state.ProcessEvent(safeEvent1, getAffectedIds, projectItem);
        
        var currentState = state.GetCurrentState();
        var safeStateOnly = state.GetSafeState();

        // Assert
        Assert.Single(currentState);
        Assert.Single(safeStateOnly);
        
        var currentItem = currentState[itemId];
        var safeItem = safeStateOnly[itemId];
        
        // Current state has unsafe events applied
        // Safe events update the safe backup but current state only shows unsafe events
        Assert.Equal("Unsafe1 -> Unsafe2", currentItem.Name);
        Assert.Equal(2, currentItem.UpdateCount);
        
        // Safe state only has safe events in the order they were processed
        // (not necessarily chronological order unless they transition from unsafe to safe)
        Assert.Equal("Safe2 -> Safe1", safeItem.Name);
        Assert.Equal(2, safeItem.UpdateCount);
        
        // Check if item is marked as unsafe
        Assert.True(state.IsItemUnsafe(itemId));
        
        // Check unsafe events for the item
        var unsafeEvents = state.GetUnsafeEventsForItem(itemId).ToList();
        Assert.Equal(2, unsafeEvents.Count);
    }

    [Fact]
    public void DuplicateEventIds_InDifferentBatches_CurrentlyProcessedMultipleTimes()
    {
        // Arrange
        var state = new SafeUnsafeProjectionStateV7<Guid, TestItem>();
        var itemId = Guid.NewGuid();
        var eventId = Guid.NewGuid(); // Same event ID for duplicates
        var now = DateTime.UtcNow;
        
        // Create safe window threshold
        var threshold = SortableUniqueId.Generate(now.AddSeconds(-5), Guid.Empty);
        state = state with { SafeWindowThreshold = threshold };

        // Create duplicate events with same ID and timestamp
        var event1 = CreateEvent(eventId, itemId, "Update", now.AddSeconds(-3));
        var event2 = CreateEvent(eventId, itemId, "Update", now.AddSeconds(-3));
        
        // Functions for projection
        Func<Event, IEnumerable<Guid>> getAffectedIds = evt =>
        {
            var payload = evt.Payload as TestEvent;
            return new[] { payload!.ItemId };
        };
        
        var processCount = 0;
        Func<Guid, TestItem?, Event, TestItem?> projectItem = (id, current, evt) =>
        {
            processCount++;
            var payload = evt.Payload as TestEvent;
            if (current == null)
            {
                return new TestItem(id, payload!.NewName, 1);
            }
            return current with 
            { 
                Name = payload!.NewName,
                UpdateCount = current.UpdateCount + 1 
            };
        };

        // Act
        state = state.ProcessEvent(event1, getAffectedIds, projectItem);
        state = state.ProcessEvent(event2, getAffectedIds, projectItem);
        
        // Assert
        Assert.Equal(1, processCount); // Now only processes once (duplicate ignored)
        
        var currentState = state.GetCurrentState();
        Assert.Single(currentState);
        
        var item = currentState[itemId];
        Assert.Equal(1, item.UpdateCount); // Updated only once - duplicate filtered
        
        // Get all unsafe events
        var allUnsafeEvents = state.GetAllUnsafeEvents().ToList();
        
        // Only one event should be stored (duplicate not added)
        var unsafeEventsForItem = state.GetUnsafeEventsForItem(itemId).ToList();
        Assert.Single(unsafeEventsForItem); // Only one event stored, duplicate ignored
    }

    [Fact]
    public void ProcessedEventIds_CleanedUp_WhenEventsBecomeSafe()
    {
        // Arrange
        var state = new SafeUnsafeProjectionStateV7<Guid, TestItem>();
        var itemId = Guid.NewGuid();
        var eventId1 = Guid.NewGuid();
        var eventId2 = Guid.NewGuid();
        var now = DateTime.UtcNow;
        
        // Start with old threshold (everything is unsafe)
        var oldThreshold = SortableUniqueId.Generate(now.AddSeconds(-10), Guid.Empty);
        state = state with { SafeWindowThreshold = oldThreshold };

        // Create unsafe events
        var event1 = CreateEvent(eventId1, itemId, "First", now.AddSeconds(-5));
        var event2 = CreateEvent(eventId2, itemId, "Second", now.AddSeconds(-3));
        
        // Functions for projection
        Func<Event, IEnumerable<Guid>> getAffectedIds = evt =>
        {
            var payload = evt.Payload as TestEvent;
            return new[] { payload!.ItemId };
        };
        
        Func<Guid, TestItem?, Event, TestItem?> projectItem = (id, current, evt) =>
        {
            var payload = evt.Payload as TestEvent;
            if (current == null)
            {
                return new TestItem(id, payload!.NewName, 1);
            }
            return current with 
            { 
                Name = current.Name + " -> " + payload!.NewName,
                UpdateCount = current.UpdateCount + 1 
            };
        };

        // Act - Process unsafe events
        state = state.ProcessEvent(event1, getAffectedIds, projectItem);
        state = state.ProcessEvent(event2, getAffectedIds, projectItem);
        
        // Try to process duplicates (should be ignored)
        var stateBeforeDup = state;
        state = state.ProcessEvent(event1, getAffectedIds, projectItem);
        state = state.ProcessEvent(event2, getAffectedIds, projectItem);
        
        // Duplicates should have been ignored
        Assert.Equal(stateBeforeDup, state);
        
        // Now move the threshold forward so events become safe
        var newThreshold = SortableUniqueId.Generate(now, Guid.Empty);
        state = state.UpdateSafeWindowThreshold(newThreshold, getAffectedIds, projectItem);
        
        // After events become safe, try processing them again
        // They should NOT be blocked as duplicates anymore since they're safe
        // and no longer tracked in _processedEventIds
        state = state.ProcessEvent(event1, getAffectedIds, projectItem);
        state = state.ProcessEvent(event2, getAffectedIds, projectItem);
        
        // Assert - Safe events can be processed again (IDs were cleaned up)
        var currentState = state.GetCurrentState();
        var item = currentState[itemId];
        
        // The events were applied again after becoming safe
        Assert.Equal("First -> Second -> First -> Second", item.Name);
        Assert.Equal(4, item.UpdateCount);
    }
}