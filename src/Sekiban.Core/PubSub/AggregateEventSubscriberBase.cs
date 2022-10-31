using MediatR;
using Sekiban.Core.Event;
namespace Sekiban.Core.PubSub;

public abstract class AggregateEventSubscriberBase<TEventPayload> : INotificationHandler<Event<TEventPayload>>
    where TEventPayload : IEventPayload
{
    public async Task Handle(Event<TEventPayload> ev, CancellationToken cancellationToken)
    {
        await SubscribeAggregateEventAsync(ev);
    }

    public abstract Task SubscribeAggregateEventAsync(Event<TEventPayload> ev);
}
