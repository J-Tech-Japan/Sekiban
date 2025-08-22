using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text.Json;
using Xunit;

namespace Sekiban.Dcb.Tests;

/// <summary>
/// Tests for GeneralTagStateActor incremental update functionality
/// </summary>
public class GeneralTagStateActorIncrementalTests
{
    private readonly IEventStore _eventStore;
    private readonly DcbDomainTypes _domainTypes;
    private readonly IActorObjectAccessor _actorAccessor;
    private readonly ITagStatePersistent _statePersistent;

    public GeneralTagStateActorIncrementalTests()
    {
        _eventStore = new InMemoryEventStore();
        
        // Setup domain types
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<TestEvent>();
        eventTypes.RegisterEventType<IncrementEvent>();
        
        var tagTypes = new SimpleTagTypes();
        var tagProjectorTypes = new SimpleTagProjectorTypes();
        tagProjectorTypes.RegisterProjector<TestIncrementalProjector>();
        tagProjectorTypes.RegisterProjector<TestProjectorVersionTwo>();
        
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        tagStatePayloadTypes.RegisterPayloadType<TestIncrementalState>();
        
        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        var queryTypes = new SimpleQueryTypes();
        
        _domainTypes = new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            multiProjectorTypes,
            queryTypes,
            new JsonSerializerOptions());
        
