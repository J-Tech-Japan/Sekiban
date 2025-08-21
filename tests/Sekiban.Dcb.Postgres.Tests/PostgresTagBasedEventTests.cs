using Dcb.Domain.ClassRoom;
using Dcb.Domain.Enrollment;
using Dcb.Domain.Student;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Xunit;
namespace Sekiban.Dcb.Postgres.Tests;

public class PostgresTagBasedEventTests : PostgresTestBase
{
    public PostgresTagBasedEventTests(PostgresTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Should_Write_And_Read_Events_With_Tags()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var classRoomId = Guid.NewGuid();
        var classRoomTag = new ClassRoomTag(classRoomId);

        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "John Doe"), studentTag);

        var event2 = EventTestHelper.CreateEvent(new ClassRoomCreated(classRoomId, "Math 101", 30), classRoomTag);

        var event3 = EventTestHelper.CreateEvent(
            new StudentEnrolledInClassRoom(studentId, classRoomId),
            studentTag,
            classRoomTag);

        // Act - Write events
        var writeResult = await Fixture.EventStore.WriteEventsAsync(new[] { event1, event2, event3 });

        // Assert - Write was successful
        Assert.True(writeResult.IsSuccess);
        Assert.Equal(3, writeResult.GetValue().Events.Count);

        // Act - Read all events
        var allEventsResult = await Fixture.EventStore.ReadAllEventsAsync();

        // Assert - All events are returned in order
        Assert.True(allEventsResult.IsSuccess);
        var allEvents = allEventsResult.GetValue().ToList();
        Assert.Equal(3, allEvents.Count);
        Assert.Equal(event1.Id, allEvents[0].Id);
        Assert.Equal(event2.Id, allEvents[1].Id);
        Assert.Equal(event3.Id, allEvents[2].Id);

        // Act - Read events by student tag
        var studentEventsResult = await Fixture.EventStore.ReadEventsByTagAsync(studentTag);

        // Assert - Only student-related events are returned
        Assert.True(studentEventsResult.IsSuccess);
        var studentEvents = studentEventsResult.GetValue().ToList();
        Assert.Equal(2, studentEvents.Count);
        Assert.Equal(event1.Id, studentEvents[0].Id);
        Assert.Equal(event3.Id, studentEvents[1].Id);

        // Act - Read events by classroom tag
        var classRoomEventsResult = await Fixture.EventStore.ReadEventsByTagAsync(classRoomTag);

        // Assert - Only classroom-related events are returned
        Assert.True(classRoomEventsResult.IsSuccess);
        var classRoomEvents = classRoomEventsResult.GetValue().ToList();
        Assert.Equal(2, classRoomEvents.Count);
        Assert.Equal(event2.Id, classRoomEvents[0].Id);
        Assert.Equal(event3.Id, classRoomEvents[1].Id);
    }

    [Fact]
    public async Task Should_Read_Events_Since_Specific_SortableUniqueId()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);

        var events = new List<Event>();
        for (var i = 0; i < 5; i++)
        {
            events.Add(EventTestHelper.CreateEvent(new StudentCreated(Guid.NewGuid(), $"Student {i}"), studentTag));
            await Task.Delay(10); // Ensure different timestamps
        }

        // Write all events
        await Fixture.EventStore.WriteEventsAsync(events);

        // Act - Read events since the second event
        var sinceId = new SortableUniqueId(events[1].SortableUniqueIdValue);
        var eventsResult = await Fixture.EventStore.ReadEventsByTagAsync(studentTag, sinceId);

        // Assert - Only events after the second one are returned
        Assert.True(eventsResult.IsSuccess);
        var returnedEvents = eventsResult.GetValue().ToList();
        Assert.Equal(3, returnedEvents.Count);
        Assert.Equal(events[2].Id, returnedEvents[0].Id);
        Assert.Equal(events[3].Id, returnedEvents[1].Id);
        Assert.Equal(events[4].Id, returnedEvents[2].Id);
    }

    [Fact]
    public async Task Should_Check_Tag_Exists()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var nonExistentTag = new StudentTag(Guid.NewGuid());

        // Act - Check before writing any events
        var existsBeforeResult = await Fixture.EventStore.TagExistsAsync(studentTag);

        // Assert
        Assert.True(existsBeforeResult.IsSuccess);
        Assert.False(existsBeforeResult.GetValue());

        // Arrange - Write an event with the tag
        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "Jane Doe", 3), studentTag);

        await Fixture.EventStore.WriteEventsAsync(new[] { event1 });

        // Act - Check after writing event
        var existsAfterResult = await Fixture.EventStore.TagExistsAsync(studentTag);
        var nonExistentResult = await Fixture.EventStore.TagExistsAsync(nonExistentTag);

        // Assert
        Assert.True(existsAfterResult.IsSuccess);
        Assert.True(existsAfterResult.GetValue());

        Assert.True(nonExistentResult.IsSuccess);
        Assert.False(nonExistentResult.GetValue());
    }

    [Fact]
    public async Task Should_Get_Tag_Streams()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);

        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "Alice"), studentTag);

        var event2 = EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, Guid.NewGuid()), studentTag);

        await Fixture.EventStore.WriteEventsAsync(new[] { event1, event2 });

        // Act
        var tagStreamsResult = await Fixture.EventStore.ReadTagsAsync(studentTag);

        // Assert
        Assert.True(tagStreamsResult.IsSuccess);
        var tagStreams = tagStreamsResult.GetValue().ToList();
        Assert.Equal(2, tagStreams.Count);

        Assert.Equal(studentTag.GetTag(), tagStreams[0].Tag);
        Assert.Equal(event1.Id, tagStreams[0].EventId);
        Assert.Equal(event1.SortableUniqueIdValue, tagStreams[0].SortableUniqueId);

        Assert.Equal(studentTag.GetTag(), tagStreams[1].Tag);
        Assert.Equal(event2.Id, tagStreams[1].EventId);
        Assert.Equal(event2.SortableUniqueIdValue, tagStreams[1].SortableUniqueId);
    }

    [Fact]
    public async Task Should_Get_Latest_Tag_State()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);

        // Act - Get state before any events
        var beforeResult = await Fixture.EventStore.GetLatestTagAsync(studentTag);

        // Assert - Should return empty state
        Assert.True(beforeResult.IsSuccess);
        var beforeState = beforeResult.GetValue();
        Assert.IsType<EmptyTagStatePayload>(beforeState.Payload);
        Assert.Equal(0, beforeState.Version);
        Assert.Empty(beforeState.LastSortedUniqueId);

        // Arrange - Write events
        var event1 = EventTestHelper.CreateEvent(new StudentCreated(studentId, "Bob"), studentTag);

        await Task.Delay(10);

        var event2 = EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, Guid.NewGuid()), studentTag);

        await Fixture.EventStore.WriteEventsAsync(new[] { event1, event2 });

        // Act - Get state after events
        var afterResult = await Fixture.EventStore.GetLatestTagAsync(studentTag);

        // Assert - Should return state with latest sortable unique ID
        Assert.True(afterResult.IsSuccess);
        var afterState = afterResult.GetValue();
        Assert.IsType<EmptyTagStatePayload>(afterState.Payload);
        Assert.Equal(0, afterState.Version); // Version is not tracked in simplified implementation
        Assert.Equal(event2.SortableUniqueIdValue, afterState.LastSortedUniqueId);
    }
}
