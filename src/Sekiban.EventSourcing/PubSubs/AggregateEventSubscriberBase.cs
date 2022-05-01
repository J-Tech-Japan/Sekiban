using MediatR;
using Sekiban.EventSourcing.AggregateEvents;

namespace Sekiban.EventSourcing.PubSubs;

public abstract class AggregateEventSubscriberBase<TEvent> : INotificationHandler<TEvent>
    where TEvent : AggregateEvent
{
    public async Task Handle(
        TEvent ev,
        CancellationToken cancellationToken)
    {
        await SubscribeAggregateEventAsync(ev);
    }

    public abstract Task SubscribeAggregateEventAsync(TEvent ev);
}
