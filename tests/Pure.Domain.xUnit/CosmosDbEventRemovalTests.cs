using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
namespace Pure.Domain.xUnit;

// Extension method for IEventReader to get all events
public static class EventReaderExtensions
{
    public static async Task<IReadOnlyList<IEvent>> GetAllEventsAsync(this IEventReader eventReader)
    {
        var result = await eventReader.GetEvents(EventRetrievalInfo.All);
        return result.UnwrapBox();
    }
}
public class CosmosDbEventRemovalTests : CosmosDbTestBase
{
    private readonly IEventWriter _eventWriter;
    private readonly IEventReader _eventReader;

    public CosmosDbEventRemovalTests()
    {
        _eventWriter = ServiceProvider.GetRequiredService<IEventWriter>();
        _eventReader = ServiceProvider.GetRequiredService<IEventReader>();
    }

    [Fact]
    public async Task RemoveAllEvents_ShouldClearAllEventsFromCosmosDb()
    {
        // Arrange - Add some events to CosmosDb
        var branchId = Guid.NewGuid();
        var branchCreatedEvent = new BranchCreated("TestBranch");
        var branchEvent = new Event<BranchCreated>(
            Guid.NewGuid(),
            branchCreatedEvent,
            new PartitionKeys(branchId, "Branch", "root"),
            SortableUniqueIdValue.GetCurrentIdFromUtc(),
            1,
            new EventMetadata("test", "test-correlation", "test-user"));

        var userId = Guid.NewGuid();
        var userRegisteredEvent = new UserRegistered("TestUser", "test@example.com");
        var userEvent = new Event<UserRegistered>(
            Guid.NewGuid(),
            userRegisteredEvent,
            new PartitionKeys(userId, "User", "root"),
            SortableUniqueIdValue.GetCurrentIdFromUtc(),
            1,
            new EventMetadata("test", "test-correlation", "test-user"));

        await _eventWriter.SaveEvents(new IEvent[] { branchEvent, userEvent });

        // Verify that events were added
        var events = await _eventReader.GetAllEventsAsync();
        Assert.NotEmpty(events);

        // Get the event remover
        var eventRemover = ServiceProvider.GetRequiredService<IEventRemover>();

        // Act - Remove all events
        await eventRemover.RemoveAllEvents();

        // Assert - Verify events were removed
        events = await _eventReader.GetAllEventsAsync();
        Assert.Empty(events);
    }

    [Fact]
    public async Task RemoveAllEvents_ShouldWorkWithEmptyContainer()
    {
        // Arrange - Get the event remover
        var eventRemover = ServiceProvider.GetRequiredService<IEventRemover>();

        // Make sure the container is empty
        await eventRemover.RemoveAllEvents();
        var events = await _eventReader.GetAllEventsAsync();
        Assert.Empty(events);

        // Act - Remove all events again
        await eventRemover.RemoveAllEvents();

        // Assert - Verify no errors occurred
        events = await _eventReader.GetAllEventsAsync();
        Assert.Empty(events);
    }

    [Fact]
    public async Task RemoveAllEvents_ShouldAllowAddingEventsAfterRemoval()
    {
        // Arrange - Add an event
        var branchId = Guid.NewGuid();
        var branchCreatedEvent = new BranchCreated("TestBranch");
        var branchEvent = new Event<BranchCreated>(
            Guid.NewGuid(),
            branchCreatedEvent,
            new PartitionKeys(branchId, "Branch", "root"),
            SortableUniqueIdValue.GetCurrentIdFromUtc(),
            1,
            new EventMetadata("test", "test-correlation", "test-user"));

        await _eventWriter.SaveEvents(new IEvent[] { branchEvent });

        // Verify that event was added
        var events = await _eventReader.GetAllEventsAsync();
        Assert.NotEmpty(events);

        // Get the event remover
        var eventRemover = ServiceProvider.GetRequiredService<IEventRemover>();

        // Act - Remove all events
        await eventRemover.RemoveAllEvents();

        // Verify events were removed
        events = await _eventReader.GetAllEventsAsync();
        Assert.Empty(events);

        // Add a new event after removal
        var userId = Guid.NewGuid();
        var userRegisteredEvent = new UserRegistered("TestUser", "test@example.com");
        var userEvent = new Event<UserRegistered>(
            Guid.NewGuid(),
            userRegisteredEvent,
            new PartitionKeys(userId, "User", "root"),
            SortableUniqueIdValue.GetCurrentIdFromUtc(),
            1,
            new EventMetadata("test", "test-correlation", "test-user"));

        await _eventWriter.SaveEvents(new IEvent[] { userEvent });

        // Assert - Verify new event was added
        events = await _eventReader.GetAllEventsAsync();
        Assert.NotEmpty(events);
        Assert.Single(events);
        var lastEvent = events.Last();
        Assert.IsType<Event<UserRegistered>>(lastEvent);
    }
}
