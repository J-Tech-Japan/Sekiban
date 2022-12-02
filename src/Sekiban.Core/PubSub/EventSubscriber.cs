using MediatR;
using Sekiban.Core.Event;

namespace Sekiban.Core.PubSub;

public class EventSubscriber<TEventPayload, TEventSubscriber> : INotificationHandler<Event<TEventPayload>>
    where TEventPayload : IEventPayloadCommon
    where TEventSubscriber : IEventSubscriber<TEventPayload>
{
    private readonly IEventSubscriber<TEventPayload> _eventSubscriber;
    public EventSubscriber(IEventSubscriber<TEventPayload> eventSubscriber)
    {
        _eventSubscriber = eventSubscriber;
    }

    public async Task Handle(Event<TEventPayload> ev, CancellationToken cancellationToken)
    {
        await _eventSubscriber.HandleEventAsync(ev);
    }

}
