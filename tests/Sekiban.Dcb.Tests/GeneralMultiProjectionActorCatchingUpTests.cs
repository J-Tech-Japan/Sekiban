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
using Xunit;

namespace Sekiban.Dcb.Tests;

public class GeneralMultiProjectionActorCatchingUpTests
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly GeneralMultiProjectionActorOptions _options;

    public GeneralMultiProjectionActorCatchingUpTests()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<TestEventCreated>("TestEventCreated");
        eventTypes.RegisterEventType<TestEventUpdated>("TestEventUpdated");

        var tagTypes = new SimpleTagTypes();
        var tagProjectorTypes = new SimpleTagProjectorTypes();
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        
        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjector<TestMultiProjector, TestMultiProjector>(TestMultiProjector.MultiProjectorName);

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
    public async Task CatchingUp_WhenNotFinished_StateReflectsCatchingUp()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        
        // Create events
        var oldEvent = CreateEvent(new TestEventCreated("Item 1"), DateTime.UtcNow.AddSeconds(-10));
        var recentEvent = CreateEvent(new TestEventCreated("Item 2"), DateTime.UtcNow.AddSeconds(-2));
        
        // Act - Add events with finishedCatchUp = false
        await actor.AddEventsAsync(new[] { oldEvent, recentEvent }, finishedCatchUp: false);
        
        var safeState = await actor.GetStateAsync(canGetUnsafeState: false); // Explicitly get safe state
        var unsafeState = await actor.GetUnsafeStateAsync();
        var defaultState = await actor.GetStateAsync(); // Default behavior
        
        // Assert
        Assert.True(safeState.IsSuccess);
        Assert.True(unsafeState.IsSuccess);
        Assert.True(defaultState.IsSuccess);
        
        // All states should NOT be caught up
        Assert.False(safeState.GetValue().IsCatchedUp);
        Assert.False(unsafeState.GetValue().IsCatchedUp);
        Assert.False(defaultState.GetValue().IsCatchedUp);
        
        // Safe state should be marked as safe
        Assert.True(safeState.GetValue().IsSafeState);
        // Unsafe state should be marked as unsafe
        Assert.False(unsafeState.GetValue().IsSafeState);
        // Default state should be unsafe (since there are buffered events)
        Assert.False(defaultState.GetValue().IsSafeState);
    }

    [Fact]
    public async Task CatchingUp_WhenFinished_StateReflectsCaughtUp()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        
        // Create events
        var event1 = CreateEvent(new TestEventCreated("Item 1"), DateTime.UtcNow.AddSeconds(-10));
        var event2 = CreateEvent(new TestEventCreated("Item 2"), DateTime.UtcNow.AddSeconds(-8));
        
        // Act - First add with catching up
        await actor.AddEventsAsync(new[] { event1 }, finishedCatchUp: false);
        // Then add with finished catching up
        await actor.AddEventsAsync(new[] { event2 }, finishedCatchUp: true);
        
        var state = await actor.GetStateAsync();
        
        // Assert
        Assert.True(state.IsSuccess);
        Assert.True(state.GetValue().IsCatchedUp);
    }

    [Fact]
    public async Task GetSerializableState_WithCanGetUnsafeFalse_ReturnsSafeStateOnly()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        
        // Add event outside safe window
        var oldEvent = CreateEvent(new TestEventCreated("Old Item"), DateTime.UtcNow.AddSeconds(-10));
        await actor.AddEventsAsync(new[] { oldEvent });
        
        // Add event within safe window
        var recentEvent = CreateEvent(new TestEventCreated("Recent Item"), DateTime.UtcNow.AddSeconds(-2));
        await actor.AddEventsAsync(new[] { recentEvent }, finishedCatchUp: false);
        
        // Act - Get serializable state with canGetUnsafeState = false
        var serializableState = await actor.GetSerializableStateAsync(canGetUnsafeState: false);
        
        // Assert
        Assert.True(serializableState.IsSuccess);
        var state = serializableState.GetValue();
        
        // Should be safe state
        Assert.True(state.IsSafeState);
        // Should not be caught up (because we set finishedCatchUp = false)
        Assert.False(state.IsCatchedUp);
        
        // Verify the state only contains the old event (safe state)
        // This is implicitly tested by IsSafeState being true
    }

    [Fact]
    public async Task GetSerializableState_WithCanGetUnsafeTrue_CanReturnUnsafeState()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        
        // Add event within safe window
        var recentEvent = CreateEvent(new TestEventCreated("Recent Item"), DateTime.UtcNow.AddSeconds(-2));
        await actor.AddEventsAsync(new[] { recentEvent });
        
        // Act - Get serializable state with canGetUnsafeState = true (default)
        var serializableState = await actor.GetSerializableStateAsync(canGetUnsafeState: true);
        
        // Assert
        Assert.True(serializableState.IsSuccess);
        var state = serializableState.GetValue();
        
        // Should be unsafe state (because there are buffered events)
        Assert.False(state.IsSafeState);
        Assert.True(state.IsCatchedUp);
    }

    [Fact]
    public async Task StateReload_PreservesCatchingUpState()
    {
        // Arrange
        var actor1 = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        
        // Add events with catching up state
        var event1 = CreateEvent(new TestEventCreated("Item 1"), DateTime.UtcNow.AddSeconds(-10));
        await actor1.AddEventsAsync(new[] { event1 }, finishedCatchUp: false);
        
        // Get serializable state
        var serializableState = await actor1.GetSerializableStateAsync();
        
        // Create new actor and load state
        var actor2 = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        await actor2.SetCurrentState(serializableState.GetValue());
        
        // Act
        var reloadedState = await actor2.GetStateAsync();
        
        // Assert - Catching up state should be preserved
        Assert.True(reloadedState.IsSuccess);
        Assert.False(reloadedState.GetValue().IsCatchedUp);
    }

    [Fact]
    public async Task SafeState_ForSnapshot_OnlyReturnsSafeData()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        
        // Add old event (safe)
        var oldEvent = CreateEvent(new TestEventCreated("Safe Item"), DateTime.UtcNow.AddSeconds(-10));
        await actor.AddEventsAsync(new[] { oldEvent });
        
        // Add recent events (unsafe)
        var recentEvent1 = CreateEvent(new TestEventCreated("Unsafe Item 1"), DateTime.UtcNow.AddSeconds(-2));
        var recentEvent2 = CreateEvent(new TestEventCreated("Unsafe Item 2"), DateTime.UtcNow.AddSeconds(-1));
        await actor.AddEventsAsync(new[] { recentEvent1, recentEvent2 });
        
        // Act - Get state for snapshot (safe state only)
        var snapshotState = await actor.GetSerializableStateAsync(canGetUnsafeState: false);
        
        // Assert
        Assert.True(snapshotState.IsSuccess);
        var state = snapshotState.GetValue();
        
        // Should be safe state suitable for snapshots
        Assert.True(state.IsSafeState);
        Assert.True(state.IsCatchedUp);
        
        // Create new actor from snapshot
        var restoredActor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        await restoredActor.SetCurrentState(state);
        
        var restoredState = await restoredActor.GetStateAsync();
        var payload = restoredState.GetValue().Payload as TestMultiProjector;
        
        // Should only have the safe item
        Assert.Single(payload!.Items);
        Assert.Equal("Safe Item", payload.Items[0]);
    }

    [Fact]
    public async Task GetStateAsync_WithCanGetUnsafeFalse_ReturnsSafeStateOnly()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        
        // Add event outside safe window (safe)
        var oldEvent = CreateEvent(new TestEventCreated("Safe Item"), DateTime.UtcNow.AddSeconds(-10));
        await actor.AddEventsAsync(new[] { oldEvent });
        
        // Add event within safe window (unsafe)
        var recentEvent = CreateEvent(new TestEventCreated("Unsafe Item"), DateTime.UtcNow.AddSeconds(-2));
        await actor.AddEventsAsync(new[] { recentEvent });
        
        // Act - Get state with canGetUnsafeState = false
        var safeOnlyState = await actor.GetStateAsync(canGetUnsafeState: false);
        var defaultState = await actor.GetStateAsync(); // Should get unsafe state by default
        
        // Assert
        Assert.True(safeOnlyState.IsSuccess);
        Assert.True(defaultState.IsSuccess);
        
        var safePayload = safeOnlyState.GetValue().Payload as TestMultiProjector;
        var defaultPayload = defaultState.GetValue().Payload as TestMultiProjector;
        
        // Safe state should only have safe item
        Assert.Single(safePayload!.Items);
        Assert.Equal("Safe Item", safePayload.Items[0]);
        Assert.True(safeOnlyState.GetValue().IsSafeState);
        
        // Default state should have both items (unsafe state)
        Assert.Equal(2, defaultPayload!.Items.Count);
        Assert.Contains("Safe Item", defaultPayload.Items);
        Assert.Contains("Unsafe Item", defaultPayload.Items);
        Assert.False(defaultState.GetValue().IsSafeState);
    }

    [Fact]
    public async Task GetStateAsync_WithNoBufferedEvents_AlwaysReturnsSafeState()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        
        // Add only old events (outside safe window)
        var oldEvent1 = CreateEvent(new TestEventCreated("Item 1"), DateTime.UtcNow.AddSeconds(-10));
        var oldEvent2 = CreateEvent(new TestEventCreated("Item 2"), DateTime.UtcNow.AddSeconds(-8));
        await actor.AddEventsAsync(new[] { oldEvent1, oldEvent2 });
        
        // Act
        var stateWithUnsafe = await actor.GetStateAsync(canGetUnsafeState: true);
        var stateWithoutUnsafe = await actor.GetStateAsync(canGetUnsafeState: false);
        
        // Assert - Both should return safe state since there are no buffered events
        Assert.True(stateWithUnsafe.IsSuccess);
        Assert.True(stateWithoutUnsafe.IsSuccess);
        
        Assert.True(stateWithUnsafe.GetValue().IsSafeState);
        Assert.True(stateWithoutUnsafe.GetValue().IsSafeState);
        
        var payload1 = stateWithUnsafe.GetValue().Payload as TestMultiProjector;
        var payload2 = stateWithoutUnsafe.GetValue().Payload as TestMultiProjector;
        
        // Both should have the same items
        Assert.Equal(2, payload1!.Items.Count);
        Assert.Equal(2, payload2!.Items.Count);
        Assert.Equal(payload1.Items, payload2.Items);
    }

    [Fact]
    public async Task CatchingUp_OldDataOnly_AlwaysSafeState()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        
        // Add only old events (outside safe window) during catch up
        var oldEvent1 = CreateEvent(new TestEventCreated("Item 1"), DateTime.UtcNow.AddSeconds(-10));
        var oldEvent2 = CreateEvent(new TestEventCreated("Item 2"), DateTime.UtcNow.AddSeconds(-8));
        
        // Act - Phase 1: During catch up with old data only
        await actor.AddEventsAsync(new[] { oldEvent1 }, finishedCatchUp: false);
        var stateDuringCatchUp = await actor.GetStateAsync(canGetUnsafeState: false);
        
        // Assert - Should be safe but not caught up
        Assert.True(stateDuringCatchUp.IsSuccess);
        Assert.False(stateDuringCatchUp.GetValue().IsCatchedUp, "Should NOT be caught up during catch up");
        Assert.True(stateDuringCatchUp.GetValue().IsSafeState, "Should be safe state with old data only");
        
        // Act - Phase 2: After catch up completed with old data only
        await actor.AddEventsAsync(new[] { oldEvent2 }, finishedCatchUp: true);
        var stateAfterCatchUp = await actor.GetStateAsync(canGetUnsafeState: false);
        
        // Assert - Should be both safe and caught up
        Assert.True(stateAfterCatchUp.IsSuccess);
        Assert.True(stateAfterCatchUp.GetValue().IsCatchedUp, "Should be caught up after finish");
        Assert.True(stateAfterCatchUp.GetValue().IsSafeState, "Should be safe state with old data only");
        
        // Verify the data
        var payload = stateAfterCatchUp.GetValue().Payload as TestMultiProjector;
        Assert.Equal(2, payload!.Items.Count);
        Assert.Contains("Item 1", payload.Items);
        Assert.Contains("Item 2", payload.Items);
    }

    [Fact]
    public async Task CatchingUp_MixedData_ReflectsCorrectStates()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        
        // Create mixed events (old and recent)
        var oldEvent = CreateEvent(new TestEventCreated("Old Item"), DateTime.UtcNow.AddSeconds(-10));
        var recentEvent = CreateEvent(new TestEventCreated("Recent Item"), DateTime.UtcNow.AddSeconds(-2));
        
        // Act - Phase 1: During catch up with mixed data
        await actor.AddEventsAsync(new[] { oldEvent, recentEvent }, finishedCatchUp: false);
        
        var safeStateDuringCatchUp = await actor.GetStateAsync(canGetUnsafeState: false);
        var unsafeStateDuringCatchUp = await actor.GetStateAsync(canGetUnsafeState: true);
        
        // Assert - During catch up
        // Safe state: not caught up, but is safe
        Assert.False(safeStateDuringCatchUp.GetValue().IsCatchedUp, "Safe state should NOT be caught up during catch up");
        Assert.True(safeStateDuringCatchUp.GetValue().IsSafeState, "Should be safe state");
        
        // Unsafe state: not caught up, not safe (has buffered events)
        Assert.False(unsafeStateDuringCatchUp.GetValue().IsCatchedUp, "Unsafe state should NOT be caught up during catch up");
        Assert.False(unsafeStateDuringCatchUp.GetValue().IsSafeState, "Should NOT be safe state when has buffered events");
        
        // Act - Phase 2: Complete catch up
        var finalEvent = CreateEvent(new TestEventCreated("Final Item"), DateTime.UtcNow.AddSeconds(-7));
        await actor.AddEventsAsync(new[] { finalEvent }, finishedCatchUp: true);
        
        var safeStateAfterCatchUp = await actor.GetStateAsync(canGetUnsafeState: false);
        var unsafeStateAfterCatchUp = await actor.GetStateAsync(canGetUnsafeState: true);
        
        // Assert - After catch up completed
        // Safe state: caught up and safe
        Assert.True(safeStateAfterCatchUp.GetValue().IsCatchedUp, "Safe state should be caught up after finish");
        Assert.True(safeStateAfterCatchUp.GetValue().IsSafeState, "Should still be safe state");
        
        // Unsafe state: caught up but not safe (still has buffered recent event)
        Assert.True(unsafeStateAfterCatchUp.GetValue().IsCatchedUp, "Unsafe state should be caught up after finish");
        Assert.False(unsafeStateAfterCatchUp.GetValue().IsSafeState, "Should NOT be safe state with buffered events");
    }

    [Fact]
    public async Task CatchingUp_TransitionFromCatchingUpToCaughtUp()
    {
        // Arrange
        var actor = new GeneralMultiProjectionActor(_domainTypes, TestMultiProjector.MultiProjectorName, _options);
        
        // Phase 1: Initial events during catching up
        var batch1 = new[]
        {
            CreateEvent(new TestEventCreated("Item 1"), DateTime.UtcNow.AddSeconds(-15)),
            CreateEvent(new TestEventCreated("Item 2"), DateTime.UtcNow.AddSeconds(-12))
        };
        await actor.AddEventsAsync(batch1, finishedCatchUp: false);
        
        var state1 = await actor.GetStateAsync();
        Assert.False(state1.GetValue().IsCatchedUp);
        
        // Phase 2: More events, still catching up
        var batch2 = new[]
        {
            CreateEvent(new TestEventCreated("Item 3"), DateTime.UtcNow.AddSeconds(-8))
        };
        await actor.AddEventsAsync(batch2, finishedCatchUp: false);
        
        var state2 = await actor.GetStateAsync();
        Assert.False(state2.GetValue().IsCatchedUp);
        
        // Phase 3: Final batch, catching up complete
        var batch3 = new[]
        {
            CreateEvent(new TestEventCreated("Item 4"), DateTime.UtcNow.AddSeconds(-6))
        };
        await actor.AddEventsAsync(batch3, finishedCatchUp: true);
        
        // Act & Assert
        var finalState = await actor.GetStateAsync();
        Assert.True(finalState.GetValue().IsCatchedUp);
        
        var payload = finalState.GetValue().Payload as TestMultiProjector;
        Assert.Equal(4, payload!.Items.Count);
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
    public record TestMultiProjector : IMultiProjector<TestMultiProjector>, IMultiProjectionPayload
    {
        public const string MultiProjectorName = "TestMultiProjector";
        
        public List<string> Items { get; init; }
        
        public TestMultiProjector() : this(new List<string>()) { }
        
        public TestMultiProjector(List<string> items)
        {
            Items = items;
        }
        
        public static TestMultiProjector GenerateInitialPayload() => new(new List<string>());
        
        public static string GetMultiProjectorName() => MultiProjectorName;
        
        public string GetVersion() => "1.0.0";
        
        public ResultBox<TestMultiProjector> Project(TestMultiProjector payload, Event ev)
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