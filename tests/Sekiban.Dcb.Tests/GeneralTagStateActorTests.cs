using Dcb.Domain;
using Dcb.Domain.ClassRoom;
using Dcb.Domain.Enrollment;
using Dcb.Domain.Student;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for GeneralTagStateActor with real domain events
/// </summary>
public class GeneralTagStateActorTests
{
    private readonly InMemoryObjectAccessor _accessor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly InMemoryEventStore _eventStore;

    public GeneralTagStateActorTests()
    {
        _eventStore = new InMemoryEventStore();
        _domainTypes = DomainType.GetDomainTypes();
        _accessor = new InMemoryObjectAccessor(_eventStore, _domainTypes);
    }

    [Fact]
    public async Task TagStateActor_Should_Compute_State_From_Events()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagStateId = new TagStateId(studentTag, new StudentProjector(), _domainTypes.TagProjectorTypes);

        // Add StudentCreated event
        var studentCreatedEvent = new StudentCreated(studentId, "John Doe");
        await _eventStore.WriteEventAsync(EventTestHelper.CreateEvent(studentCreatedEvent, studentTag));

        // Act
        var actor = new GeneralTagStateActor(tagStateId.GetTagStateId(), _eventStore, _domainTypes, _accessor);
        var state = await actor.GetTagStateAsync();

        // Assert
        Assert.NotNull(state);
        Assert.Equal("Student", state.TagGroup);
        Assert.Equal(studentId.ToString(), state.TagContent);
        Assert.Equal("StudentProjector", state.TagProjector);
        Assert.Equal(1, state.Version);
        Assert.NotEmpty(state.LastSortedUniqueId);

