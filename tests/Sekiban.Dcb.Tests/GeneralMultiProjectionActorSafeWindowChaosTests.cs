using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for verifying that SafeWindow correctly handles out-of-order and duplicate events
/// </summary>
public class GeneralMultiProjectionActorSafeWindowChaosTests
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly GeneralMultiProjectionActorOptions _options;

    public GeneralMultiProjectionActorSafeWindowChaosTests()
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
            multiProjectorTypes,
            new SimpleQueryTypes());

        // Set SafeWindow to 5 seconds for testing
        _options = new GeneralMultiProjectionActorOptions { SafeWindowMs = 5000 };
    }

    [Fact]
    public async Task SafeWindow_OutOfOrderEvents_CorrectlySortedInSafeState()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);

        // Create events in SafeWindow with specific timestamps (but will send out of order)
        var now = DateTime.UtcNow;
        var event1 = CreateEvent(
            new TestEventCreated("Item 1"),
            now.AddSeconds(-3),
            Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var event2 = CreateEvent(
            new TestEventCreated("Item 2"),
            now.AddSeconds(-2.5),
            Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var event3 = CreateEvent(
            new TestEventCreated("Item 3"),
            now.AddSeconds(-2),
            Guid.Parse("00000000-0000-0000-0000-000000000003"));

        // Act - Send events in wrong order (3, 1, 2)
        await actor.AddEventsAsync(new[] { event3, event1, event2 });

        // Initial state - unsafe state has events in the order they were received
        var unsafeState1 = await actor.GetUnsafeStateAsync();
        var unsafePayload1 = unsafeState1.GetValue().Payload as TestMultiProjector;
        Assert.Equal(3, unsafePayload1!.Items.Count);
        // Unsafe state has them in received order: 3, 1, 2
        Assert.Equal("Item 3", unsafePayload1.Items[0]);
        Assert.Equal("Item 1", unsafePayload1.Items[1]);
        Assert.Equal("Item 2", unsafePayload1.Items[2]);

        // Wait for events to move outside SafeWindow
        await Task.Delay(5500);

        // Add a new event to trigger processing
        var triggerEvent = CreateEvent(new TestEventCreated("Trigger"), now.AddSeconds(-10));
        await actor.AddEventsAsync(new[] { triggerEvent });

        // Act - Get safe state
        var safeState = await actor.GetStateAsync(canGetUnsafeState: false);
        var safePayload = safeState.GetValue().Payload as TestMultiProjector;

        // Assert - Safe state should have events in correct chronological order
        Assert.Equal(4, safePayload!.Items.Count);
        Assert.Equal("Trigger", safePayload.Items[0]); // Oldest event
        Assert.Equal("Item 1", safePayload.Items[1]); // Correct chronological order
        Assert.Equal("Item 2", safePayload.Items[2]);
        Assert.Equal("Item 3", safePayload.Items[3]);
    }

    [Fact]
    public async Task SafeWindow_DuplicateEvents_OnlyProcessedOnce()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);

        // Create the same event
        var eventId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var event1 = CreateEvent(new TestEventCreated("Item 1"), DateTime.UtcNow.AddSeconds(-3), eventId);
        var event1Duplicate = CreateEvent(new TestEventCreated("Item 1"), DateTime.UtcNow.AddSeconds(-3), eventId);

        // Act - Send the same event multiple times
        await actor.AddEventsAsync(new[] { event1 });
        await actor.AddEventsAsync(new[] { event1Duplicate });
        await actor.AddEventsAsync(new[] { event1Duplicate }); // Send again

        // Wait for events to move outside SafeWindow
        await Task.Delay(5500);

        // Add a trigger event to process buffered events
        var triggerEvent = CreateEvent(new TestEventCreated("Trigger"), DateTime.UtcNow.AddSeconds(-10));
        await actor.AddEventsAsync(new[] { triggerEvent });

        // Get safe state
        var safeState = await actor.GetStateAsync(canGetUnsafeState: false);
        var safePayload = safeState.GetValue().Payload as TestMultiProjector;

        // Assert - Event should only appear once
        Assert.Equal(2, safePayload!.Items.Count);
        Assert.Equal("Trigger", safePayload.Items[0]);
        Assert.Equal("Item 1", safePayload.Items[1]);
        Assert.Single(safePayload.Items.Where(i => i == "Item 1"));
    }

    [Fact]
    public async Task SafeWindow_ChaosScenario_EventuallyConsistent()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        var now = DateTime.UtcNow;

        // Create a mix of events with various timestamps
        var events = new List<Event>
        {
            // Old events (outside SafeWindow)
            CreateEvent(new TestEventCreated("Old 1"), now.AddSeconds(-10), Guid.NewGuid()),
            CreateEvent(new TestEventCreated("Old 2"), now.AddSeconds(-8), Guid.NewGuid()),

            // Recent events (inside SafeWindow)
            CreateEvent(
                new TestEventCreated("Recent 1"),
                now.AddSeconds(-3),
                Guid.Parse("10000000-0000-0000-0000-000000000001")),
            CreateEvent(
                new TestEventCreated("Recent 2"),
                now.AddSeconds(-2),
                Guid.Parse("20000000-0000-0000-0000-000000000002")),
            CreateEvent(
                new TestEventCreated("Recent 3"),
                now.AddSeconds(-1),
                Guid.Parse("30000000-0000-0000-0000-000000000003"))
        };

        // Duplicate event
        var duplicateEvent = CreateEvent(
            new TestEventCreated("Recent 2"),
            now.AddSeconds(-2),
            Guid.Parse("20000000-0000-0000-0000-000000000002"));

        // Act - Send events in random order with duplicates
        await actor.AddEventsAsync(new[] { events[4] }); // Recent 3
        await actor.AddEventsAsync(new[] { events[0] }); // Old 1
        await actor.AddEventsAsync(new[] { duplicateEvent }); // Duplicate Recent 2
        await actor.AddEventsAsync(new[] { events[2] }); // Recent 1
        await actor.AddEventsAsync(new[] { events[1] }); // Old 2
        await actor.AddEventsAsync(new[] { events[3] }); // Recent 2
        await actor.AddEventsAsync(new[] { duplicateEvent }); // Duplicate Recent 2 again

        // Immediate check - safe state should have old events only
        var immediateSafeState = await actor.GetStateAsync(canGetUnsafeState: false);
        var immediateSafePayload = immediateSafeState.GetValue().Payload as TestMultiProjector;
        Assert.Equal(2, immediateSafePayload!.Items.Count);
        Assert.Contains("Old 1", immediateSafePayload.Items);
        Assert.Contains("Old 2", immediateSafePayload.Items);

        // Wait for events to move outside SafeWindow
        await Task.Delay(5500);

        // Add a trigger event
        var triggerEvent = CreateEvent(new TestEventCreated("Final"), now.AddSeconds(-15));
        await actor.AddEventsAsync(new[] { triggerEvent });

        // Final check - all events should be in correct order
        var finalSafeState = await actor.GetStateAsync(canGetUnsafeState: false);
        var finalSafePayload = finalSafeState.GetValue().Payload as TestMultiProjector;

        // Assert - Should have all unique events in chronological order
        Assert.Equal(6, finalSafePayload!.Items.Count);
        Assert.Equal("Final", finalSafePayload.Items[0]); // -15s
        Assert.Equal("Old 1", finalSafePayload.Items[1]); // -10s
        Assert.Equal("Old 2", finalSafePayload.Items[2]); // -8s
        Assert.Equal("Recent 1", finalSafePayload.Items[3]); // -3s
        Assert.Equal("Recent 2", finalSafePayload.Items[4]); // -2s (no duplicate)
        Assert.Equal("Recent 3", finalSafePayload.Items[5]); // -1s
    }

    [Fact]
    public async Task SafeWindow_UpdateEvents_CorrectOrderingMaintained()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        var now = DateTime.UtcNow;

        // Create events with updates in SafeWindow
        var createEvent = CreateEvent(
            new TestEventCreated("Original"),
            now.AddSeconds(-4),
            Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var update1 = CreateEvent(
            new TestEventUpdated("Original", "Updated1"),
            now.AddSeconds(-3),
            Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var update2 = CreateEvent(
            new TestEventUpdated("Updated1", "Updated2"),
            now.AddSeconds(-2),
            Guid.Parse("00000000-0000-0000-0000-000000000003"));
        var update3 = CreateEvent(
            new TestEventUpdated("Updated2", "Final"),
            now.AddSeconds(-1),
            Guid.Parse("00000000-0000-0000-0000-000000000004"));

        // Act - Send updates out of order
        await actor.AddEventsAsync(new[] { update3 }); // Last update first
        await actor.AddEventsAsync(new[] { createEvent }); // Create event
        await actor.AddEventsAsync(new[] { update2 }); // Middle update
        await actor.AddEventsAsync(new[] { update1 }); // First update

        // Unsafe state will have wrong order
        var unsafeState = await actor.GetUnsafeStateAsync();
        var unsafePayload = unsafeState.GetValue().Payload as TestMultiProjector;
        // Due to out-of-order processing, the state might be incorrect
        Assert.Single(unsafePayload!.Items);

        // Wait for events to move outside SafeWindow
        await Task.Delay(5500);

        // Add trigger event
        var triggerEvent = CreateEvent(new TestEventCreated("Trigger"), now.AddSeconds(-10));
        await actor.AddEventsAsync(new[] { triggerEvent });

        // Get safe state
        var safeState = await actor.GetStateAsync(canGetUnsafeState: false);
        var safePayload = safeState.GetValue().Payload as TestMultiProjector;

        // Assert - Updates should be applied in correct order
        Assert.Equal(2, safePayload!.Items.Count);
        Assert.Equal("Trigger", safePayload.Items[0]);
        Assert.Equal("Final", safePayload.Items[1]); // Correctly updated through all stages
    }

    private Event CreateEvent(IEventPayload payload, DateTime timestamp, Guid? eventId = null)
    {
        var sortableId = SortableUniqueId.Generate(timestamp, Guid.NewGuid());
        return new Event(
            payload,
            sortableId,
            payload.GetType().Name,
            eventId ?? Guid.NewGuid(),
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

        public TestMultiProjector(List<string> items) => Items = items;

        public static TestMultiProjector GenerateInitialPayload() => new(new List<string>());

        public static string MultiProjectorName => "TestMultiProjector";

        public static string MultiProjectorVersion => "1.0.0";

    public static ResultBox<TestMultiProjector> Project(TestMultiProjector payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold)
        {
            var result = ev.Payload switch
            {
                TestEventCreated created => new TestMultiProjector(
                    payload.Items.Concat(new[] { created.Name }).ToList()),
                TestEventUpdated updated => new TestMultiProjector(
                    payload.Items.Select(item => item == updated.OldName ? updated.NewName : item).ToList()),
                _ => payload
            };
            return ResultBox.FromValue(result);
        }
    }
}
