using Sekiban.Pure.Events;

namespace Sekiban.Pure.OrleansEventSourcing;

public interface IEventWriter
{
    Task SaveEvents<TEvent>(IEnumerable<TEvent> events) where TEvent : IEvent;
}