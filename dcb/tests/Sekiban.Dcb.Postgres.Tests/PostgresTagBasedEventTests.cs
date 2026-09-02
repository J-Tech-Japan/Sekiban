using Dcb.Domain.ClassRoom;
using Dcb.Domain.Enrollment;
using Dcb.Domain.Student;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;
namespace Sekiban.Dcb.Postgres.Tests;

public class PostgresTagBasedEventTests : PostgresTestBase
{
    private static readonly DateTime TaggedStreamBaseTime = new(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string TaggedStreamP0 = SortableUniqueId.Generate(TaggedStreamBaseTime.AddSeconds(-1), Guid.Empty);
    private static readonly string TaggedStreamP1 = SortableUniqueId.Generate(TaggedStreamBaseTime, Guid.Empty);
    private static readonly string TaggedStreamP2 = SortableUniqueId.Generate(TaggedStreamBaseTime.AddSeconds(1), Guid.Empty);
    private static readonly string TaggedStreamP3 = SortableUniqueId.Generate(TaggedStreamBaseTime.AddSeconds(2), Guid.Empty);
    private static readonly string TaggedStreamP4 = SortableUniqueId.Generate(TaggedStreamBaseTime.AddSeconds(3), Guid.Empty);
    private static readonly string TaggedStreamP5 = SortableUniqueId.Generate(TaggedStreamBaseTime.AddSeconds(4), Guid.Empty);

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
    public async Task TaggedStream_SinceIsExclusive_UntilIsInclusive_AndPreservesOrdinalOrder()
    {
        var studentId = Guid.NewGuid();
        var tag = new StudentTag(studentId);
        var events = new[]
        {
            new Event(
                new StudentCreated(studentId, "before-since"),
                TaggedStreamP0,
                nameof(StudentCreated),
                Guid.NewGuid(),
                new EventMetadata("cause", "correlation", "user"),
                new List<string> { tag.GetTag() }),
            new Event(
                new StudentCreated(studentId, "one"),
                TaggedStreamP1,
                nameof(StudentCreated),
                Guid.NewGuid(),
                new EventMetadata("cause", "correlation", "user"),
                new List<string> { tag.GetTag() }),
            new Event(
                new StudentCreated(studentId, "two"),
                TaggedStreamP2,
                nameof(StudentCreated),
                Guid.NewGuid(),
                new EventMetadata("cause", "correlation", "user"),
                new List<string> { tag.GetTag() }),
            new Event(
                new StudentCreated(studentId, "three"),
                TaggedStreamP3,
                nameof(StudentCreated),
                Guid.NewGuid(),
                new EventMetadata("cause", "correlation", "user"),
                new List<string> { tag.GetTag() }),
            new Event(
                new StudentCreated(studentId, "after-until"),
                TaggedStreamP4,
                nameof(StudentCreated),
                Guid.NewGuid(),
                new EventMetadata("cause", "correlation", "user"),
                new List<string> { tag.GetTag() })
        };
        var write = await Fixture.EventStore.WriteEventsAsync(events);
        Assert.True(write.IsSuccess, write.IsSuccess ? string.Empty : write.GetException().ToString());

        var stream = Assert.IsAssignableFrom<IStreamingTaggedSerializableEventStore>(Fixture.EventStore);
        var emitted = new List<string>();
        var result = await stream.StreamSerializableEventsByTagAsync(
            tag,
            new SortableUniqueId(TaggedStreamP1),
            new SortableUniqueId(TaggedStreamP3),
            serializableEvent =>
            {
                emitted.Add(serializableEvent.SortableUniqueIdValue);
                return ValueTask.CompletedTask;
            });

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
        Assert.Equal(new[] { TaggedStreamP2, TaggedStreamP3 }, emitted);
        Assert.Equal(2, result.GetValue().EventsRead);
        Assert.Equal(TaggedStreamP3, result.GetValue().LastSortableUniqueId);
    }

    [Fact]
    public async Task TaggedStream_CapturedHeadSnapshot_ExcludesTheAppendAboveHeadReleasedDuringTheNativeQuery()
    {
        var studentId = Guid.NewGuid();
        var tag = new StudentTag(studentId);
        var initial = new[]
        {
            TaggedStreamEvent(studentId, tag, "before-since", TaggedStreamP0),
            TaggedStreamEvent(studentId, tag, "since", TaggedStreamP1),
            TaggedStreamEvent(studentId, tag, "middle", TaggedStreamP2),
            TaggedStreamEvent(studentId, tag, "until", TaggedStreamP3)
        };
        var initialWrite = await Fixture.EventStore.WriteEventsAsync(initial);
        Assert.True(initialWrite.IsSuccess, initialWrite.IsSuccess ? string.Empty : initialWrite.GetException().ToString());

        var head = await Fixture.EventStore.GetLatestSortableUniqueIdAsync();
        Assert.True(head.IsSuccess, head.IsSuccess ? string.Empty : head.GetException().ToString());
        Assert.Equal(TaggedStreamP3, head.GetValue());
        var capturedHead = new SortableUniqueId(head.GetValue());

        // The five-point after-until boundary is already durable before the real query begins. A second above-head event
        // is appended only after its first callback proves the native query/reader is active.
        var afterUntilWrite = await Fixture.EventStore.WriteEventsAsync(
            new[] { TaggedStreamEvent(studentId, tag, "after-until", TaggedStreamP4) });
        Assert.True(afterUntilWrite.IsSuccess, afterUntilWrite.IsSuccess ? string.Empty : afterUntilWrite.GetException().ToString());

        var nativeQueryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var emitted = new List<string>();
        var callbackCount = 0;
        var stream = Assert.IsAssignableFrom<IStreamingTaggedSerializableEventStore>(Fixture.EventStore);
        var streamTask = stream.StreamSerializableEventsByTagAsync(
            tag,
            new SortableUniqueId(TaggedStreamP1),
            capturedHead,
            async serializableEvent =>
            {
                emitted.Add(serializableEvent.SortableUniqueIdValue);
                if (Interlocked.Increment(ref callbackCount) == 1)
                {
                    nativeQueryStarted.TrySetResult();
                    await releaseCallback.Task;
                }
            });

        try
        {
            await nativeQueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var aboveHeadWrite = await Fixture.EventStore.WriteEventsAsync(
                    new[] { TaggedStreamEvent(studentId, tag, "appended-above-head", TaggedStreamP5) })
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(aboveHeadWrite.IsSuccess, aboveHeadWrite.IsSuccess ? string.Empty : aboveHeadWrite.GetException().ToString());
        }
        finally
        {
            releaseCallback.TrySetResult();
        }

        var result = await streamTask;
        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
        Assert.Equal(new[] { TaggedStreamP2, TaggedStreamP3 }, emitted);
        Assert.Equal(emitted.OrderBy(id => id, StringComparer.Ordinal), emitted);
        Assert.DoesNotContain(TaggedStreamP4, emitted);
        Assert.DoesNotContain(TaggedStreamP5, emitted);
    }

    [Fact]
    public async Task TaggedStream_GatedNativeReader_CancellationBeforeReadReachesPostgresIoWithoutReadingARow()
    {
        var studentId = Guid.NewGuid();
        var tag = new StudentTag(studentId);
        var store = new PostgresEventStore(
            Fixture.DbContextFactory,
            Fixture.DomainTypes.EventTypes,
            new DefaultServiceIdProvider());
        var write = await store.WriteEventsAsync(new[]
        {
            TaggedStreamEvent(studentId, tag, "one", TaggedStreamP1),
            TaggedStreamEvent(studentId, tag, "two", TaggedStreamP2)
        });
        Assert.True(write.IsSuccess, write.IsSuccess ? string.Empty : write.GetException().ToString());

        var readerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var beforeReadAttempts = 0;
        var rowsRead = 0;
        var callbacks = 0;
        store.BeforeTaggedStreamReaderReadHook = async () =>
        {
            Interlocked.Increment(ref beforeReadAttempts);
            readerStarted.TrySetResult();
            await releaseReader.Task;
        };
        store.AfterTaggedStreamReaderReadHook = () =>
        {
            Interlocked.Increment(ref rowsRead);
            return Task.CompletedTask;
        };

        using var cancellation = new CancellationTokenSource();
        try
        {
            var streamTask = ((IStreamingTaggedSerializableEventStore)store).StreamSerializableEventsByTagAsync(
                tag,
                null,
                null,
                _ =>
                {
                    callbacks++;
                    return ValueTask.CompletedTask;
                },
                cancellation.Token);

            await readerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            releaseReader.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await streamTask);
        }
        finally
        {
            releaseReader.TrySetResult();
            store.BeforeTaggedStreamReaderReadHook = null;
            store.AfterTaggedStreamReaderReadHook = null;
        }

        Assert.Equal(1, beforeReadAttempts);
        Assert.Equal(0, rowsRead);
        Assert.Equal(0, callbacks);
    }

