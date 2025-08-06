using Xunit;
using FluentAssertions;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Dcb.Domain;
using Sekiban.Dcb.Actors;

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
        
        var event1 = EventTestHelper.CreateEvent(
            new StudentCreated(studentId, "John Doe", 5),
            studentTag);
        
        var event2 = EventTestHelper.CreateEvent(
            new ClassRoomCreated(classRoomId, "Math 101", 30),
            classRoomTag);
        
        var event3 = EventTestHelper.CreateEvent(
            new StudentEnrolledInClassRoom(studentId, classRoomId),
            studentTag, classRoomTag);
        
        // Act - Write events
        var writeResult = await Fixture.EventStore.WriteEventsAsync(new[] { event1, event2, event3 });
        
        // Assert - Write was successful
        writeResult.IsSuccess.Should().BeTrue();
        writeResult.GetValue().Events.Should().HaveCount(3);
        
        // Act - Read all events
        var allEventsResult = await Fixture.EventStore.ReadAllEventsAsync();
        
        // Assert - All events are returned in order
        allEventsResult.IsSuccess.Should().BeTrue();
        var allEvents = allEventsResult.GetValue().ToList();
        allEvents.Should().HaveCount(3);
        allEvents[0].Id.Should().Be(event1.Id);
        allEvents[1].Id.Should().Be(event2.Id);
        allEvents[2].Id.Should().Be(event3.Id);
        
        // Act - Read events by student tag
        var studentEventsResult = await Fixture.EventStore.ReadEventsByTagAsync(studentTag);
        
        // Assert - Only student-related events are returned
        studentEventsResult.IsSuccess.Should().BeTrue();
        var studentEvents = studentEventsResult.GetValue().ToList();
        studentEvents.Should().HaveCount(2);
        studentEvents[0].Id.Should().Be(event1.Id);
        studentEvents[1].Id.Should().Be(event3.Id);
        
        // Act - Read events by classroom tag
        var classRoomEventsResult = await Fixture.EventStore.ReadEventsByTagAsync(classRoomTag);
        
        // Assert - Only classroom-related events are returned
        classRoomEventsResult.IsSuccess.Should().BeTrue();
        var classRoomEvents = classRoomEventsResult.GetValue().ToList();
        classRoomEvents.Should().HaveCount(2);
        classRoomEvents[0].Id.Should().Be(event2.Id);
        classRoomEvents[1].Id.Should().Be(event3.Id);
    }
    
    [Fact]
    public async Task Should_Read_Events_Since_Specific_SortableUniqueId()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        
        var events = new List<Event>();
        for (int i = 0; i < 5; i++)
        {
            events.Add(EventTestHelper.CreateEvent(
                new StudentCreated(Guid.NewGuid(), $"Student {i}", 5),
                studentTag));
            await Task.Delay(10); // Ensure different timestamps
        }
        
        // Write all events
        await Fixture.EventStore.WriteEventsAsync(events);
        
        // Act - Read events since the second event
        var sinceId = new SortableUniqueId(events[1].SortableUniqueIdValue);
        var eventsResult = await Fixture.EventStore.ReadEventsByTagAsync(studentTag, sinceId);
        
        // Assert - Only events after the second one are returned
        eventsResult.IsSuccess.Should().BeTrue();
        var returnedEvents = eventsResult.GetValue().ToList();
        returnedEvents.Should().HaveCount(3);
        returnedEvents[0].Id.Should().Be(events[2].Id);
        returnedEvents[1].Id.Should().Be(events[3].Id);
        returnedEvents[2].Id.Should().Be(events[4].Id);
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
        existsBeforeResult.IsSuccess.Should().BeTrue();
        existsBeforeResult.GetValue().Should().BeFalse();
        
        // Arrange - Write an event with the tag
        var event1 = EventTestHelper.CreateEvent(
            new StudentCreated(studentId, "Jane Doe", 3),
            studentTag);
        
        await Fixture.EventStore.WriteEventsAsync(new[] { event1 });
        
        // Act - Check after writing event
        var existsAfterResult = await Fixture.EventStore.TagExistsAsync(studentTag);
        var nonExistentResult = await Fixture.EventStore.TagExistsAsync(nonExistentTag);
        
        // Assert
        existsAfterResult.IsSuccess.Should().BeTrue();
        existsAfterResult.GetValue().Should().BeTrue();
        
        nonExistentResult.IsSuccess.Should().BeTrue();
        nonExistentResult.GetValue().Should().BeFalse();
    }
    
    [Fact]
    public async Task Should_Get_Tag_Streams()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        
        var event1 = EventTestHelper.CreateEvent(
            new StudentCreated(studentId, "Alice", 5),
            studentTag);
        
        var event2 = EventTestHelper.CreateEvent(
            new StudentEnrolledInClassRoom(studentId, Guid.NewGuid()),
            studentTag);
        
        await Fixture.EventStore.WriteEventsAsync(new[] { event1, event2 });
        
        // Act
        var tagStreamsResult = await Fixture.EventStore.ReadTagsAsync(studentTag);
        
        // Assert
        tagStreamsResult.IsSuccess.Should().BeTrue();
        var tagStreams = tagStreamsResult.GetValue().ToList();
        tagStreams.Should().HaveCount(2);
        
        tagStreams[0].Tag.Should().Be(studentTag.GetTag());
        tagStreams[0].EventId.Should().Be(event1.Id);
        tagStreams[0].SortableUniqueId.Should().Be(event1.SortableUniqueIdValue);
        
        tagStreams[1].Tag.Should().Be(studentTag.GetTag());
        tagStreams[1].EventId.Should().Be(event2.Id);
        tagStreams[1].SortableUniqueId.Should().Be(event2.SortableUniqueIdValue);
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
        beforeResult.IsSuccess.Should().BeTrue();
        var beforeState = beforeResult.GetValue();
        beforeState.Payload.Should().BeOfType<EmptyTagStatePayload>();
        beforeState.Version.Should().Be(0);
        beforeState.LastSortedUniqueId.Should().BeEmpty();
        
        // Arrange - Write events
        var event1 = EventTestHelper.CreateEvent(
            new StudentCreated(studentId, "Bob", 5),
            studentTag);
        
        await Task.Delay(10);
        
        var event2 = EventTestHelper.CreateEvent(
            new StudentEnrolledInClassRoom(studentId, Guid.NewGuid()),
            studentTag);
        
        await Fixture.EventStore.WriteEventsAsync(new[] { event1, event2 });
        
        // Act - Get state after events
        var afterResult = await Fixture.EventStore.GetLatestTagAsync(studentTag);
        
        // Assert - Should return state with latest sortable unique ID
        afterResult.IsSuccess.Should().BeTrue();
        var afterState = afterResult.GetValue();
        afterState.Payload.Should().BeOfType<EmptyTagStatePayload>();
        afterState.Version.Should().Be(0); // Version is not tracked in simplified implementation
        afterState.LastSortedUniqueId.Should().Be(event2.SortableUniqueIdValue);
    }
}