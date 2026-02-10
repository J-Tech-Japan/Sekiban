using Dcb.Domain;
using Dcb.Domain.Enrollment;
using Dcb.Domain.Student;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Storage;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for TagConsistentActor catchup behavior when accessed through TagStateActor
/// </summary>
public class TagConsistentActorCatchupTest
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly InMemoryEventStore _eventStore;

    public TagConsistentActorCatchupTest()
    {
        _eventStore = new InMemoryEventStore();
        _domainTypes = DomainType.GetDomainTypes();
    }

    [Fact]
    public async Task TagStateActor_Should_Return_Empty_State_When_TagConsistentActor_Has_No_LastSortableUniqueId()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagStateId = $"Student:{studentId}:StudentProjector";
        var tagConsistentId = $"Student:{studentId}";

        // Write multiple events
        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "Test Student"), studentTag);
        await _eventStore.WriteEventAsync(event1);

        var classRoomId1 = Guid.NewGuid();
        var event2 = EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, classRoomId1), studentTag);
        await _eventStore.WriteEventAsync(event2);

        var classRoomId2 = Guid.NewGuid();
        var event3 = EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, classRoomId2), studentTag);
        await _eventStore.WriteEventAsync(event3);

        // Create TagConsistentActor without event store (so it won't catch up)
        var tagConsistentActor = new GeneralTagConsistentActor(
            tagConsistentId,
            null,
            new TagConsistentActorOptions(),
            _domainTypes.TagTypes);
        var latestIdResult = await tagConsistentActor.GetLatestSortableUniqueIdAsync();
        Assert.True(latestIdResult.IsSuccess);
        Assert.Equal("", latestIdResult.GetValue()); // Verify it's empty

        // Create accessor that returns our TagConsistentActor
        var accessor = new TestActorAccessor(tagConsistentActor);

        // Create TagStateActor
        var tagStateActor = new GeneralTagStateActor(tagStateId, _eventStore, _domainTypes.TagProjectorTypes, _domainTypes.TagTypes, _domainTypes.TagStatePayloadTypes, accessor);

        // Act
        var state = await tagStateActor.GetStateAsync();

        // Assert - should return empty state when TagConsistentActor has no LastSortableUniqueId
        Assert.Equal(0, state.Version);
        Assert.Equal("", state.LastSortedUniqueId);
        Assert.Equal("EmptyTagStatePayload", state.TagPayloadName);
    }

    [Fact]
    public async Task TagConsistentActor_Should_Catchup_When_Created_And_Events_Exist()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagConsistentId = $"Student:{studentId}";

        // Write events before creating TagConsistentActor
        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "Test Student"), studentTag);
        await _eventStore.WriteEventAsync(event1);

        var classRoomId1 = Guid.NewGuid();
        var event2 = EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, classRoomId1), studentTag);
        await _eventStore.WriteEventAsync(event2);

        // Create accessor and get TagConsistentActor (should trigger catchup)
        var accessor = new InMemoryObjectAccessor(_eventStore, _domainTypes);
        var actorResult = await accessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentId);
        Assert.True(actorResult.IsSuccess);

        var tagConsistentActor = actorResult.GetValue();
        var latestIdResult = await tagConsistentActor.GetLatestSortableUniqueIdAsync();

        // Assert - should have caught up to the latest event
        Assert.True(latestIdResult.IsSuccess);
        Assert.Equal(event2.SortableUniqueIdValue, latestIdResult.GetValue());
    }

    [Fact]
    public async Task TagStateActor_Should_See_Events_After_TagConsistentActor_Catchup()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagStateId = $"Student:{studentId}:StudentProjector";
        var tagConsistentId = $"Student:{studentId}";

        // Write events
        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "Test Student"), studentTag);
        await _eventStore.WriteEventAsync(event1);

        var classRoomId1 = Guid.NewGuid();
        var event2 = EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, classRoomId1), studentTag);
        await _eventStore.WriteEventAsync(event2);

        // Create accessor
        var accessor = new InMemoryObjectAccessor(_eventStore, _domainTypes);

        // First access - TagConsistentActor doesn't exist yet, should create and catchup
        var tagStateActor = new GeneralTagStateActor(tagStateId, _eventStore, _domainTypes.TagProjectorTypes, _domainTypes.TagTypes, _domainTypes.TagStatePayloadTypes, accessor);
        var state1 = await tagStateActor.GetStateAsync();

        // Should see events after TagConsistentActor catches up
        Assert.Equal(2, state1.Version);
        Assert.Equal(event2.SortableUniqueIdValue, state1.LastSortedUniqueId);

        // Verify TagConsistentActor was created and caught up
        var actorResult = await accessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentId);
        Assert.True(actorResult.IsSuccess);
        var tagConsistentActor = actorResult.GetValue();
        var latestIdResult = await tagConsistentActor.GetLatestSortableUniqueIdAsync();
        Assert.True(latestIdResult.IsSuccess);
        Assert.Equal(event2.SortableUniqueIdValue, latestIdResult.GetValue());
    }

    /// <summary>
    ///     Test accessor that returns a specific TagConsistentActor instance
    /// </summary>
    private class TestActorAccessor : IActorObjectAccessor
    {
        private readonly ITagConsistentActorCommon _tagConsistentActor;

        public TestActorAccessor(ITagConsistentActorCommon tagConsistentActor) =>
            _tagConsistentActor = tagConsistentActor;

        public Task<ResultBox<T>> GetActorAsync<T>(string actorId) where T : class
        {
            if (typeof(T) == typeof(ITagConsistentActorCommon) && _tagConsistentActor is T actor)
            {
                return Task.FromResult(ResultBox.FromValue(actor));
            }

            return Task.FromResult(ResultBox.Error<T>(new NotSupportedException()));
        }

        public Task<bool> ActorExistsAsync(string actorId) => Task.FromResult(true);
    }
}
