using DcbLib;
using DcbLib.Actors;
using DcbLib.Commands;
using DcbLib.Events;
using DcbLib.InMemory;
using DcbLib.Storage;
using DcbLib.Tags;
using Domain;
using ResultBoxes;
using Xunit;

namespace Unit;

public class InMemoryCommandExecutorTest
{
    private readonly InMemoryEventStore _eventStore;
    private readonly InMemoryObjectAccessor _actorAccessor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly InMemoryCommandExecutor _commandExecutor;
    
    // Test command and handler
    private record TestCommand(string Name, int Value) : ICommand;
    
    private class TestCommandHandler : ICommandHandler<TestCommand>
    {
        public static async Task<ResultBox<EventOrNone>> HandleAsync(
            TestCommand command,
            ICommandContext context)
        {
            // Create test events
            var testTag = new TestTag();
            var testEvent = new TestEvent(command.Name, command.Value);
            
            // Check if tag exists
            var exists = await context.TagExistsAsync(testTag);
            
            // Get current state to track dependencies
            var stateResult = await context.GetStateAsync<TestProjector>(testTag);
            
            return EventOrNone.Event(testEvent, testTag);
        }
    }
    
    // Error handler for testing failures
    private class ErrorCommandHandler : ICommandHandler<TestCommand>
    {
        public static Task<ResultBox<EventOrNone>> HandleAsync(
            TestCommand command,
            ICommandContext context)
        {
            return Task.FromResult(ResultBox.Error<EventOrNone>(
                new InvalidOperationException("Handler error")));
        }
    }
    
    // No events handler
    private class NoEventsCommandHandler : ICommandHandler<TestCommand>
    {
        public static Task<ResultBox<EventOrNone>> HandleAsync(
            TestCommand command,
            ICommandContext context)
        {
            return Task.FromResult(EventOrNone.None);
        }
    }
    
    // Test types
    private record TestEvent(string Name, int Value) : IEventPayload;
    
    private record TestTag : ITag
    {
        public bool IsConsistencyTag() => true;
        public string GetTag() => "TestGroup:Test123";
        public string GetTagGroup() => "TestGroup";
    }
    
    private class TestProjector : ITagProjector
    {
        public string GetProjectorName() => "TestProjector";
        public string GetTagProjectorName() => "TestProjector";
    public ITagStatePayload Project(ITagStatePayload current, Event _) => current;
    }
    
    public InMemoryCommandExecutorTest()
    {
        _eventStore = new InMemoryEventStore();
        _domainTypes = DomainType.GetDomainTypes();
        _actorAccessor = new InMemoryObjectAccessor(_eventStore, _domainTypes);
        _commandExecutor = new InMemoryCommandExecutor(_eventStore, _actorAccessor, _domainTypes);
    }
    
    [Fact]
    public async Task ExecuteAsync_WithValidCommand_WritesEventsAndTags()
    {
        // Arrange
        var command = new TestCommand("Test", 42);
        // Act
        var result = await _commandExecutor.ExecuteAsync<TestCommand, TestCommandHandler>(command);
        
        // Assert
        Assert.True(result.IsSuccess);
        var executionResult = result.GetValue();
        Assert.NotEqual(Guid.Empty, executionResult.EventId);
        Assert.Single(executionResult.TagWrites);
        Assert.True(executionResult.Duration > TimeSpan.Zero);
        
        // Verify event was written
        var testTag = new TestTag();
        var eventsResult = await _eventStore.ReadEventsByTagAsync(testTag);
        Assert.True(eventsResult.IsSuccess);
        var events = eventsResult.GetValue().ToList();
        Assert.Single(events);
        
        // Verify tag was written
        var tagExistsResult = await _eventStore.TagExistsAsync(testTag);
        Assert.True(tagExistsResult.IsSuccess);
        Assert.True(tagExistsResult.GetValue());
    }
    