        var studentState = state.Payload as StudentState;
        Assert.NotNull(studentState);
        Assert.Equal(studentId, studentState.StudentId);
        Assert.Equal("John Doe", studentState.Name);
        Assert.Equal(5, studentState.MaxClassCount);
        Assert.Empty(studentState.EnrolledClassRoomIds);
    }

    [Fact]
    public async Task TagStateActor_Should_Handle_Multiple_Events()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var classRoomId1 = Guid.NewGuid();
        var classRoomId2 = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagStateId = new TagStateId(studentTag, new StudentProjector(), _domainTypes.TagProjectorTypes);

        // Add multiple events
        await _eventStore.WriteEventAsync(
            EventTestHelper.CreateEvent(new StudentCreated(studentId, "Jane Smith", 3), studentTag));

        await _eventStore.WriteEventAsync(
            EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, classRoomId1), studentTag));

        await _eventStore.WriteEventAsync(
            EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, classRoomId2), studentTag));

        // Act
        var actor = new GeneralTagStateActor(tagStateId.GetTagStateId(), _eventStore, _domainTypes, _accessor);
        var state = await actor.GetTagStateAsync();

        // Assert
        Assert.Equal(3, state.Version);
        var studentState = state.Payload as StudentState;
        Assert.NotNull(studentState);
        Assert.Equal(2, studentState.EnrolledClassRoomIds.Count);
        Assert.Contains(classRoomId1, studentState.EnrolledClassRoomIds);
        Assert.Contains(classRoomId2, studentState.EnrolledClassRoomIds);
        Assert.Equal(1, studentState.GetRemaining()); // MaxClassCount(3) - Enrolled(2) = 1
    }

    [Fact]
    public async Task TagStateActor_Should_Handle_ClassRoom_State()
    {
        // Arrange
        var classRoomId = Guid.NewGuid();
        var studentId1 = Guid.NewGuid();
        var studentId2 = Guid.NewGuid();
        var classRoomTag = new ClassRoomTag(classRoomId);
        var tagStateId = new TagStateId(classRoomTag, new ClassRoomProjector(), _domainTypes.TagProjectorTypes);

        // Add ClassRoom events
        await _eventStore.WriteEventAsync(
            EventTestHelper.CreateEvent(new ClassRoomCreated(classRoomId, "Math 101"), classRoomTag));

        await _eventStore.WriteEventAsync(
            EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId1, classRoomId), classRoomTag));

        await _eventStore.WriteEventAsync(
            EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId2, classRoomId), classRoomTag));

        // Act
        var actor = new GeneralTagStateActor(tagStateId.GetTagStateId(), _eventStore, _domainTypes, _accessor);
        var state = await actor.GetTagStateAsync();

        // Assert
        Assert.Equal(3, state.Version);
        var classRoomState = state.Payload as AvailableClassRoomState;
        Assert.NotNull(classRoomState);
        Assert.Equal(classRoomId, classRoomState.ClassRoomId);
        Assert.Equal("Math 101", classRoomState.Name);
        Assert.Equal(10, classRoomState.MaxStudents);
        Assert.Equal(2, classRoomState.EnrolledStudentIds.Count);
        Assert.Contains(studentId1, classRoomState.EnrolledStudentIds);
        Assert.Contains(studentId2, classRoomState.EnrolledStudentIds);
        Assert.Equal(8, classRoomState.GetRemaining());
    }

    [Fact]
    public async Task TagStateActor_Should_Handle_Student_Dropped_From_ClassRoom()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var classRoomId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagStateId = new TagStateId(studentTag, new StudentProjector(), _domainTypes.TagProjectorTypes);

        // Add events
        await _eventStore.WriteEventAsync(
            EventTestHelper.CreateEvent(new StudentCreated(studentId, "Alice Johnson"), studentTag));

        await _eventStore.WriteEventAsync(
            EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, classRoomId), studentTag));

        await _eventStore.WriteEventAsync(
            EventTestHelper.CreateEvent(new StudentDroppedFromClassRoom(studentId, classRoomId), studentTag));

        // Act
        var actor = new GeneralTagStateActor(tagStateId.GetTagStateId(), _eventStore, _domainTypes, _accessor);
        var state = await actor.GetTagStateAsync();

        // Assert
        Assert.Equal(3, state.Version);
        var studentState = state.Payload as StudentState;
        Assert.NotNull(studentState);
        Assert.Empty(studentState.EnrolledClassRoomIds);
    }

    [Fact]
    public async Task TagStateActor_Should_Return_Empty_State_When_No_Events()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagStateId = new TagStateId(studentTag, new StudentProjector(), _domainTypes.TagProjectorTypes);

        // Act
        var actor = new GeneralTagStateActor(tagStateId.GetTagStateId(), _eventStore, _domainTypes, _accessor);
        var state = await actor.GetTagStateAsync();

        // Assert
        Assert.NotNull(state);
        Assert.Equal(0, state.Version);
        Assert.Equal("", state.LastSortedUniqueId);
        Assert.IsType<EmptyTagStatePayload>(state.Payload);
    }

    // Removed: Cache test is no longer valid with required TagConsistentActor
    // The cache behavior now depends on TagConsistentActor's state

    [Fact]
    public async Task TagStateActor_Should_Return_SerializableState()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagStateId = new TagStateId(studentTag, new StudentProjector(), _domainTypes.TagProjectorTypes);

        await _eventStore.WriteEventAsync(
            EventTestHelper.CreateEvent(new StudentCreated(studentId, "Carol Davis", 2), studentTag));

        var actor = new GeneralTagStateActor(tagStateId.GetTagStateId(), _eventStore, _domainTypes, _accessor);

        // Act
        var serializableState = await actor.GetStateAsync();

        // Assert
        Assert.NotNull(serializableState);
        Assert.NotEmpty(serializableState.Payload);
        Assert.Equal("StudentState", serializableState.TagPayloadName);
        Assert.Equal(1, serializableState.Version);
        Assert.NotEmpty(serializableState.LastSortedUniqueId);
        Assert.Equal("Student", serializableState.TagGroup);
        Assert.Equal(studentId.ToString(), serializableState.TagContent);
        Assert.Equal("StudentProjector", serializableState.TagProjector);

        // Verify payload can be deserialized
        var json = Encoding.UTF8.GetString(serializableState.Payload);
        Assert.Contains("Carol Davis", json);
    }

    [Fact]
    public void TagStateActor_Should_Handle_Invalid_TagStateId()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new GeneralTagStateActor(
            "InvalidFormat",
            _eventStore,
            _domainTypes,
            _accessor));

        Assert.Throws<ArgumentException>(() => new GeneralTagStateActor(
            "Only:Two",
            _eventStore,
            _domainTypes,
            _accessor));
    }

    // Removed: UpdateState test is no longer valid with required TagConsistentActor
    // State updates should only happen through event processing with TagConsistentActor control

    [Fact]
    public async Task TagStateActor_Should_Reject_UpdateState_With_Different_Identity()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagStateId = new TagStateId(studentTag, new StudentProjector(), _domainTypes.TagProjectorTypes);

        var actor = new GeneralTagStateActor(tagStateId.GetTagStateId(), _eventStore, _domainTypes, _accessor);

        var differentState = new TagState(
            null!,
            1,
            "",
            "ClassRoom", // Different tag group
            studentId.ToString(),
            "StudentProjector",
            "1.0.0");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => actor.UpdateStateAsync(differentState));
    }
}
