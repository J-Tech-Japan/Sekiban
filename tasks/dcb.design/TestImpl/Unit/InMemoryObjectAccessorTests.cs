using DcbLib;
using DcbLib.Actors;
using DcbLib.Common;
using DcbLib.InMemory;
using DcbLib.Tags;
using Domain;
using ResultBoxes;
using Xunit;

namespace Unit;

/// <summary>
/// Tests for InMemoryObjectAccessor
/// </summary>
public class InMemoryObjectAccessorTests
{
    private readonly InMemoryEventStore _eventStore;
    private readonly DcbDomainTypes _domainTypes;
    private readonly InMemoryObjectAccessor _accessor;
    
    public InMemoryObjectAccessorTests()
    {
        _eventStore = new InMemoryEventStore();
        _domainTypes = DomainType.GetDomainTypes();
        _accessor = new InMemoryObjectAccessor(_eventStore, _domainTypes);
    }
    
    [Fact]
    public async Task GetActorAsync_Should_Create_TagConsistentActor()
    {
        // Arrange
        var actorId = "Student:12345";
        
        // Act
        var result = await _accessor.GetActorAsync<ITagConsistentActorCommon>(actorId);
        
        // Assert
        Assert.True(result.IsSuccess);
        var actor = result.GetValue();
        Assert.NotNull(actor);
        Assert.Equal(actorId, await actor.GetTagActorIdAsync());
    }
    
    [Fact]
    public async Task GetActorAsync_Should_Create_TagStateActor()
    {
        // Arrange
        var actorId = "Student:12345:StudentProjector";
        
        // Act
        var result = await _accessor.GetActorAsync<ITagStateActorCommon>(actorId);
        
        // Assert
        Assert.True(result.IsSuccess);
        var actor = result.GetValue();
        Assert.NotNull(actor);
        Assert.Equal(actorId, await actor.GetTagStateActorIdAsync());
    }
    
    [Fact]
    public async Task GetActorAsync_Should_Return_Same_Actor_Instance()
    {
        // Arrange
        var actorId = "Student:12345";
        
        // Act
        var result1 = await _accessor.GetActorAsync<ITagConsistentActorCommon>(actorId);
        var result2 = await _accessor.GetActorAsync<ITagConsistentActorCommon>(actorId);
        
        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        var actor1 = result1.GetValue();
        var actor2 = result2.GetValue();
        Assert.Same(actor1, actor2);
    }
    
    [Fact]
    public async Task GetActorAsync_Should_Return_Error_For_Invalid_Type()
    {
        // Arrange
        var actorId = "Student:12345";
        
        // Act
        var result = await _accessor.GetActorAsync<ITagStateActorCommon>(actorId);
        
        // Assert
        Assert.False(result.IsSuccess);
    }
    
    [Fact]
    public async Task GetActorAsync_Should_Return_Error_For_Empty_ActorId()
    {
        // Act
        var result1 = await _accessor.GetActorAsync<ITagConsistentActorCommon>("");
        var result2 = await _accessor.GetActorAsync<ITagConsistentActorCommon>(null!);
        var result3 = await _accessor.GetActorAsync<ITagConsistentActorCommon>("   ");
        
        // Assert
        Assert.False(result1.IsSuccess);
        Assert.False(result2.IsSuccess);
        Assert.False(result3.IsSuccess);
    }
    
    [Fact]
    public async Task ActorExistsAsync_Should_Return_True_For_Existing_Actor()
    {
        // Arrange
        var actorId = "Student:12345";
        await _accessor.GetActorAsync<ITagConsistentActorCommon>(actorId);
        
        // Act
        var exists = await _accessor.ActorExistsAsync(actorId);
        
        // Assert
        Assert.True(exists);
    }
    
    [Fact]
    public async Task ActorExistsAsync_Should_Return_False_For_NonExistent_Actor()
    {
        // Act
        var exists = await _accessor.ActorExistsAsync("Student:99999");
        
        // Assert
        Assert.False(exists);
    }
    