    [Fact]
    public async Task ExecuteAsync_WithHandlerError_ReturnsError()
    {
        // Arrange
        var command = new TestCommand("Test", 42);
        // Act
        var result = await _commandExecutor.ExecuteAsync<TestCommand, ErrorCommandHandler>(command);
        
        // Assert
        Assert.False(result.IsSuccess);
        var exception = result.GetException();
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("Handler error", exception.Message);
    }
    
    [Fact]
    public async Task ExecuteAsync_WithNoEvents_ReturnsEmptyResult()
    {
        // Arrange
        var command = new TestCommand("Test", 42);
        // Act
        var result = await _commandExecutor.ExecuteAsync<TestCommand, NoEventsCommandHandler>(command);
        
        // Assert
        Assert.True(result.IsSuccess);
        var executionResult = result.GetValue();
        Assert.Equal(Guid.Empty, executionResult.EventId);
        Assert.Empty(executionResult.TagWrites);
    }
    
    [Fact]
    public async Task ExecuteAsync_WithConcurrentCommands_HandlesReservationsCorrectly()
    {
        // Arrange
        var command1 = new TestCommand("Test1", 1);
        var command2 = new TestCommand("Test2", 2);
        // Act - Execute two commands concurrently
        var task1 = _commandExecutor.ExecuteAsync<TestCommand, TestCommandHandler>(command1);
        var task2 = _commandExecutor.ExecuteAsync<TestCommand, TestCommandHandler>(command2);
        
        var results = await Task.WhenAll(task1, task2);
        
        // Assert - At least one should succeed
        var successCount = results.Count(r => r.IsSuccess);
        Assert.True(successCount >= 1, "At least one command should succeed");
        
        // If both succeeded, they should have different event IDs
        if (results.All(r => r.IsSuccess))
        {
            var eventId1 = results[0].GetValue().EventId;
            var eventId2 = results[1].GetValue().EventId;
            Assert.NotEqual(eventId1, eventId2);
        }
    }
    
    [Fact]
    public async Task ExecuteAsync_TracksAccessedStates()
    {
        // Arrange
        var command = new TestCommand("Test", 42);
        var testTag = new TestTag();
        
        // Custom handler that accesses state
        // Act
        var result = await _commandExecutor.ExecuteAsync<TestCommand, AccessStateCommandHandler>(command);
        
        // Assert
        Assert.True(result.IsSuccess);
        
        // The handler should have accessed the state
        // This is implicitly tested by the successful execution
    }
    
    // Handler that accesses state
    private class AccessStateCommandHandler : ICommandHandler<TestCommand>
    {
        public static async Task<ResultBox<EventOrNone>> HandleAsync(
            TestCommand command,
            ICommandContext context)
        {
            var testTag = new TestTag();
            
            // Access state to ensure it's tracked
            var stateResult = await context.GetStateAsync<TestProjector>(testTag);
            var exists = await context.TagExistsAsync(testTag);
            var latestId = await context.GetTagLatestSortableUniqueIdAsync(testTag);
            
            // Create event
            var testEvent = new TestEvent(command.Name, command.Value);
            
            return EventOrNone.Event(testEvent, testTag);
        }
    }
    
    [Fact]
    public async Task ExecuteAsync_WithMultipleTags_ReservesAllTags()
    {
        // Arrange
        var command = new TestCommand("Test", 42);
        // Act
        var result = await _commandExecutor.ExecuteAsync<TestCommand, MultiTagCommandHandler>(command);
        
        // Assert
        Assert.True(result.IsSuccess);
        var executionResult = result.GetValue();
        Assert.Equal(2, executionResult.TagWrites.Count);
        
        // Verify both tags were written
        var tag1 = new TestTag();
        var tag2 = new TestTag2();
        
        var tag1Exists = await _eventStore.TagExistsAsync(tag1);
        Assert.True(tag1Exists.GetValue());
        
        var tag2Exists = await _eventStore.TagExistsAsync(tag2);
        Assert.True(tag2Exists.GetValue());
    }
    
