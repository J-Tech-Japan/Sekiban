using Dcb.Domain;
using Dcb.Domain.Student;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Tests;

public class TagReservationHelperTests
{
    private readonly DcbDomainTypes _domainTypes = DomainType.GetDomainTypes();

    private (InMemoryObjectAccessor Accessor, InMemoryEventStore EventStore) CreateAccessor()
    {
        var eventStore = new InMemoryEventStore();
        var accessor = new InMemoryObjectAccessor(eventStore, _domainTypes);
        return (accessor, eventStore);
    }

    [Fact]
    public async Task RequestReservationAsync_Should_Succeed_For_New_Tag()
    {
        // Given
        var (accessor, _) = CreateAccessor();
        var studentId = Guid.NewGuid();
        var tag = new ConsistencyTag(new StudentTag(studentId));

        // When
        var result = await TagReservationHelper.RequestReservationAsync(accessor, tag, "");

        // Then
        Assert.True(result.IsSuccess);
        var reservation = result.GetValue();
        Assert.NotEmpty(reservation.ReservationCode);
        Assert.Equal(tag.GetTag(), reservation.Tag);
    }

    [Fact]
    public async Task RequestReservationAsync_Should_Fail_With_Stale_SortableId()
    {
        // Given: commit an event via InMemoryDcbExecutor to establish real state
        var executor = new InMemoryDcbExecutor(_domainTypes);
        var sekibanExecutor = (ISekibanExecutor)executor;
        var studentId = Guid.NewGuid();
        var tag = new ConsistencyTag(new StudentTag(studentId));

        var command = new CreateStudent(studentId, "Alice", 5);
        var commandResult = await sekibanExecutor.ExecuteAsync(command);
        Assert.True(commandResult.IsSuccess);

        // When: request reservation via serialized path with a stale sortable ID
        var serializedExecutor = (ISerializedSekibanDcbExecutor)executor;
        var tagString = $"Student:{studentId}";
        var payload = System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new StudentCreated(studentId, "Updated", 3)));
        var staleSortableId = "0000000000000000000_00000000000";

        var request = new SerializedCommitRequest(
            [new SerializableEventCandidate(payload, nameof(StudentCreated), [tagString])],
            [new ConsistencyTagEntry(tagString, staleSortableId)]);

        var result = await serializedExecutor.CommitSerializableEventsAsync(request);

        // Then: should fail due to version mismatch
        Assert.False(result.IsSuccess);
        Assert.Contains("Failed to reserve tags", result.GetException().Message);
    }

    [Fact]
    public async Task CancelReservationsAsync_Should_Not_Throw_On_Empty()
    {
        // Given
        var (accessor, _) = CreateAccessor();
        var reservations = new Dictionary<ITag, TagWriteReservation>();

        // When / Then: should complete without error
        await TagReservationHelper.CancelReservationsAsync(accessor, reservations);
    }

    [Fact]
    public async Task ConfirmReservationsAsync_Should_Succeed_After_Valid_Reservation()
    {
        // Given
        var (accessor, _) = CreateAccessor();
        var studentId = Guid.NewGuid();
        var tag = new ConsistencyTag(new StudentTag(studentId));

        var reservationResult = await TagReservationHelper.RequestReservationAsync(accessor, tag, "");
        Assert.True(reservationResult.IsSuccess);
        var reservation = reservationResult.GetValue();

        // When
        var reservations = new Dictionary<ITag, TagWriteReservation> { { tag, reservation } };
        await TagReservationHelper.ConfirmReservationsAsync(accessor, reservations);

        // Then: a subsequent reservation with empty lastSortableUniqueId should succeed
        // (confirming the previous reservation was properly processed)
        var nextResult = await TagReservationHelper.RequestReservationAsync(accessor, tag, "");
        Assert.True(nextResult.IsSuccess);
    }

    [Fact]
    public async Task CancelReservationAsync_Should_Allow_New_Reservation()
    {
        // Given: make and cancel a reservation
        var (accessor, _) = CreateAccessor();
        var studentId = Guid.NewGuid();
        var tag = new ConsistencyTag(new StudentTag(studentId));

        var reservationResult = await TagReservationHelper.RequestReservationAsync(accessor, tag, "");
        Assert.True(reservationResult.IsSuccess);
        var reservation = reservationResult.GetValue();

        var reservations = new Dictionary<ITag, TagWriteReservation> { { tag, reservation } };
        await TagReservationHelper.CancelReservationsAsync(accessor, reservations);

        // When: request a new reservation after cancel
        var nextResult = await TagReservationHelper.RequestReservationAsync(accessor, tag, "");

        // Then: should succeed since the previous reservation was cancelled
        Assert.True(nextResult.IsSuccess);
    }

    [Fact]
    public async Task NotifyNonConsistencyTagsAsync_Should_Not_Throw()
    {
        // Given
        var (accessor, _) = CreateAccessor();
        var studentId = Guid.NewGuid();
        var consistencyTag = (ITag)new ConsistencyTag(new StudentTag(studentId));
        var nonConsistencyTag = (ITag)new FallbackTag("AuditLog", "global");

        var allTags = new HashSet<ITag> { consistencyTag, nonConsistencyTag };
        var reservedTags = new[] { consistencyTag };

        // When / Then: should complete without error
        await TagReservationHelper.NotifyNonConsistencyTagsAsync(accessor, allTags, reservedTags);
    }
}
