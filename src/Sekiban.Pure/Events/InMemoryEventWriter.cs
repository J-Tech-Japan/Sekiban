using ResultBoxes;
using Sekiban.Pure.Repositories;
namespace Sekiban.Pure.Events;

/// <summary>
/// In-memory implementation of IEventWriter and IEventRemover interfaces
/// </summary>
/// <param name="repository">The repository to store events</param>
public class InMemoryEventWriter(Repository repository) : IEventWriter, IEventRemover
{
    private readonly Repository _repository = repository;

    /// <summary>
    /// Saves events to the repository
    /// </summary>
    /// <typeparam name="TEvent">The type of events to save</typeparam>
    /// <param name="events">The events to save</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public Task SaveEvents<TEvent>(IEnumerable<TEvent> events) where TEvent : IEvent
    {
        var eventsList = events.ToList();
        var result = _repository.Save(eventsList.Cast<IEvent>().ToList());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes all events from the repository
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public Task RemoveAllEvents()
    {
        var result = _repository.ClearAllEvents();
        return Task.CompletedTask;
    }
}