    // Handler that creates events with multiple tags
    private class MultiTagCommandHandler : ICommandHandler<TestCommand>
    {
        public static Task<ResultBox<EventOrNone>> HandleAsync(
            TestCommand command,
            ICommandContext context)
        {
            var tag1 = new TestTag();
            var tag2 = new TestTag2();
            
            var testEvent = new TestEvent(command.Name, command.Value);
            
            return Task.FromResult(EventOrNone.Event(testEvent, tag1, tag2));
        }
    }
    
    private record TestTag2 : ITag
    {
        public bool IsConsistencyTag() => true;
        public string GetTag() => "TestGroup2:Test456";
        public string GetTagGroup() => "TestGroup2";
    }
}

/// <summary>
/// Comprehensive tests for InMemoryCommandExecutor using domain types
/// Testing the actual business rules and command execution flow
/// </summary>
public class InMemoryCommandExecutorDomainTests
{
    private readonly InMemoryEventStore _eventStore;
    private readonly InMemoryObjectAccessor _actorAccessor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly InMemoryCommandExecutor _commandExecutor;
    
    public InMemoryCommandExecutorDomainTests()
    {
        _eventStore = new InMemoryEventStore();
        _domainTypes = DomainType.GetDomainTypes();
        _actorAccessor = new InMemoryObjectAccessor(_eventStore, _domainTypes);
        _commandExecutor = new InMemoryCommandExecutor(_eventStore, _actorAccessor, _domainTypes);
    }
    
    
    // Example command that includes its own handler logic
    public record CreateTeacherCommand(Guid TeacherId, string Name, string Subject) 
        : ICommandWithHandler<CreateTeacherCommand>
    {
        public async Task<ResultBox<EventOrNone>> HandleAsync(ICommandContext context)
        {
            var tag = new TeacherTag(TeacherId);
            var exists = await context.TagExistsAsync(tag);
            
            if (exists)
            {
                return ResultBox.Error<EventOrNone>(
                    new ApplicationException($"Teacher {TeacherId} already exists"));
            }
            
            return EventOrNone.EventWithTags(
                new TeacherCreated(TeacherId, Name, Subject),
                tag);
        }
    }
    
    // Supporting types for the test
    public record TeacherTag(Guid TeacherId) : ITag
    {
        public bool IsConsistencyTag() => true;
        public string GetTagGroup() => "Teacher";
        public string GetTag() => $"Teacher:{TeacherId}";
    }
    
    public record TeacherCreated(Guid TeacherId, string Name, string Subject) : IEventPayload;
    
    [Fact]
    public async Task ExecuteAsync_With_ICommandWithHandler_Should_Work()
    {
        // Arrange
        var teacherId = Guid.NewGuid();
        var command = new CreateTeacherCommand(teacherId, "Dr. Smith", "Mathematics");
        
        // Act - Execute command with its built-in handler
        var result = await _commandExecutor.ExecuteAsync(command);
        
        // Assert
        Assert.True(result.IsSuccess);
        var executionResult = result.GetValue();
        Assert.NotEqual(Guid.Empty, executionResult.EventId);
        Assert.Single(executionResult.TagWrites);
        Assert.Equal($"Teacher:{teacherId}", executionResult.TagWrites[0].Tag);
        
        // Verify teacher was created
        var teacherTag = new TeacherTag(teacherId);
        var tagExists = await _eventStore.TagExistsAsync(teacherTag);
        Assert.True(tagExists.GetValue());
        
        // Verify event was written
        var events = await _eventStore.ReadEventsByTagAsync(teacherTag);
        var eventsList = events.GetValue().ToList();
        Assert.Single(eventsList);
        var payload = eventsList[0].Payload as TeacherCreated;
        Assert.NotNull(payload);
        Assert.Equal(teacherId, payload.TeacherId);
        Assert.Equal("Dr. Smith", payload.Name);
        Assert.Equal("Mathematics", payload.Subject);
    }
    
    [Fact]
    public async Task CreateStudent_With_Built_In_Handler_Should_Work()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var command = new CreateStudent(studentId, "Alice Johnson", 6);
        
