using MediatR;
using Sekiban.Core.Events;
namespace Sekiban.Core.PubSub;

// ReSharper disable once UnusedTypeParameter
public class EventSubscriber<TEventPayload, TEventSubscriber> : INotificationHandler<Event<TEventPayload>> where TEventPayload : IEventPayloadCommon
    where TEventSubscriber : IEventSubscriber<TEventPayload, TEventSubscriber>
{
    private readonly IEventSubscriber<TEventPayload, TEventSubscriber> _eventSubscriber;
    public EventSubscriber(IEventSubscriber<TEventPayload, TEventSubscriber> eventSubscriber) => _eventSubscriber = eventSubscriber;

    public async Task Handle(Event<TEventPayload> ev, CancellationToken cancellationToken)
    {
        await _eventSubscriber.HandleEventAsync(ev);
    }
}
