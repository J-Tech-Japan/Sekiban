using Dcb.Domain;
using Dcb.Domain.Enrollment;
using Dcb.Domain.Student;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for GeneralTagConsistentActor with catch-up functionality
/// </summary>
public class GeneralTagConsistentActorTests
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventStore _eventStore;

    public GeneralTagConsistentActorTests()
    {
        _domainTypes = DomainType.GetDomainTypes();
        _eventStore = new CoreInMemoryEventStore(_domainTypes.EventTypes);
    }

    [Fact]
    public async Task TagConsistentActor_Should_CatchUp_From_EventStore()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagName = studentTag.GetTag();

        // Write some events to create tag state
        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "John Doe"), studentTag);
        await _eventStore.WriteEventAsync(event1, _domainTypes.EventTypes);

        var event2 = EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, Guid.NewGuid()), studentTag);
        await _eventStore.WriteEventAsync(event2, _domainTypes.EventTypes);

        // Create actor (it should catch up lazily)
        var actor = new GeneralTagConsistentActor(tagName, _eventStore, new TagConsistentActorOptions(), _domainTypes.TagTypes);

        // Act - Get latest sortable unique ID (should trigger catch-up)
        var latestSortableUniqueIdResult = await actor.GetLatestSortableUniqueIdAsync();

        // Assert
        Assert.True(latestSortableUniqueIdResult.IsSuccess);
        Assert.Equal(event2.SortableUniqueIdValue, latestSortableUniqueIdResult.GetValue());
    }

    [Fact]
    public async Task TagConsistentActor_Should_Handle_No_Existing_State()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagName = studentTag.GetTag();

        // Create actor without any existing state
        var actor = new GeneralTagConsistentActor(tagName, _eventStore, new TagConsistentActorOptions(), _domainTypes.TagTypes);

        // Act
        var latestSortableUniqueIdResult = await actor.GetLatestSortableUniqueIdAsync();

        // Assert
        Assert.True(latestSortableUniqueIdResult.IsSuccess);
        Assert.Equal("", latestSortableUniqueIdResult.GetValue());
    }

    [Fact]
    public async Task TagConsistentActor_NonEmptyExpected_On_EmptyTag_Conflicts()
    {
        // SEK-G19 (secondary hole, class 3): a caller that expects a specific non-empty version on a tag that has NO
        // committed state must CONFLICT — the tag never had that version. (Previously this passed and silently adopted the
        // expected version.) The conflict surfaces through the existing ResultBox.Error channel, no new exception type.
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagName = studentTag.GetTag();
        var actor = new GeneralTagConsistentActor(tagName, _eventStore, new TagConsistentActorOptions(), _domainTypes.TagTypes);

        var reservationResult = await actor.MakeReservationAsync(SortableUniqueId.GenerateNew());

        Assert.False(reservationResult.IsSuccess);
        Assert.NotNull(reservationResult.GetException());
        // The tag stays empty — a rejected reservation does not adopt the expected version.
        var latestIdResult = await actor.GetLatestSortableUniqueIdAsync();
        Assert.True(latestIdResult.IsSuccess);
        Assert.Equal("", latestIdResult.GetValue());
    }

    [Fact]
    public async Task TagConsistentActor_Without_EventStore_Should_Work()
    {
        // Arrange
        var tagName = "Student:12345";
        var actor = new GeneralTagConsistentActor(tagName, null, new TagConsistentActorOptions(), _domainTypes.TagTypes);

        // Act
        var latestSortableUniqueIdResult = await actor.GetLatestSortableUniqueIdAsync();
        var reservationResult = await actor.MakeReservationAsync("");

        // Assert
        Assert.True(latestSortableUniqueIdResult.IsSuccess);
        Assert.Equal("", latestSortableUniqueIdResult.GetValue());
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
        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "John Doe"), studentTag);
        await _eventStore.WriteEventAsync(event1, _domainTypes.EventTypes);

        // Create actor and trigger catch-up
        var actor = new GeneralTagConsistentActor(tagName, _eventStore, new TagConsistentActorOptions(), _domainTypes.TagTypes);
        var firstIdResult = await actor.GetLatestSortableUniqueIdAsync();

        // Write another event after actor creation
        var event2 = EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, Guid.NewGuid()), studentTag);
        await _eventStore.WriteEventAsync(event2, _domainTypes.EventTypes);

        // Act - Get latest ID again (should not catch up again)
        var secondIdResult = await actor.GetLatestSortableUniqueIdAsync();

        // Assert - Should still have the first event's ID
        Assert.True(firstIdResult.IsSuccess);
        Assert.True(secondIdResult.IsSuccess);
        Assert.Equal(event1.SortableUniqueIdValue, firstIdResult.GetValue());
        Assert.Equal(firstIdResult.GetValue(), secondIdResult.GetValue());
    }

    [Fact]
    public async Task TagConsistentActor_Should_Preserve_State_After_CatchUp()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagName = studentTag.GetTag();

        // Write event
        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "John Doe"), studentTag);
        await _eventStore.WriteEventAsync(event1, _domainTypes.EventTypes);

        // Create actor - it will catch up from event store
        var actor = new GeneralTagConsistentActor(tagName, _eventStore, new TagConsistentActorOptions(), _domainTypes.TagTypes);

        // Act - Get the latest ID after catch up
        var latestIdResult = await actor.GetLatestSortableUniqueIdAsync();

        // Assert - Should have the event's ID after catch up
        Assert.True(latestIdResult.IsSuccess);
        Assert.Equal(event1.SortableUniqueIdValue, latestIdResult.GetValue());
    }

    [Fact]
    public async Task All_Methods_Should_Trigger_CatchUp()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagName = studentTag.GetTag();

        // Write event
        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "John Doe"), studentTag);
        await _eventStore.WriteEventAsync(event1, _domainTypes.EventTypes);

        // Test each method triggers catch-up

        // Test GetLatestSortableUniqueId
        var actor1 = new GeneralTagConsistentActor(tagName, _eventStore, new TagConsistentActorOptions(), _domainTypes.TagTypes);
        var result1 = await actor1.GetLatestSortableUniqueIdAsync();
        Assert.True(result1.IsSuccess);
        Assert.Equal(event1.SortableUniqueIdValue, result1.GetValue());

        // Test MakeReservation (still triggers catch-up). SEK-G19: the tag already has committed state (event1), so the
        // reservation must EXPECT the current version to succeed — an expect-empty ("") here would (correctly) conflict.
        var actor2 = new GeneralTagConsistentActor(tagName, _eventStore, new TagConsistentActorOptions(), _domainTypes.TagTypes);
        var reservation = await actor2.MakeReservationAsync(event1.SortableUniqueIdValue);
        Assert.True(reservation.IsSuccess);
        var result2 = await actor2.GetLatestSortableUniqueIdAsync();
        Assert.True(result2.IsSuccess);
        Assert.Equal(event1.SortableUniqueIdValue, result2.GetValue());

        // Test ConfirmReservation
        var actor3 = new GeneralTagConsistentActor(tagName, _eventStore, new TagConsistentActorOptions(), _domainTypes.TagTypes);
        await actor3.ConfirmReservationAsync(null!);
        var result3 = await actor3.GetLatestSortableUniqueIdAsync();
        Assert.True(result3.IsSuccess);
        Assert.Equal(event1.SortableUniqueIdValue, result3.GetValue());

        // Test CancelReservation
        var actor4 = new GeneralTagConsistentActor(tagName, _eventStore, new TagConsistentActorOptions(), _domainTypes.TagTypes);
        await actor4.CancelReservationAsync(null!);
        var result4 = await actor4.GetLatestSortableUniqueIdAsync();
        Assert.True(result4.IsSuccess);
        Assert.Equal(event1.SortableUniqueIdValue, result4.GetValue());

        // Test GetActiveReservations
        var actor5 = new GeneralTagConsistentActor(tagName, _eventStore, new TagConsistentActorOptions(), _domainTypes.TagTypes);
        await actor5.GetActiveReservationsAsync();
        var result5 = await actor5.GetLatestSortableUniqueIdAsync();
        Assert.True(result5.IsSuccess);
        Assert.Equal(event1.SortableUniqueIdValue, result5.GetValue());
    }
}
