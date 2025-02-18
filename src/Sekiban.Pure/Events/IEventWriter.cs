namespace Sekiban.Pure.Events;

public interface IEventWriter
{
    Task SaveEvents<TEvent>(IEnumerable<TEvent> events) where TEvent : IEvent;
}