    [Fact]
    public async Task TaggedStream_GatedNativeReader_CancellationAfterFirstCallbackDoesNotAttemptALaterPostgresRow()
    {
        var studentId = Guid.NewGuid();
        var tag = new StudentTag(studentId);
        var store = new PostgresEventStore(
            Fixture.DbContextFactory,
            Fixture.DomainTypes.EventTypes,
            new DefaultServiceIdProvider());
        var write = await store.WriteEventsAsync(new[]
        {
            TaggedStreamEvent(studentId, tag, "one", TaggedStreamP1),
            TaggedStreamEvent(studentId, tag, "two", TaggedStreamP2)
        });
        Assert.True(write.IsSuccess, write.IsSuccess ? string.Empty : write.GetException().ToString());

        var readerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var beforeReadAttempts = 0;
        var rowsRead = 0;
        var callbacks = 0;
        store.BeforeTaggedStreamReaderReadHook = async () =>
        {
            if (Interlocked.Increment(ref beforeReadAttempts) == 1)
            {
                readerStarted.TrySetResult();
                await releaseReader.Task;
            }
        };
        store.AfterTaggedStreamReaderReadHook = () =>
        {
            Interlocked.Increment(ref rowsRead);
            return Task.CompletedTask;
        };

        using var cancellation = new CancellationTokenSource();
        try
        {
            var streamTask = ((IStreamingTaggedSerializableEventStore)store).StreamSerializableEventsByTagAsync(
                tag,
                null,
                null,
                async _ =>
                {
                    Interlocked.Increment(ref callbacks);
                    callbackStarted.TrySetResult();
                    await releaseCallback.Task;
                },
                cancellation.Token);

            await readerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            releaseReader.TrySetResult();
            await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            releaseCallback.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await streamTask);
        }
        finally
        {
            releaseReader.TrySetResult();
            releaseCallback.TrySetResult();
            store.BeforeTaggedStreamReaderReadHook = null;
            store.AfterTaggedStreamReaderReadHook = null;
        }

