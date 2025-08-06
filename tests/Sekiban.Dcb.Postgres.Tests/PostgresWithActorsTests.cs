using Xunit;
using FluentAssertions;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.InMemory;
using Dcb.Domain;

namespace Sekiban.Dcb.Postgres.Tests;

public class PostgresWithActorsTests : PostgresTestBase
{
    public PostgresWithActorsTests(PostgresTestFixture fixture) : base(fixture)
    {
    }
    
    [Fact]
    public async Task Should_Execute_Commands_With_InMemory_Actors_And_Postgres_Storage()
    {
        // Arrange
        var commandExecutor = new InMemoryCommandExecutor(
            Fixture.EventStore,
            Fixture.ActorAccessor,
            Fixture.DomainTypes);
        
        var studentId = Guid.NewGuid();
        var classRoomId = Guid.NewGuid();
        
        // Act - Create student
        var createStudentResult = await commandExecutor.ExecuteAsync(
            new CreateStudent(studentId, "John Doe", 5));
        
        // Assert
        createStudentResult.IsSuccess.Should().BeTrue();
        createStudentResult.GetValue().TagWrites.Should().NotBeEmpty();
        
        // Act - Create classroom
        var createClassRoomResult = await commandExecutor.ExecuteAsync(
            new CreateClassRoom(classRoomId, "Physics 101", 20),
            new CreateClassRoomHandler());
        
        // Assert
        createClassRoomResult.IsSuccess.Should().BeTrue();
        
        // Act - Enroll student
        var enrollResult = await commandExecutor.ExecuteAsync(
            new EnrollStudentInClassRoom(studentId, classRoomId),
            new EnrollStudentInClassRoomHandler());
        
        // Assert
        enrollResult.IsSuccess.Should().BeTrue();
        enrollResult.GetValue().TagWrites.Should().NotBeEmpty();
        
        // Verify events are persisted in PostgreSQL
        var studentTag = new StudentTag(studentId);
        var eventsResult = await Fixture.EventStore.ReadEventsByTagAsync(studentTag);
        
        eventsResult.IsSuccess.Should().BeTrue();
        var events = eventsResult.GetValue().ToList();
        events.Should().HaveCount(2); // StudentCreated and StudentEnrolledInClassRoom
    }
    
    [Fact]
    public async Task Should_Handle_Concurrent_Commands_With_Actors()
    {
        // Arrange
        var commandExecutor = new InMemoryCommandExecutor(
            Fixture.EventStore,
            Fixture.ActorAccessor,
            Fixture.DomainTypes);
        
        var classRoomId = Guid.NewGuid();
        
        // Create classroom
        await commandExecutor.ExecuteAsync(
            new CreateClassRoom(classRoomId, "Limited Seats", 2),
            new CreateClassRoomHandler());
        
        // Create multiple students
        var studentIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        
        foreach (var studentId in studentIds)
        {
            await commandExecutor.ExecuteAsync(
                new CreateStudent(studentId, $"Student {studentId}", 5));
        }
        
        // Act - Try to enroll all students concurrently
        var enrollTasks = studentIds.Select(studentId =>
            commandExecutor.ExecuteAsync(
                new EnrollStudentInClassRoom(studentId, classRoomId),
                new EnrollStudentInClassRoomHandler())
        ).ToList();
        
        var results = await Task.WhenAll(enrollTasks);
        
        // Assert - Only 2 should succeed (max students = 2)
        var successCount = results.Count(r => r.IsSuccess);
        var failureCount = results.Count(r => !r.IsSuccess);
        
        successCount.Should().Be(2);
        failureCount.Should().Be(3);
        
        // Verify in database
        var classRoomTag = new ClassRoomTag(classRoomId);
        var eventsResult = await Fixture.EventStore.ReadEventsByTagAsync(classRoomTag);
        
        eventsResult.IsSuccess.Should().BeTrue();
        var events = eventsResult.GetValue().ToList();
        events.Should().HaveCount(3); // 1 ClassRoomCreated + 2 StudentEnrolledInClassRoom
    }
    
    [Fact]
    public async Task Should_Maintain_Actor_State_Consistency_With_Database()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        
        // Create student via command executor
        var commandExecutor = new InMemoryCommandExecutor(
            Fixture.EventStore,
            Fixture.ActorAccessor,
            Fixture.DomainTypes);
        
        await commandExecutor.ExecuteAsync(
            new CreateStudent(studentId, "Test Student", 5));
        
        // Act - Get actor directly
        var tagConsistentActorId = studentTag.GetTag();
        var actorResult = await Fixture.ActorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);
        
        // Assert - Actor should exist and have correct state
        actorResult.IsSuccess.Should().BeTrue();
        var actor = actorResult.GetValue();
        
        var latestIdResult = await actor.GetLatestSortableUniqueIdAsync();
        latestIdResult.IsSuccess.Should().BeTrue();
        latestIdResult.GetValue().Should().NotBeEmpty();
        
        // Verify the same data is in database
        var tagExistsResult = await Fixture.EventStore.TagExistsAsync(studentTag);
        tagExistsResult.IsSuccess.Should().BeTrue();
        tagExistsResult.GetValue().Should().BeTrue();
        
        var latestTagResult = await Fixture.EventStore.GetLatestTagAsync(studentTag);
        latestTagResult.IsSuccess.Should().BeTrue();
        latestTagResult.GetValue().LastSortedUniqueId.Should().Be(latestIdResult.GetValue());
    }
    
    [Fact]
    public async Task Should_Recreate_Actor_State_From_Database_After_Removal()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var tagConsistentActorId = studentTag.GetTag();
        
        // Create student
        var commandExecutor = new InMemoryCommandExecutor(
            Fixture.EventStore,
            Fixture.ActorAccessor,
            Fixture.DomainTypes);
        
        await commandExecutor.ExecuteAsync(
            new CreateStudent(studentId, "Persistent Student", 5));
        
        // Get initial actor state
        var actor1Result = await Fixture.ActorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);
        actor1Result.IsSuccess.Should().BeTrue();
        var actor1 = actor1Result.GetValue();
        var latestId1Result = await actor1.GetLatestSortableUniqueIdAsync();
        latestId1Result.IsSuccess.Should().BeTrue();
        var latestId1 = latestId1Result.GetValue();
        
        // Act - Remove actor from memory
        var removed = Fixture.ActorAccessor.RemoveActor(tagConsistentActorId);
        removed.Should().BeTrue();
        
        // Get actor again (should recreate from database)
        var actor2Result = await Fixture.ActorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);
        actor2Result.IsSuccess.Should().BeTrue();
        var actor2 = actor2Result.GetValue();
        
        // Assert - New actor should have same state from database
        var latestId2Result = await actor2.GetLatestSortableUniqueIdAsync();
        latestId2Result.IsSuccess.Should().BeTrue();
        latestId2Result.GetValue().Should().Be(latestId1);
        
        // Verify actors are different instances
        actor1.Should().NotBeSameAs(actor2);
    }
}