using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Tags;
using System.Text.Json;
namespace Sekiban.Dcb.Tests;

public class OptimisticLockingTest
{
    private readonly InMemoryObjectAccessor _actorAccessor;
    private readonly GeneralSekibanExecutor _commandExecutor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly InMemoryEventStore _eventStore;

    public OptimisticLockingTest()
    {
        _eventStore = new InMemoryEventStore();
        _domainTypes = CreateTestDomainTypes();
        _actorAccessor = new InMemoryObjectAccessor(_eventStore, _domainTypes);
        _commandExecutor = new GeneralSekibanExecutor(_eventStore, _actorAccessor, _domainTypes);
    }

    [Fact]
    public async Task OptimisticLocking_WithCorrectVersion_ShouldSucceed()
    {
        // Arrange - Create initial entity
        var tagId = Guid.NewGuid().ToString();
        var createCommand = new CreateTestCommand(tagId);
        var createResult = await _commandExecutor.ExecuteAsync(createCommand, new CreateTestHandler());
        Assert.True(createResult.IsSuccess);

        // Get the current version of the tag by using the command context
        var tag = new TestTag(tagId);
        var commandContext = new GeneralCommandContext(_actorAccessor, _domainTypes);
        var stateResult = await commandContext.GetStateAsync<TestProjector>(tag);

        SortableUniqueId currentVersion;
        if (stateResult.IsSuccess)
        {
            currentVersion = new SortableUniqueId(stateResult.GetValue().LastSortedUniqueId);
        } else
        {
            // If state doesn't exist, use the event ID from creation
            currentVersion = new SortableUniqueId(SortableUniqueId.GenerateNew());
        }

        // Act - Update with correct version
        var updateCommand = new UpdateTestCommand(tagId, 2);
        var updateHandler = new UpdateTestHandler(true, currentVersion);
        var updateResult = await _commandExecutor.ExecuteAsync(updateCommand, updateHandler);

        // Assert
        Assert.True(updateResult.IsSuccess);
        var executionResult = updateResult.GetValue();
        Assert.NotEqual(Guid.Empty, executionResult.EventId);
        Assert.True(executionResult.TagWrites.Any());
    }

    [Fact]
    public async Task OptimisticLocking_WithIncorrectVersion_ShouldFail()
    {
        // Arrange - Create initial entity
        var tagId = Guid.NewGuid().ToString();
        var createCommand = new CreateTestCommand(tagId);
        var createResult = await _commandExecutor.ExecuteAsync(createCommand, new CreateTestHandler());
        Assert.True(createResult.IsSuccess);

        // Get the initial version
        var tag = new TestTag(tagId);
        var commandContext = new GeneralCommandContext(_actorAccessor, _domainTypes);
        var initialStateResult = await commandContext.GetStateAsync<TestProjector>(tag);
        Assert.True(initialStateResult.IsSuccess);
        var initialVersion = new SortableUniqueId(initialStateResult.GetValue().LastSortedUniqueId);

        // Update the entity once (to change its version)
        var firstUpdateCommand = new UpdateTestCommand(tagId, 2);
        var firstUpdateResult = await _commandExecutor.ExecuteAsync(firstUpdateCommand, new UpdateTestHandler());
        Assert.True(firstUpdateResult.IsSuccess);

        // Act - Try to update with the OLD (initial) version - should fail
        var secondUpdateCommand = new UpdateTestCommand(tagId, 3);
        var updateHandler = new UpdateTestHandler(true, initialVersion);
        var updateResult = await _commandExecutor.ExecuteAsync(secondUpdateCommand, updateHandler);

        // Assert - Should fail due to version mismatch
        Assert.False(updateResult.IsSuccess);
        var exception = updateResult.GetException();
        Assert.Contains("Failed to reserve tags", exception.Message);
    }

    [Fact]
    public async Task OptimisticLocking_WithoutVersion_ShouldUseLatest()
    {
        // Arrange - Create initial entity
        var tagId = Guid.NewGuid().ToString();
        var createCommand = new CreateTestCommand(tagId);
        var createResult = await _commandExecutor.ExecuteAsync(createCommand, new CreateTestHandler());
        Assert.True(createResult.IsSuccess);

        // Update multiple times
        for (var i = 2; i <= 5; i++)
        {
            var updateCommand = new UpdateTestCommand(tagId, i);
            var updateResult = await _commandExecutor.ExecuteAsync(updateCommand, new UpdateTestHandler());
            Assert.True(updateResult.IsSuccess);
        }

        // Act - Update without specifying version (should use latest)
        var finalUpdateCommand = new UpdateTestCommand(tagId, 6);
        var finalUpdateResult = await _commandExecutor.ExecuteAsync(finalUpdateCommand, new UpdateTestHandler());

        // Assert
        Assert.True(finalUpdateResult.IsSuccess);
    }