        Assert.Equal(1, beforeReadAttempts);
        Assert.Equal(1, rowsRead);
        Assert.Equal(1, callbacks);
    }

    [Fact]
    public async Task TaggedStream_CancellationAfterFirstCallbackStopsBeforeTheNextPostgresRow()
    {
        var studentId = Guid.NewGuid();
        var tag = new StudentTag(studentId);
        var start = TaggedStreamBaseTime.AddDays(1);
        var events = Enumerable.Range(0, 3)
            .Select(index => new Event(
                new StudentCreated(studentId, $"cancellation-{index}"),
                SortableUniqueId.Generate(start.AddSeconds(index), Guid.Empty),
                nameof(StudentCreated),
                Guid.NewGuid(),
                new EventMetadata("cause", "correlation", "user"),
                new List<string> { tag.GetTag() }))
            .ToArray();
        var write = await Fixture.EventStore.WriteEventsAsync(events);
        Assert.True(write.IsSuccess, write.IsSuccess ? string.Empty : write.GetException().ToString());

        using var cancellation = new CancellationTokenSource();
        var callbacks = 0;
        var stream = Assert.IsAssignableFrom<IStreamingTaggedSerializableEventStore>(Fixture.EventStore);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.StreamSerializableEventsByTagAsync(
            tag,
            null,
            null,
            _ =>
            {
                callbacks++;
                cancellation.Cancel();
                return ValueTask.CompletedTask;
            },
            cancellation.Token));

        Assert.Equal(1, callbacks);
    }

    private static Event TaggedStreamEvent(Guid studentId, StudentTag tag, string name, string sortableUniqueId) =>
        new(
            new StudentCreated(studentId, name),
            sortableUniqueId,
            nameof(StudentCreated),
            Guid.NewGuid(),
            new EventMetadata("cause", "correlation", "user"),
            new List<string> { tag.GetTag() });

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
