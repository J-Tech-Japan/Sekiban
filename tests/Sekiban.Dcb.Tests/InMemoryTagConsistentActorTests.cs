using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Dcb.Domain;
using Xunit;

namespace Sekiban.Dcb.Tests;

/// <summary>
/// Tests for InMemoryTagConsistentActor with catch-up functionality
/// </summary>
public class InMemoryTagConsistentActorTests
{
    private readonly InMemoryEventStore _eventStore;
    private readonly DcbDomainTypes _domainTypes;
    
    public InMemoryTagConsistentActorTests()
    {
        _eventStore = new InMemoryEventStore();
        _domainTypes = DomainType.GetDomainTypes();
    }
    
    [Fact]
    public async Task TagConsistentActor_Should_CatchUp_From_EventStore()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagName = studentTag.GetTag();
        
        // Write some events to create tag state
        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "John Doe", 5), studentTag);
        await _eventStore.WriteEventAsync(event1);
        
        var event2 = EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, Guid.NewGuid()), studentTag);
        await _eventStore.WriteEventAsync(event2);
        
        // Create actor (it should catch up lazily)
        var actor = new InMemoryTagConsistentActor(tagName, _eventStore);
        
        // Act - Get latest sortable unique ID (should trigger catch-up)
        var latestSortableUniqueId = await actor.GetLatestSortableUniqueIdAsync();
        
        // Assert
        Assert.Equal(event2.SortableUniqueIdValue, latestSortableUniqueId);
    }
    
    [Fact]
    public async Task TagConsistentActor_Should_Handle_No_Existing_State()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagName = studentTag.GetTag();
        
        // Create actor without any existing state
        var actor = new InMemoryTagConsistentActor(tagName, _eventStore);
        
        // Act
        var latestSortableUniqueId = await actor.GetLatestSortableUniqueIdAsync();
        
        // Assert
        Assert.Equal("", latestSortableUniqueId);
    }
    
    [Fact]
    public async Task TagConsistentActor_Should_Update_Latest_SortableUniqueId_On_Reservation()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagName = studentTag.GetTag();
        var actor = new InMemoryTagConsistentActor(tagName, _eventStore);
        
        // Act
        var newSortableUniqueId = SortableUniqueId.GenerateNew();
        var reservationResult = await actor.MakeReservationAsync(newSortableUniqueId);
        
        // Assert
        Assert.True(reservationResult.IsSuccess);
        Assert.Equal(newSortableUniqueId, await actor.GetLatestSortableUniqueIdAsync());
    }
    
    [Fact]
    public async Task TagConsistentActor_Without_EventStore_Should_Work()
    {
        // Arrange
        var tagName = "Student:12345";
        var actor = new InMemoryTagConsistentActor(tagName);
        
        // Act
        var latestSortableUniqueId = await actor.GetLatestSortableUniqueIdAsync();
        var reservationResult = await actor.MakeReservationAsync("");
        
        // Assert
        Assert.Equal("", latestSortableUniqueId);
        Assert.True(reservationResult.IsSuccess);
    }
    
    [Fact]
    public async Task TagConsistentActor_Should_Only_CatchUp_Once()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagName = studentTag.GetTag();
        
        // Write initial event
        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "John Doe", 5), studentTag);
        await _eventStore.WriteEventAsync(event1);
        
        // Create actor and trigger catch-up
        var actor = new InMemoryTagConsistentActor(tagName, _eventStore);
        var firstId = await actor.GetLatestSortableUniqueIdAsync();
        
        // Write another event after actor creation
        var event2 = EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, Guid.NewGuid()), studentTag);
        await _eventStore.WriteEventAsync(event2);
        
        // Act - Get latest ID again (should not catch up again)
        var secondId = await actor.GetLatestSortableUniqueIdAsync();
        
        // Assert - Should still have the first event's ID
        Assert.Equal(event1.SortableUniqueIdValue, firstId);
        Assert.Equal(firstId, secondId);
    }
    
    [Fact]
    public async Task TagConsistentActor_Should_Preserve_State_After_CatchUp()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagName = studentTag.GetTag();
        
        // Write event
        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "John Doe", 5), studentTag);
        await _eventStore.WriteEventAsync(event1);
        
        // Create actor - it will catch up from event store
        var actor = new InMemoryTagConsistentActor(tagName, _eventStore);
        
        // Act - Get the latest ID after catch up
        var latestId = await actor.GetLatestSortableUniqueIdAsync();
        
        // Assert - Should have the event's ID after catch up
        Assert.Equal(event1.SortableUniqueIdValue, latestId);
    }
    
    [Fact]
    public async Task All_Methods_Should_Trigger_CatchUp()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagName = studentTag.GetTag();
        
        // Write event
        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "John Doe", 5), studentTag);
        await _eventStore.WriteEventAsync(event1);
        
        // Test each method triggers catch-up
        
        // Test GetLatestSortableUniqueId
        var actor1 = new InMemoryTagConsistentActor(tagName, _eventStore);
        Assert.Equal(event1.SortableUniqueIdValue, await actor1.GetLatestSortableUniqueIdAsync());
        
        // Test MakeReservation
        var actor2 = new InMemoryTagConsistentActor(tagName, _eventStore);
        var reservation = await actor2.MakeReservationAsync("");
        Assert.True(reservation.IsSuccess);
        Assert.Equal(event1.SortableUniqueIdValue, await actor2.GetLatestSortableUniqueIdAsync());
        
        // Test ConfirmReservation
        var actor3 = new InMemoryTagConsistentActor(tagName, _eventStore);
        await actor3.ConfirmReservationAsync(null!);
        Assert.Equal(event1.SortableUniqueIdValue, await actor3.GetLatestSortableUniqueIdAsync());
        
        // Test CancelReservation
        var actor4 = new InMemoryTagConsistentActor(tagName, _eventStore);
        await actor4.CancelReservationAsync(null!);
        Assert.Equal(event1.SortableUniqueIdValue, await actor4.GetLatestSortableUniqueIdAsync());
        
        // Test GetActiveReservations
        var actor5 = new InMemoryTagConsistentActor(tagName, _eventStore);
        await actor5.GetActiveReservationsAsync();
        Assert.Equal(event1.SortableUniqueIdValue, await actor5.GetLatestSortableUniqueIdAsync());
    }
}