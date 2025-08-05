using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using DcbLib;
using DcbLib.Actors;
using DcbLib.Commands;
using DcbLib.Common;
using DcbLib.Domains;
using DcbLib.Events;
using DcbLib.InMemory;
using DcbLib.Storage;
using DcbLib.Tags;
using Domain;
using Xunit;
using ResultBoxes;

namespace Unit;

public class InMemoryCommandContextTest
{
    private readonly InMemoryEventStore _eventStore;
    private readonly InMemoryObjectAccessor _actorAccessor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly InMemoryCommandContext _commandContext;
    
    // Test event and tag types
    private record TestEvent(string Name) : IEventPayload;
    private record TestTag : ITag
    {
        public bool IsConsistencyTag() => true;
        public string GetTag() => "TestGroup:Test123";
        public string GetTagGroup() => "TestGroup";
    }
    
    private record TestTag2 : ITag
    {
        public bool IsConsistencyTag() => true;
        public string GetTag() => "TestGroup2:Test456";
        public string GetTagGroup() => "TestGroup2";
    }
    
    private class TestProjector : ITagProjector
    {
        public string GetProjectorName() => "TestProjector";
        public string GetTagProjectorName() => "TestProjector";
        public ITagStatePayload Project(ITagStatePayload current, IEventPayload eventPayload) => current;
    }
    
    private class TestProjector2 : ITagProjector
    {
        public string GetProjectorName() => "TestProjector2";
        public string GetTagProjectorName() => "TestProjector2";
        public ITagStatePayload Project(ITagStatePayload current, IEventPayload eventPayload) => current;
    }
    
    private record TestStatePayload(string State, int Count) : ITagStatePayload;
    
    public InMemoryCommandContextTest()
    {
        _eventStore = new InMemoryEventStore();
        _domainTypes = CreateTestDomainTypes();
        _actorAccessor = new InMemoryObjectAccessor(_eventStore, _domainTypes);
        _commandContext = new InMemoryCommandContext(_actorAccessor, _domainTypes);
    }
    
