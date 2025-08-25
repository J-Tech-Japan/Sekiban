using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

public class SafeUnsafeStateAccessorTests
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly GeneralMultiProjectionActorOptions _options;

    public SafeUnsafeStateAccessorTests()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<TestItemAdded>("TestItemAdded");
        eventTypes.RegisterEventType<TestItemUpdated>("TestItemUpdated");

        var tagTypes = new SimpleTagTypes();
        var tagProjectorTypes = new SimpleTagProjectorTypes();
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();

        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjector<EfficientTestProjector>();

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
    public async Task SingleStateAccessor_UsesSingleStateInsteadOfDual()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, EfficientTestProjector.MultiProjectorName, _options);

        // Add events both inside and outside safe window
        var oldEvent = CreateEvent(new TestItemAdded("Item1", 100), DateTime.UtcNow.AddSeconds(-10));
        var recentEvent = CreateEvent(new TestItemAdded("Item2", 200), DateTime.UtcNow.AddSeconds(-2));

        // Act
        await actor.AddEventsAsync(new[] { oldEvent, recentEvent });

        // Get both safe and unsafe states
        var safeStateResult = await actor.GetStateAsync(canGetUnsafeState: false);
        var unsafeStateResult = await actor.GetUnsafeStateAsync();

        // Assert
        Assert.True(safeStateResult.IsSuccess);
        Assert.True(unsafeStateResult.IsSuccess);

        var safeState = safeStateResult.GetValue();
        var unsafeState = unsafeStateResult.GetValue();

        var safePayload = safeState.Payload as EfficientTestProjector;
        var unsafePayload = unsafeState.Payload as EfficientTestProjector;

        Assert.NotNull(safePayload);
        Assert.NotNull(unsafePayload);

        // Safe state should only have Item1 (outside safe window)
        Assert.Single(safePayload.Items);
        Assert.Contains("Item1", safePayload.Items.Keys);
        Assert.Equal(100, safePayload.Items["Item1"]);

        // Unsafe state should have both items
        Assert.Equal(2, unsafePayload.Items.Count);
        Assert.Contains("Item1", unsafePayload.Items.Keys);
        Assert.Contains("Item2", unsafePayload.Items.Keys);
        Assert.Equal(100, unsafePayload.Items["Item1"]);
        Assert.Equal(200, unsafePayload.Items["Item2"]);
    }

    [Fact]
    public async Task SingleStateAccessor_HandlesEventTransitionsCorrectly()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, EfficientTestProjector.MultiProjectorName, _options);

        // Add event within safe window
        var recentEvent = CreateEvent(new TestItemAdded("RecentItem", 300), DateTime.UtcNow.AddSeconds(-2));
        await actor.AddEventsAsync(new[] { recentEvent });

        // Get initial states
        var initialSafeResult = await actor.GetStateAsync(canGetUnsafeState: false);
        var initialUnsafeResult = await actor.GetUnsafeStateAsync();

        var initialSafePayload = initialSafeResult.GetValue().Payload as EfficientTestProjector;
        var initialUnsafePayload = initialUnsafeResult.GetValue().Payload as EfficientTestProjector;

        // Initial safe state should be empty
        Assert.Empty(initialSafePayload!.Items);

        // Initial unsafe state should have the recent item
        Assert.Single(initialUnsafePayload!.Items);
        Assert.Contains("RecentItem", initialUnsafePayload.Items.Keys);

        // Wait for event to become safe (simulate time passing)
        await Task.Delay(100);

        // Add another event that's definitely safe
        var oldEvent = CreateEvent(new TestItemAdded("OldItem", 400), DateTime.UtcNow.AddSeconds(-10));
        await actor.AddEventsAsync(new[] { oldEvent });

        // Get final states
        var finalSafeResult = await actor.GetStateAsync(canGetUnsafeState: false);
        var finalUnsafeResult = await actor.GetUnsafeStateAsync();

        var finalSafePayload = finalSafeResult.GetValue().Payload as EfficientTestProjector;
        var finalUnsafePayload = finalUnsafeResult.GetValue().Payload as EfficientTestProjector;

        // Final safe state should have at least the old item
        Assert.Contains("OldItem", finalSafePayload!.Items.Keys);
        Assert.Equal(400, finalSafePayload.Items["OldItem"]);

        // Final unsafe state should have both items
        Assert.Equal(2, finalUnsafePayload!.Items.Count);
        Assert.Contains("OldItem", finalUnsafePayload.Items.Keys);
        Assert.Contains("RecentItem", finalUnsafePayload.Items.Keys);
    }

    [Fact]
    public async Task SingleStateAccessor_HandlesUpdatesCorrectly()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, EfficientTestProjector.MultiProjectorName, _options);

        // Add initial item
        var addEvent = CreateEvent(new TestItemAdded("Item1", 100), DateTime.UtcNow.AddSeconds(-10));
        await actor.AddEventsAsync(new[] { addEvent });

        // Update the item (within safe window)
        var updateEvent = CreateEvent(new TestItemUpdated("Item1", 500), DateTime.UtcNow.AddSeconds(-2));
        await actor.AddEventsAsync(new[] { updateEvent });

        // Get states
        var safeStateResult = await actor.GetStateAsync(canGetUnsafeState: false);
        var unsafeStateResult = await actor.GetUnsafeStateAsync();

        var safePayload = safeStateResult.GetValue().Payload as EfficientTestProjector;
        var unsafePayload = unsafeStateResult.GetValue().Payload as EfficientTestProjector;

        // Safe state should have original value (update is within safe window)
        Assert.Single(safePayload!.Items);
        Assert.Equal(100, safePayload.Items["Item1"]);

        // Unsafe state should have updated value
        Assert.Single(unsafePayload!.Items);
        Assert.Equal(500, unsafePayload.Items["Item1"]);
    }

    [Fact]
    public async Task SingleStateAccessor_PreventsDuplicateEvents()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, EfficientTestProjector.MultiProjectorName, _options);

        // Create the same event
        var event1 = CreateEvent(new TestItemAdded("Item1", 100), DateTime.UtcNow.AddSeconds(-10));

        // Process the same event twice
        await actor.AddEventsAsync(new[] { event1 });
        await actor.AddEventsAsync(new[] { event1 }); // Duplicate

        // Get state
        var stateResult = await actor.GetStateAsync();
        var payload = stateResult.GetValue().Payload as EfficientTestProjector;

        // Should only have one item (duplicate prevented)
        Assert.Single(payload!.Items);
        Assert.Equal(100, payload.Items["Item1"]);

        // Version should only increment once
        var state = stateResult.GetValue();
        Assert.Equal(1, state.Version);
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
    public record TestItemAdded(string Name, int Value) : IEventPayload;
    public record TestItemUpdated(string Name, int NewValue) : IEventPayload;

    // Efficient test projector that uses SafeUnsafeMultiProjectionState
    public record EfficientTestProjector : IMultiProjector<EfficientTestProjector>
    {
        public Dictionary<string, int> Items { get; init; } = new();

        public static string MultiProjectorName => "EfficientTestProjector";
        public static string MultiProjectorVersion => "1.0.0";

        public static EfficientTestProjector GenerateInitialPayload() =>
            new() { Items = new Dictionary<string, int>() };

        public static ResultBox<EfficientTestProjector> Project(
            EfficientTestProjector payload,
            Event ev,
            List<ITag> tags)
        {
            var result = ev.Payload switch
            {
                TestItemAdded added => payload with
                {
                    Items = new Dictionary<string, int>(payload.Items) { [added.Name] = added.Value }
                },
                TestItemUpdated updated => payload with
                {
                    Items = new Dictionary<string, int>(payload.Items) { [updated.Name] = updated.NewValue }
                },
                _ => payload
            };
            return ResultBox.FromValue(result);
        }
    }
}
