using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.Enrollment;
using Dcb.Domain.WithoutResult.Queries;
using Dcb.Domain.WithoutResult.Student;
using Sekiban.Dcb;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.WithoutResult.Tests;

/// <summary>
/// Comprehensive tests for WithoutResult (exception-based) API
/// Tests: Commands, Events, Projections, and Queries
/// </summary>
public class ComprehensiveWithoutResultTests
{
    private readonly ISekibanExecutor _executor;
    private readonly DcbDomainTypes _domainTypes;

    public ComprehensiveWithoutResultTests()
    {
        _domainTypes = DomainType.GetDomainTypes();
        _executor = new InMemoryDcbExecutor(_domainTypes);
    }

    #region Command Tests

    [Fact]
    public async Task Command_CreateStudent_Should_Succeed()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var command = new CreateStudent(studentId, "Test Student", 5);

        // Act
        var result = await _executor.ExecuteAsync(command);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Events);

        var evt = result.Events.First();
        Assert.IsType<StudentCreated>(evt.Payload);
        var studentCreated = (StudentCreated)evt.Payload;
        Assert.Equal(studentId, studentCreated.StudentId);
        Assert.Equal("Test Student", studentCreated.Name);
        Assert.Equal(5, studentCreated.MaxClassCount);
    }

    [Fact]
    public async Task Command_WithInvalidData_Should_ThrowException()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var command = new CreateStudent(studentId, "", 5); // Empty name should fail

        // Act & Assert
        await Assert.ThrowsAsync<Sekiban.Dcb.Validation.SekibanValidationException>(() => _executor.ExecuteAsync(command));
    }

    [Fact]
    public async Task Command_WithDuplicateStudent_Should_ThrowException()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var command1 = new CreateStudent(studentId, "Student 1", 5);
        var command2 = new CreateStudent(studentId, "Student 2", 3);

        // Act
        await _executor.ExecuteAsync(command1);

        // Assert
        await Assert.ThrowsAsync<ApplicationException>(() => _executor.ExecuteAsync(command2));
    }

    #endregion

    #region Event Tests

    [Fact]
    public async Task Event_StudentCreated_Should_BePersistedCorrectly()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var command = new CreateStudent(studentId, "Event Test Student", 4);

        // Act
        var result = await _executor.ExecuteAsync(command);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Events);

        var persistedEvent = result.Events.First();
        Assert.NotNull(persistedEvent.SortableUniqueIdValue);
        Assert.NotEmpty(persistedEvent.SortableUniqueIdValue);
        Assert.Equal(typeof(StudentCreated).Name, persistedEvent.EventType);
    }

    #endregion

    #region Projection Tests

    [Fact]
    public async Task Projection_StudentState_Should_BePopulatedCorrectly()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var command = new CreateStudent(studentId, "Projection Test", 3);

        // Act
        await _executor.ExecuteAsync(command);

        // Assert
        var tagStateId = TagStateId.FromProjector<StudentProjector>(new StudentTag(studentId));
        var tagState = await _executor.GetTagStateAsync(tagStateId);

        Assert.NotNull(tagState);
        Assert.IsType<StudentState>(tagState.Payload);

        var studentState = (StudentState)tagState.Payload;
        Assert.Equal(studentId, studentState.StudentId);
        Assert.Equal("Projection Test", studentState.Name);
        Assert.Equal(3, studentState.MaxClassCount);
        Assert.Empty(studentState.EnrolledClassRoomIds);
        Assert.Equal(1, tagState.Version);
    }

    [Fact]
    public async Task Projection_MultipleEvents_Should_UpdateStateCorrectly()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var createCommand = new CreateStudent(studentId, "Multi Event Test", 5);
        var classRoomId = Guid.NewGuid();

        // Act
        await _executor.ExecuteAsync(createCommand);

        // Manually append enrollment event through command handler
        var enrollCommand = new EnrollInClassRoom(studentId, classRoomId);
        await _executor.ExecuteAsync(enrollCommand);

        // Assert
        var tagStateId = TagStateId.FromProjector<StudentProjector>(new StudentTag(studentId));
        var tagState = await _executor.GetTagStateAsync(tagStateId);

        var studentState = (StudentState)tagState.Payload;
        Assert.Single(studentState.EnrolledClassRoomIds);
        Assert.Contains(classRoomId, studentState.EnrolledClassRoomIds);
        Assert.Equal(2, tagState.Version);
    }

    #endregion

    #region Query Tests

    [Fact]
    public async Task Query_GetStudentList_Should_ReturnAllStudents()
    {
        // Arrange
        var student1Id = Guid.NewGuid();
        var student2Id = Guid.NewGuid();
        var student3Id = Guid.NewGuid();

        await _executor.ExecuteAsync(new CreateStudent(student1Id, "Alice", 3));
        await _executor.ExecuteAsync(new CreateStudent(student2Id, "Bob", 4));
        await _executor.ExecuteAsync(new CreateStudent(student3Id, "Charlie", 5));

        // Act
        var query = new GetStudentListQuery { PageNumber = 1, PageSize = 10 };
        var result = await _executor.QueryAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count());

        var studentNames = result.Items.Select(s => s.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "Alice", "Bob", "Charlie" }, studentNames);
    }

    [Fact]
    public async Task Query_WithPagination_Should_ReturnCorrectPage()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            await _executor.ExecuteAsync(new CreateStudent(Guid.NewGuid(), $"Student{i:D2}", 3));
        }

        // Act
        var page1Query = new GetStudentListQuery { PageNumber = 1, PageSize = 3 };
        var page1Result = await _executor.QueryAsync(page1Query);

        var page2Query = new GetStudentListQuery { PageNumber = 2, PageSize = 3 };
        var page2Result = await _executor.QueryAsync(page2Query);

        // Assert
        Assert.Equal(10, page1Result.TotalCount);
        Assert.Equal(3, page1Result.Items.Count());

        Assert.Equal(10, page2Result.TotalCount);
        Assert.Equal(3, page2Result.Items.Count());

        // Ensure different pages return different students
        var page1Names = page1Result.Items.Select(s => s.Name).ToHashSet();
        var page2Names = page2Result.Items.Select(s => s.Name).ToHashSet();
        Assert.Empty(page1Names.Intersect(page2Names));
    }

    [Fact]
    public async Task Query_EmptyDatabase_Should_ReturnEmptyResult()
    {
        // Act
        var query = new GetStudentListQuery { PageNumber = 1, PageSize = 10 };
        var result = await _executor.QueryAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task Integration_FullWorkflow_CreateQueryAndVerify()
    {
        // Arrange
        var students = new[]
        {
            (Guid.NewGuid(), "Alice", 5),
            (Guid.NewGuid(), "Bob", 4),
            (Guid.NewGuid(), "Charlie", 3)
        };

        // Act - Create students
        foreach (var (id, name, maxCount) in students)
        {
            await _executor.ExecuteAsync(new CreateStudent(id, name, maxCount));
        }

        // Act - Query students
        var listQuery = new GetStudentListQuery { PageNumber = 1, PageSize = 10 };
        var listResult = await _executor.QueryAsync(listQuery);

        // Assert - Verify list
        Assert.Equal(students.Length, listResult.TotalCount);
        Assert.Equal(students.Length, listResult.Items.Count());

        // Verify each student's state
        foreach (var (id, expectedName, expectedMaxCount) in students)
        {
            var tagStateId = TagStateId.FromProjector<StudentProjector>(new StudentTag(id));
            var tagState = await _executor.GetTagStateAsync(tagStateId);
            var studentState = (StudentState)tagState.Payload;

            Assert.Equal(id, studentState.StudentId);
            Assert.Equal(expectedName, studentState.Name);
            Assert.Equal(expectedMaxCount, studentState.MaxClassCount);
        }
    }

    #endregion

    #region Helper Commands for Testing

    // Simple enrollment command for testing projections
    private record EnrollInClassRoom(Guid StudentId, Guid ClassRoomId) : ICommandWithHandler<EnrollInClassRoom>
    {
        public static async Task<EventOrNone> HandleAsync(
            EnrollInClassRoom command,
            ICommandContext context)
        {
            var tag = new StudentTag(command.StudentId);
            var state = await context.GetStateAsync<StudentState, StudentProjector>(tag);

            if (state.Payload.EnrolledClassRoomIds.Count >= state.Payload.MaxClassCount)
            {
                throw new ApplicationException("Student has reached max class count");
            }

            return EventOrNone.From(
                new StudentEnrolledInClassRoom(
                    command.StudentId,
                    command.ClassRoomId),
                tag);
        }
    }

    #endregion
}
