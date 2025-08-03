using DcbLib;
using DcbLib.Actors;
using DcbLib.Common;
using DcbLib.InMemory;
using DcbLib.Tags;
using Domain;
using Xunit;

namespace Unit;

/// <summary>
/// Tests for pure TagStateActor catch-up behavior without TagConsistentActor
/// </summary>
public class PureTagStateActorCatchUpTest
{
    private readonly InMemoryEventStore _eventStore;
    private readonly DcbDomainTypes _domainTypes;
    
    public PureTagStateActorCatchUpTest()
    {
        _eventStore = new InMemoryEventStore();
        _domainTypes = DomainType.GetDomainTypes();
    }
    
    [Fact]
    public async Task TagStateActor_Without_Accessor_Should_Read_All_Events()
    {
        // This test verifies that TagStateActor reads all events when created directly
        // without ObjectAccessor (and thus without TagConsistentActor limitation)
        
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagStateId = new TagStateId(studentTag, new StudentProjector());
        var actorId = tagStateId.GetTagStateId();
        
        // Step 1: Write events and create first actor
        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "Test Student", 5), studentTag);
        await _eventStore.WriteEventAsync(event1);
        
        var classRoomId = Guid.NewGuid();
        var event2 = EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, classRoomId), studentTag);
        await _eventStore.WriteEventAsync(event2);
        
        // Create actor directly (no ObjectAccessor, no TagConsistentActor)
        var actor1 = new InMemoryTagStateActor(actorId, _eventStore, _domainTypes);
        var state1 = await actor1.GetStateAsync();
        Assert.Equal(2, state1.Version);
        
        // Step 2: Add more event after actor creation
        var event3 = EventTestHelper.CreateEvent(new StudentDroppedFromClassRoom(studentId, classRoomId), studentTag);
        await _eventStore.WriteEventAsync(event3);
        
        // Step 3: Create new actor instance (simulating removal and recreation)
        var actor2 = new InMemoryTagStateActor(actorId, _eventStore, _domainTypes);
        var state2 = await actor2.GetStateAsync();
        
        // Should see all 3 events since there's no TagConsistentActor limiting it
        Assert.Equal(3, state2.Version);
        Assert.Equal(event3.SortableUniqueIdValue, state2.LastSortedUniqueId);
    }
    
    [Fact]
    public async Task TagStateActor_With_TagConsistentActor_Should_Be_Limited()
    {
        // This test verifies that TagStateActor respects TagConsistentActor's latest sortable unique ID
        
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagStateId = new TagStateId(studentTag, new StudentProjector());
        var actorId = tagStateId.GetTagStateId();
        var tagConsistentActorId = studentTag.GetTag();
        
        // Use ObjectAccessor to ensure TagConsistentActor interaction
        var accessor = new InMemoryObjectAccessor(_eventStore, _domainTypes);
        
        // Step 1: Write events
        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "Test Student", 5), studentTag);
        await _eventStore.WriteEventAsync(event1);
        
        var event2 = EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, Guid.NewGuid()), studentTag);
        await _eventStore.WriteEventAsync(event2);
        
        // Step 2: Create TagConsistentActor and make it aware of event2
        var tagConsistentActor = (await accessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId)).GetValue();
        await tagConsistentActor.MakeReservationAsync(event2.SortableUniqueIdValue);
        
        // Step 3: Add another event
        var event3 = EventTestHelper.CreateEvent(new StudentDroppedFromClassRoom(studentId, Guid.NewGuid()), studentTag);
        await _eventStore.WriteEventAsync(event3);
        
        // Step 4: Create TagStateActor through accessor
        var tagStateActor = (await accessor.GetActorAsync<ITagStateActorCommon>(actorId)).GetValue();
        var state = await tagStateActor.GetStateAsync();
        
        // Should only see events up to event2 because TagConsistentActor limits it
        Assert.Equal(2, state.Version);
        Assert.Equal(event2.SortableUniqueIdValue, state.LastSortedUniqueId);
    }
}