    [Fact]
    public async Task OptimisticLocking_ConcurrentUpdates_OnlyOneSucceeds()
    {
        // Arrange - Create initial entity
        var tagId = Guid.NewGuid().ToString();
        var createCommand = new CreateTestCommand(tagId);
        var createResult = await _commandExecutor.ExecuteAsync(createCommand, new CreateTestHandler());
        Assert.True(createResult.IsSuccess);

        // Get the current version using command context
        var tag = new TestTag(tagId);
        var commandContext = new GeneralCommandContext(_actorAccessor, _domainTypes);
        var stateResult = await commandContext.GetStateAsync<TestProjector>(tag);

        SortableUniqueId currentVersion;
        if (stateResult.IsSuccess)
        {
            currentVersion = new SortableUniqueId(stateResult.GetValue().LastSortedUniqueId);
        } else
        {
            // If state doesn't exist, use the event ID from creation
            currentVersion = new SortableUniqueId(SortableUniqueId.GenerateNew());
        }

        // Act - Try to update concurrently with the same version
        var update1 = new UpdateTestCommand(tagId, 100);
        var update2 = new UpdateTestCommand(tagId, 200);
        var handler1 = new UpdateTestHandler(true, currentVersion);
        var handler2 = new UpdateTestHandler(true, currentVersion);

        var task1 = _commandExecutor.ExecuteAsync(update1, handler1);
        var task2 = _commandExecutor.ExecuteAsync(update2, handler2);

        var results = await Task.WhenAll(task1, task2);

        // Assert - Only one should succeed
        var successCount = results.Count(r => r.IsSuccess);
        Assert.Equal(1, successCount);

        var failureCount = results.Count(r => !r.IsSuccess);
        Assert.Equal(1, failureCount);

        // The failed one should have a reservation error
        var failedResult = results.First(r => !r.IsSuccess);
        Assert.Contains("Failed to reserve tags", failedResult.GetException().Message);
    }

    [Fact]
    public async Task ConsistencyTag_WithMinValue_ShouldAlwaysUseLatest()
    {
        // Arrange - Create initial entity
        var tagId = Guid.NewGuid().ToString();
        var createCommand = new CreateTestCommand(tagId);
        var createResult = await _commandExecutor.ExecuteAsync(createCommand, new CreateTestHandler());
        Assert.True(createResult.IsSuccess);

        // Update multiple times
        for (var i = 2; i <= 3; i++)
        {
            var updateCommand = new UpdateTestCommand(tagId, i);
            var updateResult = await _commandExecutor.ExecuteAsync(updateCommand, new UpdateTestHandler());
            Assert.True(updateResult.IsSuccess);
        }

        // Act - Use ConsistencyTag.From which uses MinValue (should use latest)
        var finalUpdateCommand = new UpdateTestCommand(tagId, 4);
        var handler = new UpdateTestHandlerWithConsistencyFrom();
        var finalUpdateResult = await _commandExecutor.ExecuteAsync(finalUpdateCommand, handler);

        // Assert
        Assert.True(finalUpdateResult.IsSuccess);
    }

    private DcbDomainTypes CreateTestDomainTypes()
    {
        // Create test-specific type managers
        var eventTypes = new SimpleEventTypes();

        var tagTypes = new SimpleTagTypes();

        var tagProjectorTypes = new SimpleTagProjectorTypes();
        tagProjectorTypes.RegisterProjector<TestProjector>();

        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        tagStatePayloadTypes.RegisterPayloadType<TestStatePayload>();

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        return new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            multiProjectorTypes,
            jsonOptions);
    }

    // Test-specific types
    private record TestTag(string Id) : ITag
    {
        public bool IsConsistencyTag() => true;
        public string GetTagContent() => Id;
        public string GetTagGroup() => "Test";
    }

    private record TestEvent(string Name, int Version) : IEventPayload;

    private record TestStatePayload(string State, int Count) : ITagStatePayload
    {
    }

    private class TestProjector : ITagProjector<TestProjector>
    {
        public static string ProjectorVersion => "1.0.0";
        public static string ProjectorName => nameof(TestProjector);
        public static ITagStatePayload Project(ITagStatePayload current, Event ev) =>
            current ?? new TestStatePayload("", 0);
    }

    private record UpdateTestCommand(string TagId, int NewVersion) : ICommand;

    private class UpdateTestHandler : ICommandHandler<UpdateTestCommand>
    {
        private readonly SortableUniqueId? _expectedVersion;
        private readonly bool _useOptimisticLocking;

        public UpdateTestHandler(bool useOptimisticLocking = false, SortableUniqueId? expectedVersion = null)
        {
            _useOptimisticLocking = useOptimisticLocking;
            _expectedVersion = expectedVersion;
        }

        public async Task<ResultBox<EventOrNone>> HandleAsync(UpdateTestCommand command, ICommandContext context)
        {
            var tag = new TestTag(command.TagId);

            // Get current state to retrieve version
            var state = await context.GetStateAsync<TestProjector>(tag);

            if (_useOptimisticLocking && _expectedVersion != null)
            {
                // Use ConsistencyTag with specific version for optimistic locking
                var consistencyTag = ConsistencyTag.FromTagWithSortableUniqueId(tag, _expectedVersion);
                return EventOrNone.EventWithTags(
                    new TestEvent($"Updated-{command.TagId}", command.NewVersion),
                    consistencyTag);
            }
            // Regular update without version checking
            return EventOrNone.EventWithTags(new TestEvent($"Updated-{command.TagId}", command.NewVersion), tag);
        }
    }

    private record CreateTestCommand(string TagId) : ICommand;

    private class CreateTestHandler : ICommandHandler<CreateTestCommand>
    {
        public Task<ResultBox<EventOrNone>> HandleAsync(CreateTestCommand command, ICommandContext context)
        {
            var tag = new TestTag(command.TagId);
            return Task.FromResult(EventOrNone.EventWithTags(new TestEvent($"Created-{command.TagId}", 1), tag));
        }
    }

    private class UpdateTestHandlerWithConsistencyFrom : ICommandHandler<UpdateTestCommand>
    {
        public async Task<ResultBox<EventOrNone>> HandleAsync(UpdateTestCommand command, ICommandContext context)
        {
            var tag = new TestTag(command.TagId);

            // Get current state
            await context.GetStateAsync<TestProjector>(tag);

            // Use ConsistencyTag.From which doesn't specify version
            var consistencyTag = ConsistencyTag.From(tag);
            return EventOrNone.EventWithTags(
                new TestEvent($"Updated-{command.TagId}", command.NewVersion),
                consistencyTag);
        }
    }
}
