using Microsoft.Extensions.DependencyInjection;
using Pure.Domain.Generated;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Events;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Repositories;
namespace Pure.Domain.Test;

public class EventRemovalTests
{
    [Fact]
    public async Task RemoveAllEvents_ShouldClearAllEventsFromRepository()
    {
        // Arrange
        var repository = new Repository();
        var executor = new InMemorySekibanExecutor(
            PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options),
            new FunctionCommandMetadataProvider(() => "test"),
            repository,
            new ServiceCollection().BuildServiceProvider());

        // Add some events to the repository
        await executor.CommandAsync(new RegisterBranch("branch1"));
        await executor.CommandAsync(new RegisterUser("user1", "user1@example.com"));

        // Verify that events were added
        Assert.NotEmpty(executor.Repository.Events);
        Assert.Equal(2, executor.Repository.Events.Count);

        // Create an InMemoryEventWriter with the repository
        var eventWriter = new InMemoryEventWriter(repository);

        // Act
        await eventWriter.RemoveAllEvents();

        // Assert
        Assert.Empty(executor.Repository.Events);
    }

    [Fact]
    public async Task RemoveAllEvents_ShouldWorkWithEmptyRepository()
    {
        // Arrange
        var repository = new Repository();
        var eventWriter = new InMemoryEventWriter(repository);

        // Verify that repository is empty
        Assert.Empty(repository.Events);

        // Act
        await eventWriter.RemoveAllEvents();

        // Assert
        Assert.Empty(repository.Events);
    }

    [Fact]
    public async Task RemoveAllEvents_ShouldAllowAddingEventsAfterRemoval()
    {
        // Arrange
        var repository = new Repository();
        var executor = new InMemorySekibanExecutor(
            PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options),
            new FunctionCommandMetadataProvider(() => "test"),
            repository,
            new ServiceCollection().BuildServiceProvider());

        // Add some events to the repository
        await executor.CommandAsync(new RegisterBranch("branch1"));

        // Verify that events were added
        Assert.NotEmpty(executor.Repository.Events);
        Assert.Single(executor.Repository.Events);

        // Create an InMemoryEventWriter with the repository
        var eventWriter = new InMemoryEventWriter(repository);

        // Act
        await eventWriter.RemoveAllEvents();

        // Assert that events were removed
        Assert.Empty(executor.Repository.Events);

        // Add new events after removal
        await executor.CommandAsync(new RegisterUser("user1", "user1@example.com"));

        // Assert that new events were added
        Assert.NotEmpty(executor.Repository.Events);
        Assert.Single(executor.Repository.Events);
        var lastEvent = executor.Repository.Events.Last();
        Assert.IsType<Event<UserRegistered>>(lastEvent);
    }
}