    private DcbDomainTypes CreateTestDomainTypes()
    {
        // Create test-specific type managers
        var eventTypes = new SimpleEventTypes();
        
        var tagTypes = new SimpleTagTypes();
        
        var tagProjectorTypes = new SimpleTagProjectorTypes();
        tagProjectorTypes.RegisterProjector(new TestProjector());
        tagProjectorTypes.RegisterProjector(new TestProjector2());
        
        var commandTypes = new SimpleCommandTypes();
        
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        tagStatePayloadTypes.RegisterPayloadType<TestStatePayload>(nameof(TestStatePayload));
        
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        
        return new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            commandTypes,
            tagStatePayloadTypes,
            jsonOptions
        );
    }
    
    [Fact]
    public async Task GetStateAsync_WithNoEvents_ReturnsEmptyState()
    {
        // Arrange
        var tag = new TestTag();
        
        // Act
        var result = await _commandContext.GetStateAsync<TestProjector>(tag);
        
        // Assert
        Assert.True(result.IsSuccess);
        var state = result.GetValue();
        Assert.IsType<EmptyTagStatePayload>(state.Payload);
        Assert.Equal(0, state.Version);
        Assert.Equal("TestGroup", state.TagGroup);
        Assert.Equal("TestProjector", state.TagProjector);
    }
    
    [Fact]
    public async Task GetStateAsync_Typed_WithNoEvents_ReturnsError()
    {
        // Arrange
        var tag = new TestTag();
        
        // Act
        var result = await _commandContext.GetStateAsync<TestStatePayload, TestProjector>(tag);
        
        // Assert
        // When there are no events and we request a specific type, it should fail
        // because EmptyTagStatePayload cannot be cast to TestStatePayload
        Assert.False(result.IsSuccess);
        var exception = result.GetException();
        Assert.IsType<InvalidCastException>(exception);
        Assert.Contains("TestStatePayload", exception.Message);
        Assert.Contains("EmptyTagStatePayload", exception.Message);
    }
    
    [Fact]
    public async Task TagExistsAsync_WithNoEvents_ReturnsFalse()
    {
        // Arrange
        var tag = new TestTag();
        
        // Act
        var exists = await _commandContext.TagExistsAsync(tag);
        
        // Assert
        Assert.False(exists);
    }
    
    [Fact]
    public async Task TagExistsAsync_WithEvents_ReturnsTrue()
    {
        // Arrange
        var tag = new TestTag();
        
        // Add an event to the store
        await _eventStore.WriteEventAsync(
            EventTestHelper.CreateEvent(new TestEvent("Test"), tag)
        );
        
        // Create and catchup TagConsistentActor
        var tagConsistentActorId = tag.GetTag();
        await _actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);
        
        // Act
        var exists = await _commandContext.TagExistsAsync(tag);
        
        // Assert
        Assert.True(exists);
    }
    
    [Fact]
    public async Task GetTagLatestSortableUniqueIdAsync_WithNoEvents_ReturnsEmptyString()
    {
        // Arrange
        var tag = new TestTag();
        
        // Act
        var latestId = await _commandContext.GetTagLatestSortableUniqueIdAsync(tag);
        
        // Assert
        Assert.Equal("", latestId);
    }
    
    [Fact]
    public async Task GetTagLatestSortableUniqueIdAsync_WithEvents_ReturnsLatestId()
    {
        // Arrange
        var tag = new TestTag();
        
        // Add an event to the store
        var testEvent = EventTestHelper.CreateEvent(new TestEvent("Test"), tag);
        var sortableUniqueId = testEvent.SortableUniqueIdValue;
        await _eventStore.WriteEventAsync(testEvent);
        
        // Get TagConsistentActor (this will trigger catchup)
        var tagConsistentActorId = tag.GetTag();
        await _actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);
        
        // Act
        var latestId = await _commandContext.GetTagLatestSortableUniqueIdAsync(tag);
        
        // Assert
        Assert.Equal(sortableUniqueId, latestId);
    }
    
    [Fact]
    public async Task GetStateAsync_WithCustomStatePayload_DeserializesCorrectly()
    {
        // Arrange
        var tag = new TestTag();
        var projector = new TestProjector();
        var tagStateId = new TagStateId(tag, projector);
        
        // Create a custom state payload
        var testPayload = new TestStatePayload("Active", 5);
        var payloadBytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(testPayload, _domainTypes.JsonSerializerOptions)
        );
        
        // Create SerializableTagState
        var serializableState = new SerializableTagState(
            payloadBytes,
            1,
            "test-sortable-id",
            "TestGroup",
            "Test123",
            "TestProjector",
            nameof(TestStatePayload)
        );
        
        // Create mock TagStateActor that returns the serializable state
        var mockActor = new MockTagStateActor(serializableState);
        
        // Register the mock actor directly with InMemoryObjectAccessor's internal dictionary
        var actorsField = _actorAccessor.GetType().GetField("_actors", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(actorsField);
        
        var actors = actorsField!.GetValue(_actorAccessor) as ConcurrentDictionary<string, object>;
        Assert.NotNull(actors);
        
        actors![tagStateId.GetTagStateId()] = mockActor;
        
        // Act
        var result = await _commandContext.GetStateAsync<TestStatePayload, TestProjector>(tag);
        
        // Assert
        Assert.True(result.IsSuccess);
        var state = result.GetValue();
        Assert.IsType<TestStatePayload>(state.Payload);
        var typedPayload = (TestStatePayload)state.Payload;
        Assert.Equal("Active", typedPayload.State);
        Assert.Equal(5, typedPayload.Count);
        Assert.Equal(1, state.Version);
    }
    
    [Fact]
    public void AppendEvent_AddsEventToContext()
    {
        // Arrange
        var tag = new TestTag();
        var eventPayload = new TestEvent("Test");
        var eventWithTags = new EventPayloadWithTags(eventPayload, new List<ITag> { tag });
        
        // Act
        var result = _commandContext.AppendEvent(eventWithTags);
        
        // Assert
        Assert.True(result.IsSuccess);
        var appendedEvents = _commandContext.GetAppendedEvents();
        Assert.Single(appendedEvents);
        Assert.Equal(eventWithTags, appendedEvents[0]);
    }
    
    [Fact]
    public void AppendEvent_WithNull_ReturnsError()
    {
        // Act
        var result = _commandContext.AppendEvent(null!);
        
        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ArgumentNullException>(result.GetException());
    }
    
    [Fact]
    public void ClearAppendedEvents_RemovesAllEvents()
    {
        // Arrange
        var tag = new TestTag();
        var eventPayload = new TestEvent("Test");
        var eventWithTags = new EventPayloadWithTags(eventPayload, new List<ITag> { tag });
        _commandContext.AppendEvent(eventWithTags);
        
        // Act
        #pragma warning disable CS0618 // Testing deprecated method
        _commandContext.ClearAppendedEvents();
        #pragma warning restore CS0618
        
        // Assert
        var appendedEvents = _commandContext.GetAppendedEvents();
        Assert.Empty(appendedEvents);
    }
    
    [Fact]
    public async Task GetAccessedTagStates_TracksAccessedStates()
    {
        // Arrange
        var tag = new TestTag();
        var testEvent = EventTestHelper.CreateEvent(new TestEvent("Test"), tag);
        var sortableUniqueId = testEvent.SortableUniqueIdValue;
        await _eventStore.WriteEventAsync(testEvent);
        
        // Act - Access state (only GetStateAsync tracks the full state)
        await _commandContext.GetStateAsync<TestProjector>(tag);
        
        // Assert
        var accessedStates = _commandContext.GetAccessedTagStates();
        Assert.Single(accessedStates);
        Assert.Contains(tag, accessedStates.Keys);
        var tagState = accessedStates[tag];
        Assert.Equal(sortableUniqueId, tagState.LastSortedUniqueId);
        Assert.Equal(1, tagState.Version); // Should have version 1 after one event
        Assert.NotNull(tagState.Payload);
    }
    
    [Fact]
    public void GetAccessedTagStates_EmptyWhenNoStatesAccessed()
    {
        // Arrange - Do nothing
        
        // Act
        var accessedStates = _commandContext.GetAccessedTagStates();
        
        // Assert
        Assert.Empty(accessedStates);
    }
    
    [Fact]
    public async Task ClearResults_ClearsAccessedStatesAndAppendedEvents()
    {
        // Arrange
        var tag = new TestTag();
        
        // Add an event to store and access state
        var testEvent = EventTestHelper.CreateEvent(new TestEvent("Test"), tag);
        await _eventStore.WriteEventAsync(testEvent);
        await _commandContext.GetStateAsync<TestProjector>(tag);
        
        // Append an event
        var eventPayload = new TestEvent("Test2");
        var eventWithTags = new EventPayloadWithTags(eventPayload, new List<ITag> { tag });
        _commandContext.AppendEvent(eventWithTags);
        
        // Act
        _commandContext.ClearResults();
        
        // Assert
        var accessedStates = _commandContext.GetAccessedTagStates();
        var appendedEvents = _commandContext.GetAppendedEvents();
        Assert.Empty(accessedStates);
        Assert.Empty(appendedEvents);
    }
    
    [Fact]
    public async Task GetAccessedTagStates_TracksMultipleTags()
    {
        // Arrange
        var tag1 = new TestTag();
        var tag2 = new TestTag2();
        
        // Add events for both tags
        var testEvent1 = EventTestHelper.CreateEvent(new TestEvent("Test1"), tag1);
        var testEvent2 = EventTestHelper.CreateEvent(new TestEvent("Test2"), tag2);
        await _eventStore.WriteEventAsync(testEvent1);
        await _eventStore.WriteEventAsync(testEvent2);
        
        // Act - Access states for both tags
        await _commandContext.GetStateAsync<TestProjector>(tag1);
        await _commandContext.GetStateAsync<TestProjector2>(tag2);
        
        // Assert
        var accessedStates = _commandContext.GetAccessedTagStates();
        Assert.Equal(2, accessedStates.Count);
        Assert.Contains(tag1, accessedStates.Keys);
        Assert.Contains(tag2, accessedStates.Keys);
        Assert.Equal(testEvent1.SortableUniqueIdValue, accessedStates[tag1].LastSortedUniqueId);
        Assert.Equal(testEvent2.SortableUniqueIdValue, accessedStates[tag2].LastSortedUniqueId);
        Assert.Equal(1, accessedStates[tag1].Version);
        Assert.Equal(1, accessedStates[tag2].Version);
    }
    
    // Mock implementation for testing
    private class MockTagStateActor : ITagStateActorCommon
    {
        private readonly SerializableTagState _state;
        
        public MockTagStateActor(SerializableTagState state)
        {
            _state = state;
        }
        
        public Task<SerializableTagState> GetStateAsync() => Task.FromResult(_state);
        public Task UpdateStateAsync(string lastSortableUniqueId) => Task.CompletedTask;
        public Task<string> GetTagStateActorIdAsync() => Task.FromResult("test-actor-id");
    }
}