    [Fact]
    public async Task ClearAllActors_Should_Remove_All_Actors()
    {
        // Arrange
        await _accessor.GetActorAsync<ITagConsistentActorCommon>("Student:12345");
        await _accessor.GetActorAsync<ITagStateActorCommon>("Student:12345:StudentProjector");
        Assert.Equal(2, _accessor.ActorCount);
        
        // Act
        _accessor.ClearAllActors();
        
        // Assert
        Assert.Equal(0, _accessor.ActorCount);
    }
    
    [Fact]
    public async Task RemoveActor_Should_Remove_Specific_Actor()
    {
        // Arrange
        var actorId1 = "Student:12345";
        var actorId2 = "Student:67890";
        await _accessor.GetActorAsync<ITagConsistentActorCommon>(actorId1);
        await _accessor.GetActorAsync<ITagConsistentActorCommon>(actorId2);
        
        // Act
        var removed = _accessor.RemoveActor(actorId1);
        
        // Assert
        Assert.True(removed);
        Assert.Equal(1, _accessor.ActorCount);
        Assert.False(await _accessor.ActorExistsAsync(actorId1));
        Assert.True(await _accessor.ActorExistsAsync(actorId2));
    }
    
    [Fact]
    public async Task TagStateActor_Should_Get_Latest_SortableUniqueId_From_TagConsistentActor()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagConsistentActorId = $"Student:{studentId}";
        var tagStateActorId = $"Student:{studentId}:StudentProjector";
        
        // Create some events
        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "John Doe", 5), studentTag);
        await _eventStore.WriteEventAsync(event1);
        
        // Get TagConsistentActor and make a reservation with a sortable unique ID
        var tagConsistentActorResult = await _accessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);
        Assert.True(tagConsistentActorResult.IsSuccess);
        var tagConsistentActor = tagConsistentActorResult.GetValue();
        
        // Make a reservation with the event's sortable unique ID
        var reservationResult = await tagConsistentActor.MakeReservationAsync(event1.SortableUniqueIdValue);
        Assert.True(reservationResult.IsSuccess);
        
        // Add another event (this should not be included in the state)
        var event2 = EventTestHelper.CreateEvent(
            new StudentEnrolledInClassRoom(studentId, Guid.NewGuid()), 
            studentTag);
        await _eventStore.WriteEventAsync(event2);
        
        // Get TagStateActor
        var tagStateActorResult = await _accessor.GetActorAsync<ITagStateActorCommon>(tagStateActorId);
        Assert.True(tagStateActorResult.IsSuccess);
        var tagStateActor = tagStateActorResult.GetValue();
        
        // Get state
        var state = await tagStateActor.GetStateAsync();
        
        // Assert - should only have processed the first event
        Assert.Equal(1, state.Version);
        Assert.Equal(event1.SortableUniqueIdValue, state.LastSortedUniqueId);
        
        // Verify the payload
        Assert.NotEmpty(state.Payload);
        Assert.Equal("StudentState", state.TagPayloadName);
        
        // Note: We can't directly check the StudentState properties from SerializableTagState
        // but we've verified the version is 1, meaning only the first event was processed
    }
    
    [Fact]
    public async Task Multiple_Actors_Can_Be_Created_Concurrently()
    {
        // Arrange
        var actorIds = Enumerable.Range(1, 10).Select(i => $"Student:{i}").ToList();
        
        // Act
        var tasks = actorIds.Select(id => 
            _accessor.GetActorAsync<ITagConsistentActorCommon>(id)).ToList();
        var results = await Task.WhenAll(tasks);
        
        // Assert
        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Equal(10, _accessor.ActorCount);
        
        // Verify all actors are unique
        var actors = results.Select(r => r.GetValue()).ToList();
        var uniqueActors = actors.Distinct().Count();
        Assert.Equal(10, uniqueActors);
    }
}