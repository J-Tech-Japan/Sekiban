using MediatR;
using Sekiban.Core.Event;
namespace Sekiban.Core.PubSub;

public abstract class EventSubscriberBase<TEventPayload> : INotificationHandler<Event<TEventPayload>>
    where TEventPayload : IEventPayload
{
    public async Task Handle(Event<TEventPayload> ev, CancellationToken cancellationToken)
    {
        await SubscribeEventAsync(ev);
    }

    public abstract Task SubscribeEventAsync(Event<TEventPayload> ev);
}