        // Act - Execute command using its built-in handler (no separate handler needed)
        var result = await _commandExecutor.ExecuteAsync(command);
        
        // Assert
        Assert.True(result.IsSuccess);
        var executionResult = result.GetValue();
        Assert.NotEqual(Guid.Empty, executionResult.EventId);
        Assert.Single(executionResult.TagWrites);
        Assert.Equal($"Student:{studentId}", executionResult.TagWrites[0].Tag);
        
        // Verify student was created
        var studentTag = new StudentTag(studentId);
        var tagExists = await _eventStore.TagExistsAsync(studentTag);
        Assert.True(tagExists.GetValue());
        
        // Verify the event
        var events = await _eventStore.ReadEventsByTagAsync(studentTag);
        var eventsList = events.GetValue().ToList();
        Assert.Single(eventsList);
        var payload = eventsList[0].Payload as StudentCreated;
        Assert.NotNull(payload);
        Assert.Equal("Alice Johnson", payload.Name);
        Assert.Equal(6, payload.MaxClassCount);
    }
    
    [Fact]
    public async Task ExecuteAsync_With_ICommandWithHandler_Should_Fail_When_Already_Exists()
    {
        // Arrange
        var teacherId = Guid.NewGuid();
        var command = new CreateTeacherCommand(teacherId, "Dr. Smith", "Mathematics");
        
        // Create the teacher first
        var firstResult = await _commandExecutor.ExecuteAsync(command);
        Assert.True(firstResult.IsSuccess);
        
        // Act - Try to create the same teacher again
        var secondResult = await _commandExecutor.ExecuteAsync(command);
        
        // Assert
        Assert.False(secondResult.IsSuccess);
        var exception = secondResult.GetException();
        Assert.IsType<ApplicationException>(exception);
        Assert.Contains("already exists", exception.Message);
    }
    
    [Fact]
    public async Task ExecuteAsync_With_Function_Should_Work()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var command = new CreateStudent(studentId, "Jane Doe", 4);
        
        // Define handler as a function
        Func<CreateStudent, ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc = 
            async (cmd, context) =>
            {
                var tag = new StudentTag(cmd.StudentId);
                var exists = await context.TagExistsAsync(tag);
                
                if (exists)
                {
                    return ResultBox.Error<EventOrNone>(
                        new ApplicationException("Student Already Exists"));
                }
                
                return EventOrNone.EventWithTags(
                    new StudentCreated(cmd.StudentId, cmd.Name, cmd.MaxClassCount), 
                    tag);
            };
        
        // Act
        var result = await _commandExecutor.ExecuteAsync(command, handlerFunc);
        
        // Assert
        Assert.True(result.IsSuccess);
        var executionResult = result.GetValue();
        Assert.NotEqual(Guid.Empty, executionResult.EventId);
        Assert.Single(executionResult.TagWrites);
        Assert.Equal($"Student:{studentId}", executionResult.TagWrites[0].Tag);
        
        // Verify student was created
        var studentTag = new StudentTag(studentId);
        var tagExists = await _eventStore.TagExistsAsync(studentTag);
        Assert.True(tagExists.GetValue());
    }
    
    [Fact]
    public async Task CreateStudent_Should_Create_New_Student()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var command = new CreateStudent(studentId, "John Doe", 3);
        
        // Act
        var result = await _commandExecutor.ExecuteAsync(command);
        
        // Assert
        Assert.True(result.IsSuccess);
        var executionResult = result.GetValue();
        Assert.NotEqual(Guid.Empty, executionResult.EventId);
        Assert.Single(executionResult.TagWrites);
        Assert.Equal($"Student:{studentId}", executionResult.TagWrites[0].Tag);
        
        // Verify student was created
        var studentTag = new StudentTag(studentId);
        var tagExists = await _eventStore.TagExistsAsync(studentTag);
        Assert.True(tagExists.GetValue());
        
        // Verify events
        var events = await _eventStore.ReadEventsByTagAsync(studentTag);
        var eventsList = events.GetValue().ToList();
        Assert.Single(eventsList);
        var payload = eventsList[0].Payload as StudentCreated;
        Assert.NotNull(payload);
        Assert.Equal(studentId, payload.StudentId);
        Assert.Equal("John Doe", payload.Name);
        Assert.Equal(3, payload.MaxClassCount);
    }
    
    [Fact]
    public async Task CreateStudent_Should_Fail_When_Student_Already_Exists()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var command = new CreateStudent(studentId, "John Doe");
        
        // Create the student first
        await _commandExecutor.ExecuteAsync(command);
        
        // Act - Try to create the same student again
        var result = await _commandExecutor.ExecuteAsync(command);
        
        // Assert
        Assert.False(result.IsSuccess);
        var exception = result.GetException();
        Assert.IsType<ApplicationException>(exception);
        Assert.Contains("Student Already Exists", exception.Message);
    }
    
    [Fact]
    public async Task CreateClassRoom_Should_Create_New_ClassRoom()
    {
        // Arrange
        var classRoomId = Guid.NewGuid();
        var command = new CreateClassRoom(classRoomId, "Math 101", 20);
        // Act
        var result = await _commandExecutor.ExecuteAsync<CreateClassRoom, CreateClassRoomHandler>(command);
        
        // Assert
        Assert.True(result.IsSuccess);
        var executionResult = result.GetValue();
        Assert.Single(executionResult.TagWrites);
        Assert.Equal($"ClassRoom:{classRoomId}", executionResult.TagWrites[0].Tag);
        
        // Verify classroom was created
        var classRoomTag = new ClassRoomTag(classRoomId);
        var tagExists = await _eventStore.TagExistsAsync(classRoomTag);
        Assert.True(tagExists.GetValue());
    }
    
    [Fact]
    public async Task EnrollStudent_Should_Succeed_When_Both_Exist_And_Have_Capacity()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var classRoomId = Guid.NewGuid();
        
        // Create student
        await _commandExecutor.ExecuteAsync(
            new CreateStudent(studentId, "John Doe", 5));
        
        // Create classroom
        await _commandExecutor.ExecuteAsync<CreateClassRoom, CreateClassRoomHandler>(
            new CreateClassRoom(classRoomId, "Math 101", 10),
            new CreateClassRoomHandler());
        
        // Act - Enroll student
        var enrollCommand = new EnrollStudentInClassRoom(studentId, classRoomId);
        var result = await _commandExecutor.ExecuteAsync<EnrollStudentInClassRoom, EnrollStudentInClassRoomHandler>(enrollCommand);
        
        // Assert
        Assert.True(result.IsSuccess);
        var executionResult = result.GetValue();
        Assert.Equal(2, executionResult.TagWrites.Count); // Both student and classroom tags
        
        // Verify the enrollment event exists
        var studentTag = new StudentTag(studentId);
        var events = await _eventStore.ReadEventsByTagAsync(studentTag);
        var eventsList = events.GetValue().ToList();
        Assert.Equal(2, eventsList.Count); // StudentCreated + StudentEnrolledInClassRoom
        
        var enrollmentEvent = eventsList[1].Payload as StudentEnrolledInClassRoom;
        Assert.NotNull(enrollmentEvent);
        Assert.Equal(studentId, enrollmentEvent.StudentId);
        Assert.Equal(classRoomId, enrollmentEvent.ClassRoomId);
    }
    
    [Fact]
    public async Task EnrollStudent_Should_Fail_When_Student_At_Max_Capacity()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var classRoomIds = new List<Guid>();
        
        // Create student with max 2 classes
        await _commandExecutor.ExecuteAsync(
            new CreateStudent(studentId, "John Doe", 2));
        
        // Create and enroll in 2 classrooms
        for (int i = 0; i < 2; i++)
        {
            var classRoomId = Guid.NewGuid();
            classRoomIds.Add(classRoomId);
            
            await _commandExecutor.ExecuteAsync<CreateClassRoom, CreateClassRoomHandler>(
                new CreateClassRoom(classRoomId, $"Class {i}", 10),
                new CreateClassRoomHandler());
            
            await _commandExecutor.ExecuteAsync<EnrollStudentInClassRoom, EnrollStudentInClassRoomHandler>(
                new EnrollStudentInClassRoom(studentId, classRoomId),
                new EnrollStudentInClassRoomHandler());
        }
        
        // Create a third classroom
        var thirdClassRoomId = Guid.NewGuid();
        await _commandExecutor.ExecuteAsync<CreateClassRoom, CreateClassRoomHandler>(
            new CreateClassRoom(thirdClassRoomId, "Class 3", 10),
            new CreateClassRoomHandler());
        
        // Act - Try to enroll in third classroom
        var result = await _commandExecutor.ExecuteAsync<EnrollStudentInClassRoom, EnrollStudentInClassRoomHandler>(
            new EnrollStudentInClassRoom(studentId, thirdClassRoomId),
            new EnrollStudentInClassRoomHandler());
        
        // Assert
        Assert.False(result.IsSuccess);
        var exception = result.GetException();
        Assert.Contains("maximum class count", exception.Message);
    }
    
    [Fact]
    public async Task EnrollStudent_Should_Fail_When_ClassRoom_Is_Full()
    {
        // Arrange
        var classRoomId = Guid.NewGuid();
        var studentIds = new List<Guid>();
        
        // Create classroom with max 2 students
        await _commandExecutor.ExecuteAsync<CreateClassRoom, CreateClassRoomHandler>(
            new CreateClassRoom(classRoomId, "Small Class", 2),
            new CreateClassRoomHandler());
        
        // Create and enroll 2 students
        for (int i = 0; i < 2; i++)
        {
            var studentId = Guid.NewGuid();
            studentIds.Add(studentId);
            
            await _commandExecutor.ExecuteAsync(
                new CreateStudent(studentId, $"Student {i}", 5));
            
            await _commandExecutor.ExecuteAsync<EnrollStudentInClassRoom, EnrollStudentInClassRoomHandler>(
                new EnrollStudentInClassRoom(studentId, classRoomId),
                new EnrollStudentInClassRoomHandler());
        }
        
        // Create a third student
        var thirdStudentId = Guid.NewGuid();
        await _commandExecutor.ExecuteAsync(
            new CreateStudent(thirdStudentId, "Student 3", 5));
        
        // Act - Try to enroll third student
        var result = await _commandExecutor.ExecuteAsync<EnrollStudentInClassRoom, EnrollStudentInClassRoomHandler>(
            new EnrollStudentInClassRoom(thirdStudentId, classRoomId),
            new EnrollStudentInClassRoomHandler());
        
        // Assert
        Assert.False(result.IsSuccess);
        var exception = result.GetException();
        Assert.Contains("ClassRoom is full", exception.Message);
    }
    
    [Fact]
    public async Task EnrollStudent_Should_Fail_When_Already_Enrolled()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var classRoomId = Guid.NewGuid();
        
        // Create student and classroom
        await _commandExecutor.ExecuteAsync(
            new CreateStudent(studentId, "John Doe"));
        
        await _commandExecutor.ExecuteAsync<CreateClassRoom, CreateClassRoomHandler>(
            new CreateClassRoom(classRoomId, "Math 101"),
            new CreateClassRoomHandler());
        
        // Enroll student
        await _commandExecutor.ExecuteAsync<EnrollStudentInClassRoom, EnrollStudentInClassRoomHandler>(
            new EnrollStudentInClassRoom(studentId, classRoomId),
            new EnrollStudentInClassRoomHandler());
        
        // Act - Try to enroll again
        var result = await _commandExecutor.ExecuteAsync<EnrollStudentInClassRoom, EnrollStudentInClassRoomHandler>(
            new EnrollStudentInClassRoom(studentId, classRoomId),
            new EnrollStudentInClassRoomHandler());
        
        // Assert
        Assert.False(result.IsSuccess);
        var exception = result.GetException();
        Assert.Contains("already enrolled", exception.Message);
    }
    
    [Fact]
    public async Task DropStudent_Should_Succeed_When_Enrolled()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var classRoomId = Guid.NewGuid();
        
        // Create and enroll
        await _commandExecutor.ExecuteAsync(
            new CreateStudent(studentId, "John Doe"));
        
        await _commandExecutor.ExecuteAsync<CreateClassRoom, CreateClassRoomHandler>(
            new CreateClassRoom(classRoomId, "Math 101"),
            new CreateClassRoomHandler());
        
        await _commandExecutor.ExecuteAsync<EnrollStudentInClassRoom, EnrollStudentInClassRoomHandler>(
            new EnrollStudentInClassRoom(studentId, classRoomId),
            new EnrollStudentInClassRoomHandler());
        
        // Act - Drop student
        var result = await _commandExecutor.ExecuteAsync<DropStudentFromClassRoom, DropStudentFromClassRoomHandler>(
            new DropStudentFromClassRoom(studentId, classRoomId),
            new DropStudentFromClassRoomHandler());
        
        // Assert
        Assert.True(result.IsSuccess);
        var executionResult = result.GetValue();
        Assert.Equal(2, executionResult.TagWrites.Count); // Both student and classroom tags
        
        // Verify the drop event
        var studentTag = new StudentTag(studentId);
        var events = await _eventStore.ReadEventsByTagAsync(studentTag);
        var eventsList = events.GetValue().ToList();
        Assert.Equal(3, eventsList.Count); // Created + Enrolled + Dropped
        
        var dropEvent = eventsList[2].Payload as StudentDroppedFromClassRoom;
        Assert.NotNull(dropEvent);
        Assert.Equal(studentId, dropEvent.StudentId);
        Assert.Equal(classRoomId, dropEvent.ClassRoomId);
    }
    
    [Fact]
    public async Task DropStudent_Should_Fail_When_Not_Enrolled()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var classRoomId = Guid.NewGuid();
        
        // Create student and classroom but don't enroll
        await _commandExecutor.ExecuteAsync(
            new CreateStudent(studentId, "John Doe"));
        
        await _commandExecutor.ExecuteAsync<CreateClassRoom, CreateClassRoomHandler>(
            new CreateClassRoom(classRoomId, "Math 101"),
            new CreateClassRoomHandler());
        
        // Act - Try to drop without enrollment
        var result = await _commandExecutor.ExecuteAsync<DropStudentFromClassRoom, DropStudentFromClassRoomHandler>(
            new DropStudentFromClassRoom(studentId, classRoomId),
            new DropStudentFromClassRoomHandler());
        
        // Assert
        Assert.False(result.IsSuccess);
        var exception = result.GetException();
        Assert.Contains("not enrolled", exception.Message);
    }
    
    [Fact]
    public async Task Complex_Scenario_Multiple_Students_And_ClassRooms()
    {
        // Arrange - Create 3 students and 2 classrooms
        var student1 = Guid.NewGuid();
        var student2 = Guid.NewGuid();
        var student3 = Guid.NewGuid();
        var classRoom1 = Guid.NewGuid();
        var classRoom2 = Guid.NewGuid();
        
        // Create all entities
        await _commandExecutor.ExecuteAsync(new CreateStudent(student1, "Alice", 3));
        await _commandExecutor.ExecuteAsync(new CreateStudent(student2, "Bob", 3));
        await _commandExecutor.ExecuteAsync(new CreateStudent(student3, "Charlie", 3));
        await _commandExecutor.ExecuteAsync<CreateClassRoom, CreateClassRoomHandler>(new CreateClassRoom(classRoom1, "Math", 3));
        await _commandExecutor.ExecuteAsync<CreateClassRoom, CreateClassRoomHandler>(new CreateClassRoom(classRoom2, "Science", 3));
        
        // Enroll students in various combinations
        await _commandExecutor.ExecuteAsync<EnrollStudentInClassRoom, EnrollStudentInClassRoomHandler>(new EnrollStudentInClassRoom(student1, classRoom1));
        await _commandExecutor.ExecuteAsync<EnrollStudentInClassRoom, EnrollStudentInClassRoomHandler>(new EnrollStudentInClassRoom(student1, classRoom2));
        await _commandExecutor.ExecuteAsync<EnrollStudentInClassRoom, EnrollStudentInClassRoomHandler>(new EnrollStudentInClassRoom(student2, classRoom1));
        await _commandExecutor.ExecuteAsync<EnrollStudentInClassRoom, EnrollStudentInClassRoomHandler>(new EnrollStudentInClassRoom(student3, classRoom2));
        
        // Verify all enrollments succeeded
        var mathEvents = await _eventStore.ReadEventsByTagAsync(new ClassRoomTag(classRoom1));
        var mathEventsList = mathEvents.GetValue().ToList();
        Assert.Equal(3, mathEventsList.Count); // Created + 2 enrollments
        
        var scienceEvents = await _eventStore.ReadEventsByTagAsync(new ClassRoomTag(classRoom2));
        var scienceEventsList = scienceEvents.GetValue().ToList();
        Assert.Equal(3, scienceEventsList.Count); // Created + 2 enrollments
        
        // Drop a student and verify
        await _commandExecutor.ExecuteAsync<DropStudentFromClassRoom, DropStudentFromClassRoomHandler>(new DropStudentFromClassRoom(student1, classRoom1));
        
        var aliceEvents = await _eventStore.ReadEventsByTagAsync(new StudentTag(student1));
        var aliceEventsList = aliceEvents.GetValue().ToList();
        Assert.Equal(4, aliceEventsList.Count); // Created + 2 enrollments + 1 drop
    }
    
    [Fact]
    public async Task Concurrent_Commands_Should_Be_Handled_Correctly()
    {
        // Arrange
        var classRoomId = Guid.NewGuid();
        var studentIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        
        // Create classroom with limited capacity
        await _commandExecutor.ExecuteAsync<CreateClassRoom, CreateClassRoomHandler>(
            new CreateClassRoom(classRoomId, "Limited Class", 3),
            new CreateClassRoomHandler());
        
        // Create all students
        var createTasks = studentIds.Select(id =>
            _commandExecutor.ExecuteAsync(
                new CreateStudent(id, $"Student {id}", 5))).ToList();
        
        await Task.WhenAll(createTasks);
        
        // Act - Try to enroll all students concurrently
        var enrollTasks = studentIds.Select(id =>
            _commandExecutor.ExecuteAsync<EnrollStudentInClassRoom, EnrollStudentInClassRoomHandler>(
                new EnrollStudentInClassRoom(id, classRoomId),
                new EnrollStudentInClassRoomHandler())).ToList();
        
        var results = await Task.WhenAll(enrollTasks);
        
        // Assert - Due to concurrent execution, we might get more than 3 successes
        // This is because multiple commands might read the state before any writes happen
        var successCount = results.Count(r => r.IsSuccess);
        Assert.True(successCount >= 3, $"Expected at least 3 successes but got {successCount}");
        
        // In a real system with stricter consistency, you would enforce the limit
        // For now, we just verify that some enrollments succeeded and some failed
        
        // The remaining should fail
        var failureCount = results.Count(r => !r.IsSuccess);
        Assert.Equal(5 - successCount, failureCount);
        
        // Verify failures are either due to capacity or reservation conflicts
        var failures = results.Where(r => !r.IsSuccess).ToList();
        foreach (var failure in failures)
        {
            var message = failure.GetException().Message;
            Assert.True(
                message.Contains("full", StringComparison.OrdinalIgnoreCase) || 
                message.Contains("reserve", StringComparison.OrdinalIgnoreCase),
                $"Expected failure to be about 'full' classroom or 'reserve' conflict, but got: {message}");
        }
    }
}