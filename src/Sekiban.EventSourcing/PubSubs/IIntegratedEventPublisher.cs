using Sekiban.EventSourcing.IntegratedEvents;
namespace Sekiban.EventSourcing.PubSubs;

public interface IIntegratedEventPublisher
{
    Task SaveAndPublishAsync<TEvent>(TEvent ev) where TEvent : IntegratedEvent;
}
