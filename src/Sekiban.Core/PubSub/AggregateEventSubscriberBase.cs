using MediatR;
using Sekiban.Core.Event;
namespace Sekiban.Core.PubSub;

public abstract class AggregateEventSubscriberBase<TEventPayload> : INotificationHandler<AggregateEvent<TEventPayload>>
    where TEventPayload : IEventPayload
{
    public async Task Handle(AggregateEvent<TEventPayload> ev, CancellationToken cancellationToken)
    {
        await SubscribeAggregateEventAsync(ev);
    }

    public abstract Task SubscribeAggregateEventAsync(AggregateEvent<TEventPayload> ev);
}
