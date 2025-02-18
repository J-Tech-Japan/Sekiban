using Sekiban.Pure.Repositories;
namespace Sekiban.Pure.Events;

public class InMemoryEventWriter(Repository repository): IEventWriter
{

    public Task SaveEvents<TEvent>(IEnumerable<TEvent> events) where TEvent : IEvent
    {
        var eventsList = events.ToList();
        var result = repository.Save(eventsList.Cast<IEvent>().ToList());
        return Task.CompletedTask;
    }
}
