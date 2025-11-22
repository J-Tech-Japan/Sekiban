using Dcb.Domain.ClassRoom;
using Dcb.Domain.Enrollment;
using Dcb.Domain.Student;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     Comprehensive tests for InMemoryEventStore covering:
///     - Event writing and reading
///     - Tag management and versioning
///     - Event ordering and filtering
///     - Error handling
///     - Multi-tag scenarios
/// </summary>
public class InMemoryEventStoreTests
{
    private readonly InMemoryEventStore _store;

    public InMemoryEventStoreTests() => _store = new InMemoryEventStore();

    [Fact]
    public async Task WriteEventAsync_Should_Return_EventId()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var eventPayload = new StudentCreated(studentId, "John Doe");
        var tags = new List<ITag> { new StudentTag(studentId) };

        // Act
        var evt = EventTestHelper.CreateEvent(eventPayload, tags);
        var result = await _store.WriteEventAsync(evt);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.GetValue().Id);
    }

    [Fact]
    public async Task ReadEventAsync_Should_Return_Written_Event()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var eventPayload = new StudentCreated(studentId, "John Doe");
        var tags = new List<ITag> { new StudentTag(studentId) };

        var evt = EventTestHelper.CreateEvent(eventPayload, tags);
        var writeResult = await _store.WriteEventAsync(evt);
        var eventId = writeResult.GetValue().Id;

        // Act
        var readResult = await _store.ReadEventAsync(eventId);

        // Assert
        Assert.True(readResult.IsSuccess);
        var readEvent = readResult.GetValue();
        Assert.Equal(eventId, readEvent.Id);
        Assert.Equal("StudentCreated", readEvent.EventType);
        Assert.IsType<StudentCreated>(readEvent.Payload);
        var payload = (StudentCreated)readEvent.Payload;
        Assert.Equal(studentId, payload.StudentId);
        Assert.Equal("John Doe", payload.Name);
    }

    [Fact]
    public async Task ReadEventAsync_Should_Return_Error_For_NonExistent_Event()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await _store.ReadEventAsync(nonExistentId);

        // Assert
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ReadAllEventsAsync_Should_Return_All_Events()
    {
        // Arrange
        var student1 = new StudentCreated(Guid.NewGuid(), "Student 1");
        var student2 = new StudentCreated(Guid.NewGuid(), "Student 2");
        var classroom = new ClassRoomCreated(Guid.NewGuid(), "Math 101", 20);

        await _store.WriteEventAsync(EventTestHelper.CreateEvent(student1, new StudentTag(student1.StudentId)));
        await _store.WriteEventAsync(EventTestHelper.CreateEvent(student2, new StudentTag(student2.StudentId)));
        await _store.WriteEventAsync(EventTestHelper.CreateEvent(classroom, new ClassRoomTag(classroom.ClassRoomId)));

        // Act
        var result = await _store.ReadAllEventsAsync();

        // Assert
        Assert.True(result.IsSuccess);
        var events = result.GetValue().ToList();
        Assert.Equal(3, events.Count);
    }

    [Fact]
    public async Task ReadAllEventsAsync_With_Since_Should_Return_Events_After_Id()
    {
        // Arrange
        var student1 = new StudentCreated(Guid.NewGuid(), "Student 1");
        var student2 = new StudentCreated(Guid.NewGuid(), "Student 2");
        var student3 = new StudentCreated(Guid.NewGuid(), "Student 3");

        await _store.WriteEventAsync(EventTestHelper.CreateEvent(student1, new StudentTag(student1.StudentId)));
        var secondEventResult
            = await _store.WriteEventAsync(EventTestHelper.CreateEvent(student2, new StudentTag(student2.StudentId)));
        await _store.WriteEventAsync(EventTestHelper.CreateEvent(student3, new StudentTag(student3.StudentId)));

        // Get the sortable ID of the second event
        var secondEvent = (await _store.ReadEventAsync(secondEventResult.GetValue().Id)).GetValue();
        var since = new SortableUniqueId(secondEvent.SortableUniqueIdValue);

        // Act
        var result = await _store.ReadAllEventsAsync(since);

        // Assert
        Assert.True(result.IsSuccess);
        var events = result.GetValue().ToList();
        Assert.Single(events); // Only the third event
        Assert.Equal("Student 3", ((StudentCreated)events[0].Payload).Name);
    }

    [Fact]
    public async Task ReadEventsByTagAsync_Should_Return_Events_For_Specific_Tag()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var classRoomId = Guid.NewGuid();

        var studentCreated = new StudentCreated(studentId, "John");
        var enrolled = new StudentEnrolledInClassRoom(studentId, classRoomId);
        var classRoomCreated = new ClassRoomCreated(classRoomId, "Math 101", 20);

        var studentTag = new StudentTag(studentId);
        var classRoomTag = new ClassRoomTag(classRoomId);

        await _store.WriteEventAsync(EventTestHelper.CreateEvent(studentCreated, studentTag));
        await _store.WriteEventAsync(
            EventTestHelper.CreateEvent(enrolled, new List<ITag> { studentTag, classRoomTag }));
        await _store.WriteEventAsync(EventTestHelper.CreateEvent(classRoomCreated, classRoomTag));

        // Act
        var studentEvents = await _store.ReadEventsByTagAsync(studentTag);
        var classRoomEvents = await _store.ReadEventsByTagAsync(classRoomTag);

        // Assert
        Assert.True(studentEvents.IsSuccess);
        Assert.True(classRoomEvents.IsSuccess);

        var studentEventsList = studentEvents.GetValue().ToList();
        var classRoomEventsList = classRoomEvents.GetValue().ToList();

        Assert.Equal(2, studentEventsList.Count); // StudentCreated and Enrolled
        Assert.Equal(2, classRoomEventsList.Count); // ClassRoomCreated and Enrolled
    }

    [Fact]
    public async Task TagExistsAsync_Should_Return_True_For_Existing_Tag()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var eventPayload = new StudentCreated(studentId, "John");

        await _store.WriteEventAsync(EventTestHelper.CreateEvent(eventPayload, studentTag));

        // Act
        var exists = await _store.TagExistsAsync(studentTag);
        var notExists = await _store.TagExistsAsync(new StudentTag(Guid.NewGuid()));

        // Assert
        Assert.True(exists.IsSuccess);
        Assert.True(exists.GetValue());
        Assert.True(notExists.IsSuccess);
        Assert.False(notExists.GetValue());
    }

    [Fact]
    public async Task GetLatestTagAsync_Should_Return_Latest_State()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var classRoomId = Guid.NewGuid();

        // Create multiple events for the same tag
        await _store.WriteEventAsync(EventTestHelper.CreateEvent(new StudentCreated(studentId, "John"), studentTag));

        await _store.WriteEventAsync(
            EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, classRoomId), studentTag));

        // Act
        var result = await _store.GetLatestTagAsync(studentTag);

        // Assert
        Assert.True(result.IsSuccess);
        var tagState = result.GetValue();
        Assert.Equal(2, tagState.Version);
        Assert.Equal(((ITag)studentTag).GetTagGroup(), tagState.TagGroup);
        // TagContent should be just the content part (the GUID), not the full tag string
        var fullTag = studentTag.GetTag();
        var expectedContent = fullTag.Split(':')[1];
        Assert.Equal(expectedContent, tagState.TagContent);
    }

    [Fact]
    public async Task ReadTagsAsync_Should_Return_TagStream_For_Tag()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var classRoomId = Guid.NewGuid();

        await _store.WriteEventAsync(EventTestHelper.CreateEvent(new StudentCreated(studentId, "John"), studentTag));

        await _store.WriteEventAsync(
            EventTestHelper.CreateEvent(new StudentEnrolledInClassRoom(studentId, classRoomId), studentTag));

        // Act
        var result = await _store.ReadTagsAsync(studentTag);

        // Assert
        Assert.True(result.IsSuccess);
        var tagStreams = result.GetValue().ToList();
        Assert.Equal(2, tagStreams.Count);

        foreach (var stream in tagStreams)
        {
            Assert.Equal(studentTag.GetTag(), stream.Tag);
            Assert.NotEqual(Guid.Empty, stream.EventId);
            Assert.NotEmpty(stream.SortableUniqueId);
        }
    }

    // WriteTagAsync is no longer part of the interface - tags are written automatically with events
    // [Fact]
    // public async Task WriteTagAsync_Should_Write_New_Tag_State()
    // {
    //     // Arrange
    //     var studentId = Guid.NewGuid();
    //     var studentTag = new StudentTag(studentId);
    //     var tagState = new TagState(
    //         null!, // Payload
    //         1,
    //         SortableUniqueId.Generate(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), new Guid("00000000-0000-0000-0000-000000000064")),
    //         studentTag.GetTagGroup(),
    //         studentTag.GetTag(),
    //         "TestProjector"
    //     );
    //     
    //     // Act
    //     var writeResult = await _store.WriteTagAsync(studentTag, tagState);
    //     
    //     // Assert
    //     Assert.True(writeResult.IsSuccess);
    //     var writeResultValue = writeResult.GetValue();
    //     Assert.Equal(studentTag.GetTag(), writeResultValue.Tag);
    //     Assert.Equal(1, writeResultValue.Version);
    //     
    //     // Verify tag exists
    //     var exists = await _store.TagExistsAsync(studentTag);
    //     Assert.True(exists.GetValue());
    // }

    // WriteTagAsync is no longer part of the interface - tags are written automatically with events
    // [Fact]
    // public async Task WriteTagAsync_Should_Fail_For_Existing_Tag()
    // {
    //     // Arrange
    //     var studentId = Guid.NewGuid();
    //     var studentTag = new StudentTag(studentId);
    //     var eventPayload = new StudentCreated(studentId, "John", 5);
    //     
    //     // Write an event which creates the tag
    //     await _store.WriteEventAsync(EventTestHelper.CreateEvent(eventPayload, studentTag));
    //     
    //     var tagState = new TagState(
    //         null!,
    //         1,
    //         SortableUniqueId.Generate(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), new Guid("00000000-0000-0000-0000-000000000064")),
    //         studentTag.GetTagGroup(),
    //         studentTag.GetTag(),
    //         "TestProjector"
    //     );
    //     
    //     // Act
    //     var result = await _store.WriteTagAsync(studentTag, tagState);
    //     
    //     // Assert
    //     Assert.False(result.IsSuccess);
    // }

    [Fact]
    public async Task Events_Should_Be_Ordered_By_SortableUniqueId()
    {
        // Arrange
        var events = new List<(string name, Guid id)>();

        for (var i = 0; i < 5; i++)
        {
            var studentId = Guid.NewGuid();
            events.Add(($"Student {i}", studentId));
            await _store.WriteEventAsync(
                EventTestHelper.CreateEvent(new StudentCreated(studentId, $"Student {i}"), new StudentTag(studentId)));

            // Small delay to ensure different timestamps
            await Task.Delay(10);
        }

        // Act
        var result = await _store.ReadAllEventsAsync();

        // Assert
        Assert.True(result.IsSuccess);
        var allEvents = result.GetValue().ToList();
        Assert.Equal(5, allEvents.Count);

        // Verify ordering
        for (var i = 0; i < allEvents.Count - 1; i++)
        {
            var current = allEvents[i];
            var next = allEvents[i + 1];

            Assert.True(
                string.Compare(current.SortableUniqueIdValue, next.SortableUniqueIdValue, StringComparison.Ordinal) < 0,
                $"Events not properly ordered: {current.SortableUniqueIdValue} should be before {next.SortableUniqueIdValue}");
        }

        // Verify event payloads are in correct order
        for (var i = 0; i < 5; i++)
        {
            var payload = (StudentCreated)allEvents[i].Payload;
            Assert.Equal($"Student {i}", payload.Name);
        }
    }

    [Fact]
    public async Task Multiple_Tags_Should_Be_Updated_When_Event_Written()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var classRoomId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var classRoomTag = new ClassRoomTag(classRoomId);

        // Act
        await _store.WriteEventAsync(
            EventTestHelper.CreateEvent(
                new StudentEnrolledInClassRoom(studentId, classRoomId),
                new List<ITag> { studentTag, classRoomTag }));

        // Assert
        var studentExists = await _store.TagExistsAsync(studentTag);
        var classRoomExists = await _store.TagExistsAsync(classRoomTag);

        Assert.True(studentExists.GetValue());
        Assert.True(classRoomExists.GetValue());

        var studentState = await _store.GetLatestTagAsync(studentTag);
        var classRoomState = await _store.GetLatestTagAsync(classRoomTag);

        Assert.True(studentState.IsSuccess);
        Assert.True(classRoomState.IsSuccess);
        Assert.Equal(1, studentState.GetValue().Version);
        Assert.Equal(1, classRoomState.GetValue().Version);
    }

    [Fact]
    public async Task ReadEventsByTagAsync_Should_Return_Events_Ordered_By_SortableUniqueId()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var classRoomIds = new List<Guid>();

        // Create multiple enrollment events with delays to ensure different sortable IDs
        for (var i = 0; i < 5; i++)
        {
            var classRoomId = Guid.NewGuid();
            classRoomIds.Add(classRoomId);

            await _store.WriteEventAsync(
                EventTestHelper.CreateEvent(
                    new StudentEnrolledInClassRoom(studentId, classRoomId),
                    new List<ITag> { studentTag, new ClassRoomTag(classRoomId) }));

            await Task.Delay(10); // Ensure different timestamps
        }

        // Act
        var result = await _store.ReadEventsByTagAsync(studentTag);

        // Assert
        Assert.True(result.IsSuccess);
        var events = result.GetValue().ToList();
        Assert.Equal(5, events.Count);

        // Verify ordering by SortableUniqueId
        for (var i = 0; i < events.Count - 1; i++)
        {
            Assert.True(
                string.Compare(
                    events[i].SortableUniqueIdValue,
                    events[i + 1].SortableUniqueIdValue,
                    StringComparison.Ordinal) <
                0,
                $"Events not ordered: {events[i].SortableUniqueIdValue} should be before {events[i + 1].SortableUniqueIdValue}");
        }

        // Verify the events are in the order they were created
        for (var i = 0; i < 5; i++)
        {
            var enrollment = (StudentEnrolledInClassRoom)events[i].Payload;
            Assert.Equal(classRoomIds[i], enrollment.ClassRoomId);
        }
    }

    [Fact]
    public async Task ReadEventsByTagAsync_With_Since_Should_Return_Ordered_Events_After_Id()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var eventIds = new List<Guid>();
        var sortableIds = new List<string>();

        // Create 5 events
        for (var i = 0; i < 5; i++)
        {
            var writeResult = await _store.WriteEventAsync(
                EventTestHelper.CreateEvent(new StudentCreated(studentId, $"Student Version {i}"), studentTag));

            var eventId = writeResult.GetValue().Id;
            eventIds.Add(eventId);

            var evt = (await _store.ReadEventAsync(eventId)).GetValue();
            sortableIds.Add(evt.SortableUniqueIdValue);

            await Task.Delay(10);
        }

        // Use the second event's sortable ID as "since"
        var since = new SortableUniqueId(sortableIds[1]);

        // Act
        var result = await _store.ReadEventsByTagAsync(studentTag, since);

        // Assert
        Assert.True(result.IsSuccess);
        var events = result.GetValue().ToList();
        Assert.Equal(3, events.Count); // Events 2, 3, 4 (0-indexed)

        // Verify all returned events are after the "since" ID
        foreach (var evt in events)
        {
            Assert.True(
                string.Compare(evt.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0,
                $"Event {evt.SortableUniqueIdValue} should be after {since.Value}");
        }

        // Verify ordering
        for (var i = 0; i < events.Count - 1; i++)
        {
            Assert.True(
                string.Compare(
                    events[i].SortableUniqueIdValue,
                    events[i + 1].SortableUniqueIdValue,
                    StringComparison.Ordinal) <
                0,
                $"Events not ordered: {events[i].SortableUniqueIdValue} should be before {events[i + 1].SortableUniqueIdValue}");
        }

        // Verify we got the correct events (versions 2, 3, 4)
        for (var i = 0; i < 3; i++)
        {
            var payload = (StudentCreated)events[i].Payload;
            Assert.Equal($"Student Version {i + 2}", payload.Name);
        }
    }

    [Fact]
    public async Task All_Read_Methods_Should_Return_Same_Ordering()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var classRoomId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var classRoomTag = new ClassRoomTag(classRoomId);

        // Create events in specific order
        await _store.WriteEventAsync(EventTestHelper.CreateEvent(new StudentCreated(studentId, "John"), studentTag));
        await Task.Delay(10);

        await _store.WriteEventAsync(
            EventTestHelper.CreateEvent(new ClassRoomCreated(classRoomId, "Math", 20), classRoomTag));
        await Task.Delay(10);

        await _store.WriteEventAsync(
            EventTestHelper.CreateEvent(
                new StudentEnrolledInClassRoom(studentId, classRoomId),
                new List<ITag> { studentTag, classRoomTag }));
        await Task.Delay(10);

        await _store.WriteEventAsync(
            EventTestHelper.CreateEvent(
                new StudentDroppedFromClassRoom(studentId, classRoomId),
                new List<ITag> { studentTag, classRoomTag }));

        // Act
        var allEvents = (await _store.ReadAllEventsAsync()).GetValue().ToList();
        var studentEvents = (await _store.ReadEventsByTagAsync(studentTag)).GetValue().ToList();
        var classRoomEvents = (await _store.ReadEventsByTagAsync(classRoomTag)).GetValue().ToList();

        // Assert - All events should be ordered
        Assert.Equal(4, allEvents.Count);
        AssertEventsOrdered(allEvents);

        // Student events should be ordered
        Assert.Equal(3, studentEvents.Count); // Created, Enrolled, Dropped
        AssertEventsOrdered(studentEvents);

        // ClassRoom events should be ordered
        Assert.Equal(3, classRoomEvents.Count); // Created, Enrolled, Dropped
        AssertEventsOrdered(classRoomEvents);

        // Verify the specific order of event types in allEvents
        Assert.IsType<StudentCreated>(allEvents[0].Payload);
        Assert.IsType<ClassRoomCreated>(allEvents[1].Payload);
        Assert.IsType<StudentEnrolledInClassRoom>(allEvents[2].Payload);
        Assert.IsType<StudentDroppedFromClassRoom>(allEvents[3].Payload);

        // Verify student events are in correct order
        Assert.IsType<StudentCreated>(studentEvents[0].Payload);
        Assert.IsType<StudentEnrolledInClassRoom>(studentEvents[1].Payload);
        Assert.IsType<StudentDroppedFromClassRoom>(studentEvents[2].Payload);

        // Verify classroom events are in correct order
        Assert.IsType<ClassRoomCreated>(classRoomEvents[0].Payload);
        Assert.IsType<StudentEnrolledInClassRoom>(classRoomEvents[1].Payload);
        Assert.IsType<StudentDroppedFromClassRoom>(classRoomEvents[2].Payload);
    }

    private void AssertEventsOrdered(List<Event> events)
    {
        for (var i = 0; i < events.Count - 1; i++)
        {
            Assert.True(
                string.Compare(
                    events[i].SortableUniqueIdValue,
                    events[i + 1].SortableUniqueIdValue,
                    StringComparison.Ordinal) <
                0,
                $"Events not properly ordered at index {i}: {events[i].SortableUniqueIdValue} should be before {events[i + 1].SortableUniqueIdValue}");
        }
    }
}
