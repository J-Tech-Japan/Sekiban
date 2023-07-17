using Sekiban.Core.Events;
namespace Sekiban.Core.PubSub;

public interface IEventSubscriber<TEventPayload, TEventSubscriber> where TEventPayload : IEventPayloadCommon
    where TEventSubscriber : IEventSubscriber<TEventPayload, TEventSubscriber>
{
    public Task HandleEventAsync(Event<TEventPayload> ev);
}
