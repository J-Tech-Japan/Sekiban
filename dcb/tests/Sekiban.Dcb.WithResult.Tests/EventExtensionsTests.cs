using Dcb.Domain.Student;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for EventExtensions
/// </summary>
public class EventExtensionsTests
{
    [Fact]
    public void GetTimestamp_Should_Return_DateTimeOffset_From_SortableUniqueId()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var eventPayload = new StudentCreated(studentId, "John Doe");
        var sortableId = SortableUniqueId.GenerateNew();
        var tags = new List<string> { new StudentTag(studentId).GetTag() };

        var evt = new Event(
            eventPayload,
            sortableId,
            nameof(StudentCreated),
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), "TestCommand", "TestUser"),
            tags);

        // Act
        var timestamp = evt.GetTimestamp();

        // Assert
        // DateTimeOffset.DateTime.Kind is typically Unspecified even when created from UTC DateTime
        // What matters is the Offset, which should be Zero for UTC
        Assert.Equal(TimeSpan.Zero, timestamp.Offset);

        // Verify it matches the SortableUniqueId's embedded timestamp
        var sortableIdObj = new SortableUniqueId(evt.SortableUniqueIdValue);
        var expectedDateTime = sortableIdObj.GetDateTime();
        Assert.Equal(expectedDateTime, timestamp.DateTime);
    }

    [Fact]
    public void GetTimestamp_Should_Match_SortableUniqueId_GetDateTime()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var eventPayload = new StudentCreated(studentId, "Jane Doe");
        var sortableId = SortableUniqueId.GenerateNew();
        var tags = new List<string> { new StudentTag(studentId).GetTag() };

        var evt = new Event(
            eventPayload,
            sortableId,
            nameof(StudentCreated),
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), "TestCommand", "TestUser"),
            tags);

        // Act
        var timestampFromExtension = evt.GetTimestamp();
        var sortableIdObj = new SortableUniqueId(evt.SortableUniqueIdValue);
        var timestampFromSortableId = new DateTimeOffset(sortableIdObj.GetDateTime(), TimeSpan.Zero);

        // Assert
        Assert.Equal(timestampFromSortableId, timestampFromExtension);
    }

    [Fact]
    public void GetTimestamp_Should_Be_Consistent_Across_Multiple_Calls()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var eventPayload = new StudentCreated(studentId, "Bob Smith");
        var sortableId = SortableUniqueId.GenerateNew();
        var tags = new List<string> { new StudentTag(studentId).GetTag() };

        var evt = new Event(
            eventPayload,
            sortableId,
            nameof(StudentCreated),
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), "TestCommand", "TestUser"),
            tags);

        // Act
        var timestamp1 = evt.GetTimestamp();
        var timestamp2 = evt.GetTimestamp();

        // Assert
        Assert.Equal(timestamp1, timestamp2);
    }

    [Fact]
    public void GetTimestamp_Should_Return_Earlier_Time_For_Earlier_Events()
    {
        // Arrange
        var studentId1 = Guid.NewGuid();
        var studentId2 = Guid.NewGuid();

        // Create first event
        var sortableId1 = SortableUniqueId.GenerateNew();
        var event1 = new Event(
            new StudentCreated(studentId1, "First Student"),
            sortableId1,
            nameof(StudentCreated),
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), "TestCommand", "TestUser"),
            new List<string> { new StudentTag(studentId1).GetTag() });

        // Wait a tiny bit to ensure different timestamps
        Thread.Sleep(2);

        // Create second event
        var sortableId2 = SortableUniqueId.GenerateNew();
        var event2 = new Event(
            new StudentCreated(studentId2, "Second Student"),
            sortableId2,
            nameof(StudentCreated),
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), "TestCommand", "TestUser"),
            new List<string> { new StudentTag(studentId2).GetTag() });

        // Act
        var timestamp1 = event1.GetTimestamp();
        var timestamp2 = event2.GetTimestamp();

        // Assert
        Assert.True(timestamp1 < timestamp2, "First event should have earlier timestamp");
    }
}
