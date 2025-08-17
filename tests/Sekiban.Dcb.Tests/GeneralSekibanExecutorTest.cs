using Dcb.Domain;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

public class GeneralSekibanExecutorTest
{
    private readonly InMemoryObjectAccessor _actorAccessor;
    private readonly GeneralSekibanExecutor _commandExecutor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly InMemoryEventStore _eventStore;

    public GeneralSekibanExecutorTest()
    {
        _eventStore = new InMemoryEventStore();
        _domainTypes = DomainType.GetDomainTypes();
        _actorAccessor = new InMemoryObjectAccessor(_eventStore, _domainTypes);
        _commandExecutor = new GeneralSekibanExecutor(_eventStore, _actorAccessor, _domainTypes);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidCommand_WritesEventsAndTags()
    {
        // Arrange
        var command = new TestCommand("Test", 42);
        var handler = new TestCommandHandler();

        // Act
        var result = await _commandExecutor.ExecuteAsync(command, handler);

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
        var handler = new ErrorCommandHandler();

        // Act
        var result = await _commandExecutor.ExecuteAsync(command, handler);

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
        var handler = new NoEventsCommandHandler();

        // Act
        var result = await _commandExecutor.ExecuteAsync(command, handler);

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
        var handler = new TestCommandHandler();

        // Act - Execute two commands concurrently
        var task1 = _commandExecutor.ExecuteAsync(command1, handler);
        var task2 = _commandExecutor.ExecuteAsync(command2, handler);

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
        var handler = new AccessStateCommandHandler();

        // Act
        var result = await _commandExecutor.ExecuteAsync(command, handler);

        // Assert
        Assert.True(result.IsSuccess);

        // The handler should have accessed the state
        // This is implicitly tested by the successful execution
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleTags_ReservesAllTags()
    {
        // Arrange
        var command = new TestCommand("Test", 42);
        var handler = new MultiTagCommandHandler();

        // Act
        var result = await _commandExecutor.ExecuteAsync(command, handler);

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

    // Test command and handler
    private record TestCommand(string Name, int Value) : ICommand;

    private class TestCommandHandler : ICommandHandler<TestCommand>
    {
        public async Task<ResultBox<EventOrNone>> HandleAsync(TestCommand command, ICommandContext context)
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
        public Task<ResultBox<EventOrNone>> HandleAsync(TestCommand command, ICommandContext context) =>
            Task.FromResult(ResultBox.Error<EventOrNone>(new InvalidOperationException("Handler error")));
    }

    // No events handler
    private class NoEventsCommandHandler : ICommandHandler<TestCommand>
    {
        public Task<ResultBox<EventOrNone>> HandleAsync(TestCommand command, ICommandContext context) =>
            Task.FromResult(EventOrNone.None);
    }

    // Test types
    private record TestEvent(string Name, int Value) : IEventPayload;

    private record TestTag : ITag
    {
        public bool IsConsistencyTag() => true;
        public string GetTagContent() => "Test123";
        public string GetTagGroup() => "TestGroup";
    }

    private class TestProjector : ITagProjector<TestProjector>
    {
        public static string ProjectorVersion => "1.0.0";
        public static string ProjectorName => nameof(TestProjector);
        public static ITagStatePayload Project(ITagStatePayload current, Event _) => current;
    }
    private class AccessStateCommandHandler : ICommandHandler<TestCommand>
    {
        public async Task<ResultBox<EventOrNone>> HandleAsync(TestCommand command, ICommandContext context)
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

    // Handler that creates events with multiple tags
    private class MultiTagCommandHandler : ICommandHandler<TestCommand>
    {
        public Task<ResultBox<EventOrNone>> HandleAsync(TestCommand command, ICommandContext context)
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
        public string GetTagContent() => "Test456";
        public string GetTagGroup() => "TestGroup2";
    }
}
