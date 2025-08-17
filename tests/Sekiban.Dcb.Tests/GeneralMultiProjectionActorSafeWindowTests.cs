using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Tests;

public class GeneralMultiProjectionActorSafeWindowTests
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly GeneralMultiProjectionActorOptions _options;

    public GeneralMultiProjectionActorSafeWindowTests()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<TestEventCreated>("TestEventCreated");
        eventTypes.RegisterEventType<TestEventUpdated>("TestEventUpdated");

        var tagTypes = new SimpleTagTypes();
        var tagProjectorTypes = new SimpleTagProjectorTypes();
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        
        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjector<TestMultiProjector>();

        _domainTypes = new DcbDomainTypes(
            eventTypes, 
            tagTypes, 
            tagProjectorTypes, 
            tagStatePayloadTypes, 
            multiProjectorTypes);

        // Set SafeWindow to 5 seconds for testing
        _options = new GeneralMultiProjectionActorOptions { SafeWindowMs = 5000 };
    }

    [Fact]
    public async Task SafeWindow_EventsOutsideWindow_ProcessedImmediately()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        
        // Create an event outside the safe window (10 seconds ago)
        var oldTimestamp = DateTime.UtcNow.AddSeconds(-10);
        var oldEvent = CreateEvent(new TestEventCreated("Test Item 1"), oldTimestamp);
        
        // Act
        await actor.AddEventsAsync(new[] { oldEvent });
        var safeState = await actor.GetStateAsync();
        var unsafeState = await actor.GetUnsafeStateAsync();
        
        // Assert
        Assert.True(safeState.IsSuccess);
        Assert.True(unsafeState.IsSuccess);
        
        var safePayload = safeState.GetValue().Payload as TestMultiProjector;
        var unsafePayload = unsafeState.GetValue().Payload as TestMultiProjector;
        
        // Both states should have the event
        Assert.Single(safePayload!.Items);
        Assert.Single(unsafePayload!.Items);
        Assert.Equal("Test Item 1", safePayload.Items[0]);
    }

    [Fact]
    public async Task SafeWindow_EventsInsideWindow_BufferedInUnsafeOnly()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        
        // Create an event within the safe window (2 seconds ago)
        var recentTimestamp = DateTime.UtcNow.AddSeconds(-2);
        var recentEvent = CreateEvent(new TestEventCreated("Recent Item"), recentTimestamp);
        
        // Act
        await actor.AddEventsAsync(new[] { recentEvent });
        var safeState = await actor.GetStateAsync(canGetUnsafeState: false); // Explicitly get safe state
        var unsafeState = await actor.GetUnsafeStateAsync();
        
        // Assert
        Assert.True(safeState.IsSuccess);
        Assert.True(unsafeState.IsSuccess);
        
        var safePayload = safeState.GetValue().Payload as TestMultiProjector;
        var unsafePayload = unsafeState.GetValue().Payload as TestMultiProjector;
        
        // Safe state should be empty, unsafe state should have the event
        Assert.Empty(safePayload!.Items);
        Assert.Single(unsafePayload!.Items);
        Assert.Equal("Recent Item", unsafePayload.Items[0]);
    }

    [Fact]
    public async Task SafeWindow_OutOfOrderEvents_ProcessedCorrectly()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        
        // Create events with different timestamps
        var oldTimestamp = DateTime.UtcNow.AddSeconds(-10);
        var recentTimestamp = DateTime.UtcNow.AddSeconds(-2);
        var middleTimestamp = DateTime.UtcNow.AddSeconds(-7);
        
        var events = new[]
        {
            CreateEvent(new TestEventCreated("Item 2"), recentTimestamp),  // Within safe window
            CreateEvent(new TestEventCreated("Item 1"), oldTimestamp),     // Outside safe window
            CreateEvent(new TestEventCreated("Item 3"), middleTimestamp)   // Outside safe window
        };
        
        // Act
        await actor.AddEventsAsync(events);
        var safeState = await actor.GetStateAsync(canGetUnsafeState: false); // Explicitly get safe state
        var unsafeState = await actor.GetUnsafeStateAsync();
        
        // Assert
        var safePayload = safeState.GetValue().Payload as TestMultiProjector;
        var unsafePayload = unsafeState.GetValue().Payload as TestMultiProjector;
        
        // Safe state should have only old events (Items 1 and 3)
        Assert.Equal(2, safePayload!.Items.Count);
        Assert.Contains("Item 1", safePayload.Items);
        Assert.Contains("Item 3", safePayload.Items);
        
        // Unsafe state should have all events
        Assert.Equal(3, unsafePayload!.Items.Count);
        Assert.Contains("Item 1", unsafePayload.Items);
        Assert.Contains("Item 2", unsafePayload.Items);
        Assert.Contains("Item 3", unsafePayload.Items);
    }

    [Fact]
    public async Task SafeWindow_UpdateEvents_MaintainCorrectState()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        
        // Create initial event outside safe window
        var oldTimestamp = DateTime.UtcNow.AddSeconds(-10);
        var createEvent = CreateEvent(new TestEventCreated("Original"), oldTimestamp);
        
        // Create update event within safe window
        var recentTimestamp = DateTime.UtcNow.AddSeconds(-2);
        var updateEvent = CreateEvent(new TestEventUpdated("Original", "Updated"), recentTimestamp);
        
        // Act
        await actor.AddEventsAsync(new[] { createEvent, updateEvent });
        var safeState = await actor.GetStateAsync(canGetUnsafeState: false); // Explicitly get safe state
        var unsafeState = await actor.GetUnsafeStateAsync();
        
        // Assert
        var safePayload = safeState.GetValue().Payload as TestMultiProjector;
        var unsafePayload = unsafeState.GetValue().Payload as TestMultiProjector;
        
        // Safe state should only have the create event (not updated)
        Assert.Single(safePayload!.Items);
        Assert.Equal("Original", safePayload.Items[0]);
        
        // Unsafe state should have the updated value
        Assert.Single(unsafePayload!.Items);
        Assert.Equal("Updated", unsafePayload.Items[0]);
    }

    [Fact]
    public async Task SafeWindow_StateReload_PreservesBufferedEvents()
    {
        // Arrange
        var actor1 = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        
        // Add event outside safe window
        var oldEvent = CreateEvent(new TestEventCreated("Old Item"), DateTime.UtcNow.AddSeconds(-10));
        await actor1.AddEventsAsync(new[] { oldEvent });
        
        // Get serializable state
        var serializableState = await actor1.GetSerializableStateAsync();
        
        // Create new actor and load state
        var actor2 = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        await actor2.SetCurrentState(serializableState.GetValue());
        
        // Add new event within safe window
        var recentEvent = CreateEvent(new TestEventCreated("Recent Item"), DateTime.UtcNow.AddSeconds(-2));
        await actor2.AddEventsAsync(new[] { recentEvent });
        
        // Act
        var safeState = await actor2.GetStateAsync(canGetUnsafeState: false); // Explicitly get safe state
        var unsafeState = await actor2.GetUnsafeStateAsync();
        
        // Assert
        var safePayload = safeState.GetValue().Payload as TestMultiProjector;
        var unsafePayload = unsafeState.GetValue().Payload as TestMultiProjector;
        
        // Safe state should only have old item
        Assert.Single(safePayload!.Items);
        Assert.Equal("Old Item", safePayload.Items[0]);
        
        // Unsafe state should have both items
        Assert.Equal(2, unsafePayload!.Items.Count);
        Assert.Contains("Old Item", unsafePayload.Items);
        Assert.Contains("Recent Item", unsafePayload.Items);
    }

    private Event CreateEvent(IEventPayload payload, DateTime timestamp)
    {
        var sortableId = SortableUniqueId.Generate(timestamp, Guid.NewGuid());
        return new Event(
            payload,
            sortableId,
            payload.GetType().Name,
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "TestUser"),
            new List<string>());
    }

    // Test event payloads
    public record TestEventCreated(string Name) : IEventPayload;
    public record TestEventUpdated(string OldName, string NewName) : IEventPayload;

    // Test multi-projector
    public record TestMultiProjector : IMultiProjector<TestMultiProjector>
    {
        
        public List<string> Items { get; init; }
        
        public TestMultiProjector() : this(new List<string>()) { }
        
        public TestMultiProjector(List<string> items)
        {
            Items = items;
        }
        
        public static TestMultiProjector GenerateInitialPayload() => new(new List<string>());
        
        public static string MultiProjectorName => "TestMultiProjector";
        
        public static string MultiProjectorVersion => "1.0.0";
        
        public static ResultBox<TestMultiProjector> Project(TestMultiProjector payload, Event ev, List<ITag> tags)
        {
            var result = ev.Payload switch
            {
                TestEventCreated created => new TestMultiProjector(payload.Items.Concat(new[] { created.Name }).ToList()),
                TestEventUpdated updated => new TestMultiProjector(
                    payload.Items.Select(item => item == updated.OldName ? updated.NewName : item).ToList()
                ),
                _ => payload
            };
            return ResultBox.FromValue(result);
        }
    }
}