        _actorAccessor = new TestActorAccessor();
        _statePersistent = new InMemoryTagStatePersistent();
    }

    [Fact]
    public async Task Should_Use_Incremental_Update_When_Cache_Valid()
    {
        // Arrange
        var tagStateId = "TestTag:123:TestIncrementalProjector";
        var actor = new GeneralTagStateActor(
            tagStateId,
            _eventStore,
            _domainTypes,
            new TagStateOptions(),
            _actorAccessor,
            _statePersistent);

        // Add initial events
        var tag = new TestTag("123");
        var event1 = CreateEvent(new TestEvent { Value = 10 }, tag, "001");
        var event2 = CreateEvent(new IncrementEvent { Increment = 5 }, tag, "002");
        
        await _eventStore.WriteEventsAsync(new[] { event1, event2 });

        // Set up tag consistent actor to return sortable unique ID
        var tagConsistentActor = await _actorAccessor.GetActorAsync<ITagConsistentActorCommon>("TestTag:123");
        if (tagConsistentActor.IsSuccess)
        {
            // First state computation
            var state1 = await actor.GetTagStateAsync();
            Assert.NotNull(state1);
            var payload1 = state1.Payload as TestIncrementalState;
            Assert.NotNull(payload1);
            Assert.Equal(15, payload1.Total); // 10 + 5
            Assert.Equal(2, state1.Version);
            
            // Add more events
            var event3 = CreateEvent(new IncrementEvent { Increment = 7 }, tag, "003");
            var event4 = CreateEvent(new IncrementEvent { Increment = 3 }, tag, "004");
            await _eventStore.WriteEventsAsync(new[] { event3, event4 });
            
            // Second state computation should use incremental update
            var state2 = await actor.GetTagStateAsync();
            Assert.NotNull(state2);
            var payload2 = state2.Payload as TestIncrementalState;
            Assert.NotNull(payload2);
            Assert.Equal(25, payload2.Total); // 15 + 7 + 3 (incremental)
            Assert.Equal(4, state2.Version);
            Assert.Equal("004", state2.LastSortedUniqueId);
        }
    }

    [Fact]
    public async Task Should_Rebuild_When_Projector_Version_Changes()
    {
        // Arrange
        var tagStateId = "TestTag:456:TestIncrementalProjector";
        var actor = new GeneralTagStateActor(
            tagStateId,
            _eventStore,
            _domainTypes,
            new TagStateOptions(),
            _actorAccessor,
            _statePersistent);

        // Add events
        var tag = new TestTag("456");
        var event1 = CreateEvent(new TestEvent { Value = 20 }, tag, "010");
        var event2 = CreateEvent(new IncrementEvent { Increment = 10 }, tag, "011");
        
        await _eventStore.WriteEventsAsync(new[] { event1, event2 });

        // First state computation
        var state1 = await actor.GetTagStateAsync();
        Assert.NotNull(state1);
        Assert.Equal("1.0", state1.ProjectorVersion);
        
        // Simulate projector version change by using different projector
        var newTagStateId = "TestTag:456:TestProjectorVersionTwo";
        var newActor = new GeneralTagStateActor(
            newTagStateId,
            _eventStore,
            _domainTypes,
            new TagStateOptions(),
            _actorAccessor,
            new InMemoryTagStatePersistent()); // New cache
        
        // Should rebuild from scratch with new projector
        var state2 = await newActor.GetTagStateAsync();
        Assert.NotNull(state2);
        Assert.Equal("2.0", state2.ProjectorVersion);
        // V2 doubles the values
        var payload2 = state2.Payload as TestIncrementalState;
        Assert.NotNull(payload2);
        Assert.Equal(60, payload2.Total); // (20 + 10) * 2
    }

    [Fact]
    public async Task Should_Return_Cached_State_When_No_New_Events()
    {
        // Arrange
        var tagStateId = "TestTag:789:TestIncrementalProjector";
        var actor = new GeneralTagStateActor(
            tagStateId,
            _eventStore,
            _domainTypes,
            new TagStateOptions(),
            _actorAccessor,
            _statePersistent);

        // Add events
        var tag = new TestTag("789");
        var event1 = CreateEvent(new TestEvent { Value = 100 }, tag, "020");
        
        await _eventStore.WriteEventsAsync(new[] { event1 });

        // First call - computes state
        var state1 = await actor.GetTagStateAsync();
        Assert.NotNull(state1);
        var payload1 = state1.Payload as TestIncrementalState;
        Assert.NotNull(payload1);
        Assert.Equal(100, payload1.Total);
        
        // Second call without new events - should return cached
        var state2 = await actor.GetTagStateAsync();
        Assert.NotNull(state2);
        Assert.Same(state1.Payload, state2.Payload); // Should be the same instance from cache
        Assert.Equal(state1.Version, state2.Version);
        Assert.Equal(state1.LastSortedUniqueId, state2.LastSortedUniqueId);
    }

    [Fact]
    public async Task Should_Handle_Empty_State_Correctly()
    {
        // Arrange
        var tagStateId = "TestTag:999:TestIncrementalProjector";
        var actor = new GeneralTagStateActor(
            tagStateId,
            _eventStore,
            _domainTypes,
            new TagStateOptions(),
            _actorAccessor,
            _statePersistent);

        // No events added
        
        // Should return empty state
        var state = await actor.GetTagStateAsync();
        Assert.NotNull(state);
        Assert.IsType<EmptyTagStatePayload>(state.Payload);
        Assert.Equal(0, state.Version);
        Assert.Equal(string.Empty, state.LastSortedUniqueId);
    }

    private Event CreateEvent(IEventPayload payload, ITag tag, string sortableId)
    {
        var eventId = Guid.NewGuid();
        return new Event(
            payload,
            new SortableUniqueId(sortableId),
            "TestAggregate",
            eventId,
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
            new List<string> { tag.GetTag() });
    }

    // Test event types
    private record TestEvent : IEventPayload
    {
        public int Value { get; init; }
    }

    private record IncrementEvent : IEventPayload
    {
        public int Increment { get; init; }
    }

    // Test tag
    private record TestTag : ITag
    {
        private readonly string _id;
        public TestTag(string id) => _id = id;
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => "TestTag";
        public string GetTagContent() => _id;
        public string GetTag() => $"TestTag:{_id}";
    }

    // Test projector
    private class TestIncrementalProjector : ITagProjector<TestIncrementalProjector>
    {
        public static string ProjectorVersion => "1.0";
        public static string ProjectorName => "TestIncrementalProjector";
        
        public static ITagStatePayload Project(ITagStatePayload current, Event ev)
        {
            var state = current as TestIncrementalState ?? new TestIncrementalState();
            
            return ev.Payload switch
            {
                TestEvent test => state with { Total = state.Total + test.Value },
                IncrementEvent inc => state with { Total = state.Total + inc.Increment },
                _ => state
            };
        }
    }

    // Test projector V2 (different version)
    private class TestProjectorVersionTwo : ITagProjector<TestProjectorVersionTwo>
    {
        public static string ProjectorVersion => "2.0";
        public static string ProjectorName => "TestProjectorVersionTwo";
        
        public static ITagStatePayload Project(ITagStatePayload current, Event ev)
        {
            var state = current as TestIncrementalState ?? new TestIncrementalState();
            
            // V2 doubles all values
            return ev.Payload switch
            {
                TestEvent test => state with { Total = state.Total + (test.Value * 2) },
                IncrementEvent inc => state with { Total = state.Total + (inc.Increment * 2) },
                _ => state
            };
        }
    }

    // Test state
    private record TestIncrementalState : ITagStatePayload
    {
        public int Total { get; init; }
    }

    // Test actor accessor that returns TagConsistentActor instances
    private class TestActorAccessor : IActorObjectAccessor
    {
        private readonly Dictionary<string, ITagConsistentActorCommon> _actors = new();

        public Task<ResultBox<T>> GetActorAsync<T>(string actorId) where T : class
        {
            if (typeof(T) == typeof(ITagConsistentActorCommon))
            {
                if (!_actors.ContainsKey(actorId))
                {
                    // Create a simple mock actor that returns the last sortable ID
                    var mockActor = new MockTagConsistentActor();
                    mockActor.SetActorId(actorId);
                    
                    // Set appropriate sortable unique ID based on the test scenario
                    if (actorId == "TestTag:123")
                    {
                        mockActor.SetLastSortableUniqueId("004");
                    }
                    else if (actorId == "TestTag:456")
                    {
                        mockActor.SetLastSortableUniqueId("011");
                    }
                    else if (actorId == "TestTag:789")
                    {
                        mockActor.SetLastSortableUniqueId("020");
                    }
                    else if (actorId == "TestTag:999")
                    {
                        mockActor.SetLastSortableUniqueId("");
                    }
                    
                    _actors[actorId] = mockActor;
                }
                
                if (_actors[actorId] is T actor)
                {
                    return Task.FromResult(ResultBox.FromValue(actor));
                }
            }

            return Task.FromResult(ResultBox.Error<T>(new NotSupportedException()));
        }

        public Task<bool> ActorExistsAsync(string actorId) => Task.FromResult(true);
    }

    // Mock TagConsistentActor for testing
    private class MockTagConsistentActor : ITagConsistentActorCommon
    {
        private string _lastSortableUniqueId = "";
        private string _actorId = "";

        public Task<string> GetTagActorIdAsync() => Task.FromResult(_actorId);

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => 
            Task.FromResult(ResultBox.FromValue(_lastSortableUniqueId));

        public Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string lastSortableUniqueId) =>
            Task.FromResult(ResultBox.FromValue(new TagWriteReservation(
                Guid.NewGuid().ToString(), 
                DateTime.UtcNow.AddMinutes(1).ToString("O"),
                _actorId)));

        public Task<bool> ConfirmReservationAsync(TagWriteReservation reservation) => Task.FromResult(true);

        public Task<bool> CancelReservationAsync(TagWriteReservation reservation) => Task.FromResult(true);

        public void SetLastSortableUniqueId(string sortableUniqueId) => _lastSortableUniqueId = sortableUniqueId;
        
        public void SetActorId(string actorId) => _actorId = actorId;
    }
}