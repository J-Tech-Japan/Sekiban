using Dcb.Domain;
using Dcb.Domain.Enrollment;
using Dcb.Domain.Student;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for GeneralTagConsistentActor with catch-up functionality
/// </summary>
public class GeneralTagConsistentActorTests
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly InMemoryEventStore _eventStore;

    public GeneralTagConsistentActorTests()
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
        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "John Doe"), studentTag);
        await _eventStore.WriteEventAsync(event1);

        var event2 = EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, Guid.NewGuid()), studentTag);
        await _eventStore.WriteEventAsync(event2);

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
    public async Task TagConsistentActor_Should_Update_Latest_SortableUniqueId_On_Reservation()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagName = studentTag.GetTag();
        var actor = new GeneralTagConsistentActor(tagName, _eventStore, new TagConsistentActorOptions(), _domainTypes.TagTypes);

        // Act
        var newSortableUniqueId = SortableUniqueId.GenerateNew();
        var reservationResult = await actor.MakeReservationAsync(newSortableUniqueId);

        // Assert
        Assert.True(reservationResult.IsSuccess);
        var latestIdResult = await actor.GetLatestSortableUniqueIdAsync();
        Assert.True(latestIdResult.IsSuccess);
        Assert.Equal(newSortableUniqueId, latestIdResult.GetValue());
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
        await _eventStore.WriteEventAsync(event1);

        // Create actor and trigger catch-up
        var actor = new GeneralTagConsistentActor(tagName, _eventStore, new TagConsistentActorOptions(), _domainTypes.TagTypes);
        var firstIdResult = await actor.GetLatestSortableUniqueIdAsync();

        // Write another event after actor creation
        var event2 = EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, Guid.NewGuid()), studentTag);
        await _eventStore.WriteEventAsync(event2);

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
        await _eventStore.WriteEventAsync(event1);

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
        await _eventStore.WriteEventAsync(event1);

        // Test each method triggers catch-up

        // Test GetLatestSortableUniqueId
        var actor1 = new GeneralTagConsistentActor(tagName, _eventStore, new TagConsistentActorOptions(), _domainTypes.TagTypes);
        var result1 = await actor1.GetLatestSortableUniqueIdAsync();
        Assert.True(result1.IsSuccess);
        Assert.Equal(event1.SortableUniqueIdValue, result1.GetValue());

        // Test MakeReservation
        var actor2 = new GeneralTagConsistentActor(tagName, _eventStore, new TagConsistentActorOptions(), _domainTypes.TagTypes);
        var reservation = await actor2.MakeReservationAsync("");
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
