using Dcb.Domain.ClassRoom;
using Dcb.Domain.Student;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Xunit;
using Xunit.Abstractions;
namespace Sekiban.Dcb.Postgres.Tests;

public class DebugDatabaseContentTest : PostgresTestBase
{
    private readonly ITestOutputHelper _output;

    public DebugDatabaseContentTest(PostgresTestFixture fixture, ITestOutputHelper output) : base(fixture) =>
        _output = output;

    [Fact]
    public async Task Debug_Database_Content_After_Write()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var classRoomId = Guid.NewGuid();

        var studentTag = new StudentTag(studentId);
        var classRoomTag = new ClassRoomTag(classRoomId);

        var events = new List<Event>
        {
            new(
                new StudentCreated(studentId, "Test Student"),
                SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()),
                nameof(StudentCreated),
                Guid.NewGuid(),
                new EventMetadata("cause1", "corr1", "user1"),
                new List<string> { studentTag.GetTag() }),
            new(
                new ClassRoomCreated(classRoomId, "Test Class", 30),
                SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()),
                nameof(ClassRoomCreated),
                Guid.NewGuid(),
                new EventMetadata("cause2", "corr2", "user1"),
                new List<string> { classRoomTag.GetTag() })
        };

        // Act - Write events
        var writeResult = await Fixture.EventStore.WriteEventsAsync(events);
        writeResult.IsSuccess.Should().BeTrue();

        // Debug - Check database content directly
        await using var context = await Fixture.GetDbContextAsync();

        // Check Events table
        var dbEvents = await context.Events.ToListAsync();
        _output.WriteLine($"Events in database: {dbEvents.Count}");
        foreach (var dbEvent in dbEvents)
        {
            _output.WriteLine(
                $"  Event: Id={dbEvent.Id}, Type={dbEvent.EventType}, SortableUniqueId={dbEvent.SortableUniqueId}, Tags={dbEvent.Tags}");
        }

        // Check Tags table
        var dbTags = await context.Tags.ToListAsync();
        _output.WriteLine($"Tags in database: {dbTags.Count}");
        foreach (var dbTag in dbTags)
        {
            _output.WriteLine(
                $"  Tag: Id={dbTag.Id}, Tag={dbTag.Tag}, EventId={dbTag.EventId}, SortableUniqueId={dbTag.SortableUniqueId}");
        }

        // Assertions
        dbEvents.Should().HaveCount(2);
        dbTags.Should().HaveCount(2);

        // Keep data for manual inspection (don't clear)
        _output.WriteLine("\nData kept in database for manual inspection.");
    }

    public override Task InitializeAsync() =>
        // Don't clear database for this debug test
        Task.CompletedTask;
}
